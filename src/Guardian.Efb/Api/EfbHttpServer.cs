using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Guardian.Common;
using Guardian.Core;
using Guardian.Detection;
using Guardian.Priority;
using Serilog;

namespace Guardian.Efb.Api;

/// <summary>
/// Lightweight HTTP server exposing the Guardian API for the EFB tablet app.
///
/// Endpoints:
///   GET  /api/status      — current connection, phase, alert counts, rules
///   GET  /api/alerts       — all session alerts (newest first)
///   GET  /api/alerts/active — only currently active (unresolved) alerts
///   POST /api/settings     — update sterile cockpit, audio, sensitivity
///   POST /api/silence      — silence critical alarm
///
/// Runs on configurable port (default 9847).
/// CORS headers are set to allow EFB sandbox fetch() calls (see <see cref="IsAllowedOrigin"/>).
/// </summary>
public sealed class EfbHttpServer : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<EfbHttpServer>();

    private readonly HttpListener _listener;
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    // State providers (set by the host application)
    private readonly EfbStateProvider _state;

    // Distinct untrusted origins already logged (capped so it can't grow without bound)
    private readonly ConcurrentDictionary<string, byte> _loggedRejectedOrigins = new();
    private const int MaxLoggedRejectedOrigins = 32;

    // Rate limiting
    private readonly SemaphoreSlim _concurrencyLimiter = new(10);
    private int _requestsThisSecond;
    private DateTime _rateLimitWindowStart = DateTime.UtcNow;
    private const int MaxRequestsPerSecond = 60;
    private const long MaxRequestBodyBytes = 4096;

    public EfbHttpServer(GuardianConfig config, EfbStateProvider stateProvider)
    {
        _port = config.HttpPort;
        _state = stateProvider;
        _listener = new HttpListener();
        // Security note: HTTP-only is intentional — the EFB server binds exclusively
        // to localhost/127.0.0.1 and is not reachable from the network. If network
        // exposure is ever required, add HTTPS via HttpListener certificate binding
        // or migrate to Kestrel with TLS.
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
        Log.Information("EFB HTTP server listening on port {Port}", _port);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
        _listenTask?.Wait(TimeSpan.FromSeconds(3));
        Log.Information("EFB HTTP server stopped");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in HTTP listen loop");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers for EFB sandbox — reflect the caller's origin only if trusted
        var origin = request.Headers["Origin"];
        if (IsAllowedOrigin(origin))
        {
            response.AddHeader("Access-Control-Allow-Origin", origin!);
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
        }
        else if (origin is not null
                 && _loggedRejectedOrigins.Count < MaxLoggedRejectedOrigins
                 && _loggedRejectedOrigins.TryAdd(origin, 0))
        {
            Log.Warning("EFB API request from untrusted origin {Origin} — CORS access denied", origin);
        }
        response.AddHeader("Vary", "Origin");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        // Rate limiting
        if (!_concurrencyLimiter.Wait(TimeSpan.FromMilliseconds(500)))
        {
            response.StatusCode = 503;
            await RespondJson(response, new { error = "Server busy" });
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            if ((now - _rateLimitWindowStart).TotalSeconds >= 1)
            {
                Interlocked.Exchange(ref _requestsThisSecond, 0);
                _rateLimitWindowStart = now;
            }
            if (Interlocked.Increment(ref _requestsThisSecond) > MaxRequestsPerSecond)
            {
                response.StatusCode = 429;
                await RespondJson(response, new { error = "Rate limit exceeded" });
                return;
            }
            var path = request.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case "/api/status":
                    await RespondJson(response, _state.GetStatus());
                    break;

                case "/api/alerts":
                    await RespondJson(response, _state.GetAllAlerts());
                    break;

                case "/api/alerts/active":
                    await RespondJson(response, _state.GetActiveAlerts());
                    break;

                case "/api/settings" when request.HttpMethod == "POST":
                    if (request.ContentLength64 > MaxRequestBodyBytes)
                    {
                        response.StatusCode = 413;
                        await RespondJson(response, new { error = "Request body too large" });
                        break;
                    }
                    var body = await ReadBody(request);
                    SettingsUpdateDto? settings = null;
                    try
                    {
                        settings = JsonSerializer.Deserialize<SettingsUpdateDto>(body);
                    }
                    catch (JsonException)
                    {
                        // Fall through to null check below
                    }
                    if (settings is not null)
                    {
                        _state.ApplySettings(settings);
                        response.StatusCode = 200;
                        await RespondJson(response, new { ok = true });
                    }
                    else
                    {
                        response.StatusCode = 400;
                        await RespondJson(response, new { error = "Invalid settings JSON" });
                    }
                    break;

                case "/api/silence" when request.HttpMethod == "POST":
                    _state.SilenceCriticalAlarm();
                    await RespondJson(response, new { ok = true });
                    break;

                default:
                    response.StatusCode = 404;
                    await RespondJson(response, new { error = "Not found" });
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling request {Method} {Path}", request.HttpMethod, request.Url?.AbsolutePath);
            response.StatusCode = 500;
            await RespondJson(response, new { error = "Internal server error" });
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Whether a browser page from <paramref name="origin"/> may read API responses.
    /// Allowed: the MSFS EFB tablet (pages served from coui://) and pages on this
    /// machine (localhost / loopback, any port — e.g. an EFB dev server).
    /// Arbitrary websites open in the pilot's browser are refused.
    /// </summary>
    public static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme.Equals("coui", StringComparison.OrdinalIgnoreCase))
            return true;

        return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback;
    }

    private static async Task RespondJson(HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task<string> ReadBody(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        Stop();
        ((IDisposable)_listener).Dispose();
    }
}
