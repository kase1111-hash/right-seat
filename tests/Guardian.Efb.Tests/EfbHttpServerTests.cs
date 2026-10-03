using System.Net;
using System.Net.Sockets;
using System.Text;
using Guardian.Common;
using Guardian.Core;
using Guardian.Detection;
using Guardian.Efb.Api;
using Guardian.Priority;
using Xunit;

namespace Guardian.Efb.Tests;

public class EfbHttpServerTests
{
    private const string EfbOrigin = "coui://html_ui";

    // ── Origin allow-list ──

    [Theory]
    [InlineData("coui://html_ui")]
    [InlineData("COUI://html_ui")]
    [InlineData("http://localhost:9847")]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1:8123")]
    [InlineData("http://[::1]:3000")]
    public void IsAllowedOrigin_EfbAndLocalPages_Allowed(string origin)
    {
        Assert.True(EfbHttpServer.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("https://example.com")]
    [InlineData("http://localhost.example.com")]
    [InlineData("http://127.0.0.1.nip.io")]
    [InlineData("file:///C:/efb/index.html")]
    public void IsAllowedOrigin_OtherOrigins_Refused(string? origin)
    {
        Assert.False(EfbHttpServer.IsAllowedOrigin(origin));
    }

    // ── Live server ──

    [Fact]
    public async Task Status_FromEfbOrigin_ReflectsOrigin()
    {
        var (server, client, baseUrl) = StartServer();
        using (server)
        using (client)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/status");
            request.Headers.Add("Origin", EfbOrigin);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(EfbOrigin, AllowOrigin(response));
        }
    }

    [Fact]
    public async Task SettingsPreflight_FromEfbOrigin_Allowed()
    {
        var (server, client, baseUrl) = StartServer();
        using (server)
        using (client)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, $"{baseUrl}/api/settings");
            request.Headers.Add("Origin", EfbOrigin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "content-type");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(EfbOrigin, AllowOrigin(response));
            Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        }
    }

    [Fact]
    public async Task Settings_FromEfbOrigin_Applied()
    {
        var (server, client, baseUrl) = StartServer();
        using (server)
        using (client)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/settings")
            {
                Content = new StringContent("{\"audio_enabled\":false}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Origin", EfbOrigin);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(EfbOrigin, AllowOrigin(response));
        }
    }

    [Fact]
    public async Task Status_FromUntrustedOrigin_NoCorsHeader()
    {
        var (server, client, baseUrl) = StartServer();
        using (server)
        using (client)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/status");
            request.Headers.Add("Origin", "https://example.com");

            var response = await client.SendAsync(request);

            Assert.Null(AllowOrigin(response));
        }
    }

    // ── Helpers ──

    private static (EfbHttpServer Server, HttpClient Client, string BaseUrl) StartServer()
    {
        var config = new GuardianConfig { HttpPort = GetFreePort() };
        var detection = new DetectionEngine();
        var state = new EfbStateProvider(
            config,
            new AlertPipeline(config),
            () => detection.GetRuleStates(),
            () => FlightPhase.Ground,
            () => null);

        var server = new EfbHttpServer(config, state);
        server.Start();

        var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        return (server, client, $"http://localhost:{config.HttpPort}");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? AllowOrigin(HttpResponseMessage response)
        => response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.Single()
            : null;
}
