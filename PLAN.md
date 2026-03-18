# Security Remediation Plan

Fixes for all 8 findings from the Agentic Security Audit v3.0.

---

## Fix 1 — [HIGH] XSS via innerHTML in EFB JavaScript

**File:** `efb/GuardianApp/js/guardian.js`

Add an `escapeHtml()` utility and replace every raw interpolation inside `innerHTML` template literals. Three functions are affected:

### 1a. Add escapeHtml helper (top of file, after constants)

```js
function escapeHtml(str) {
    if (typeof str !== 'string') return str;
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}
```

### 1b. Fix `renderRules()` (line 110–118)

Escape `r.rule_id` and `r.state` (used to derive `dotClass`). The `dotClass` is already safe (derived from a strict ternary), but `r.rule_id` is server-provided:

```js
// Before
<span>${r.rule_id}</span>

// After
<span>${escapeHtml(r.rule_id)}</span>
```

### 1c. Fix `renderAlertHistory()` (line 128–136)

Escape `alert.severity`, `alert.text`, and `alert.text_key`. The `sevClass` used in the class attribute also needs sanitization:

```js
// Before
<div class="alert-item ${sevClass}" onclick="showAlertDetail(${i})">
    <span ...>${formatTime(alert.timestamp)}</span>
    <span ...>${alert.severity.toUpperCase()}</span>
    <span ...>${alert.text || alert.text_key}</span>
</div>

// After
<div class="alert-item ${escapeHtml(sevClass)}" onclick="showAlertDetail(${i})">
    <span ...>${escapeHtml(formatTime(alert.timestamp))}</span>
    <span ...>${escapeHtml((alert.severity || '').toUpperCase())}</span>
    <span ...>${escapeHtml(alert.text || alert.text_key)}</span>
</div>
```

### 1d. Fix `showAlertDetail()` telemetry rendering (line 151–153)

Replace `innerHTML` with DOM element creation for telemetry entries:

```js
// Before
telDiv.innerHTML = Object.entries(alert.telemetry)
    .map(([k, v]) => `<div>${k}: ${typeof v === 'number' ? v.toFixed(2) : v}</div>`)
    .join('');

// After
telDiv.textContent = '';
Object.entries(alert.telemetry).forEach(([k, v]) => {
    const div = document.createElement('div');
    div.textContent = `${k}: ${typeof v === 'number' ? v.toFixed(2) : v}`;
    telDiv.appendChild(div);
});
```

The "No telemetry data" fallback on line 155 is safe to keep as innerHTML since it's a static string with no user data.

---

## Fix 2 — [MEDIUM] CORS allows all origins

**File:** `src/Guardian.Efb/Api/EfbHttpServer.cs` line 92

Replace the wildcard CORS origin with the specific localhost origin matching the server's own port:

```csharp
// Before
response.AddHeader("Access-Control-Allow-Origin", "*");

// After
response.AddHeader("Access-Control-Allow-Origin", $"http://localhost:{_port}");
```

This requires passing `_port` into `HandleRequest`, or capturing it as a field-derived string in the constructor. Since `_port` is already a field, the simplest approach is to precompute the allowed origin:

```csharp
// Add field
private readonly string _allowedOrigin;

// In constructor
_allowedOrigin = $"http://localhost:{_port}";

// In HandleRequest
response.AddHeader("Access-Control-Allow-Origin", _allowedOrigin);
```

---

## Fix 3 — [MEDIUM] No rate limiting on HTTP API

**File:** `src/Guardian.Efb/Api/EfbHttpServer.cs`

Add a `SemaphoreSlim` to cap concurrent request handling, and a simple token-bucket rate limiter:

```csharp
// Add fields
private readonly SemaphoreSlim _concurrencyLimiter = new(maxCount: 10);
private int _requestsThisSecond;
private DateTime _rateLimitWindowStart = DateTime.UtcNow;
private const int MaxRequestsPerSecond = 60;
```

In `HandleRequest`, wrap the logic:

```csharp
if (!_concurrencyLimiter.Wait(TimeSpan.FromMilliseconds(500)))
{
    response.StatusCode = 503;
    await RespondJson(response, new { error = "Server busy" });
    return;
}
try
{
    // Check rate limit
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

    // ... existing request handling ...
}
finally
{
    _concurrencyLimiter.Release();
}
```

Also add a `Content-Length` check on POST requests to prevent oversized payloads:

```csharp
// At top of POST /api/settings handler, before ReadBody
if (request.ContentLength64 > 4096)
{
    response.StatusCode = 413;
    await RespondJson(response, new { error = "Request body too large" });
    break;
}
```

---

## Fix 4 — [MEDIUM] Floating NuGet package versions

**Files:** All 13 `.csproj` files

Pin every `PackageReference` to an exact version. The current floating versions and their pinned replacements:

| Package | Current | Pin To |
|---------|---------|--------|
| Avalonia | 11.1.* | 11.1.0 |
| Avalonia.Desktop | 11.1.* | 11.1.0 |
| Avalonia.Themes.Fluent | 11.1.* | 11.1.0 |
| Avalonia.Fonts.Inter | 11.1.* | 11.1.0 |
| CommunityToolkit.Mvvm | 8.2.* | 8.2.2 |
| Serilog | 3.1.* | 3.1.1 |
| Serilog.Sinks.Console | 5.0.* | 5.0.1 |
| Serilog.Sinks.File | 5.0.* | 5.0.0 |
| Tomlyn | 0.17.* | 0.17.0 |
| Microsoft.NET.Test.Sdk | 17.8.* | 17.8.0 |
| xunit | 2.6.* | 2.6.6 |
| xunit.runner.visualstudio | 2.5.* | 2.5.6 |

After pinning, generate a lock file for reproducible restores:

```
dotnet restore --use-lock-file
```

Commit the resulting `packages.lock.json` files and add to `build.bat`:

```bat
dotnet restore --locked-mode
```

---

## Fix 5 — [MEDIUM] HTTP only, no TLS

**File:** `src/Guardian.Efb/Api/EfbHttpServer.cs` lines 42–43

No code change required at this stage. This is localhost-only and acceptable for a prototype. Add a comment documenting the security assumption:

```csharp
// Security note: HTTP-only is intentional — the EFB server binds exclusively
// to localhost/127.0.0.1 and is not reachable from the network. If network
// exposure is ever required, add HTTPS via HttpListener certificate binding
// or migrate to Kestrel with TLS.
_listener.Prefixes.Add($"http://localhost:{_port}/");
_listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
```

---

## Fix 6 — [LOW] Settings sensitivity field not validated

**File:** `src/Guardian.Efb/Api/EfbStateProvider.cs` line 139–143

Add a whitelist check before accepting the sensitivity value:

```csharp
// Before
if (settings.Sensitivity is not null)
{
    _config.Sensitivity = settings.Sensitivity;
    ...
}

// After
private static readonly HashSet<string> ValidSensitivities = new(StringComparer.OrdinalIgnoreCase)
    { "conservative", "standard", "sensitive" };

// In ApplySettings:
if (settings.Sensitivity is not null)
{
    if (ValidSensitivities.Contains(settings.Sensitivity))
    {
        _config.Sensitivity = settings.Sensitivity;
        Log.Information("EFB: Sensitivity set to {Value}", settings.Sensitivity);
    }
    else
    {
        Log.Warning("EFB: Rejected invalid sensitivity value: {Value}", settings.Sensitivity);
    }
}
```

---

## Fix 7 — [LOW] No .env patterns in .gitignore

**File:** `.gitignore`

Append security exclusions at the end of the file:

```gitignore
## Secrets & Credentials
.env*
*.key
*.pem
*.pfx
secrets/
```

---

## Fix 8 — [LOW] No CI/CD security gates

**File:** `.github/workflows/build.yml` (new)

Create a GitHub Actions workflow that runs on every push and PR:

```yaml
name: Build & Test
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore FlightGuardian.sln --locked-mode
      - run: dotnet build FlightGuardian.sln -c Release --no-restore
      - run: dotnet test FlightGuardian.sln -c Release --no-build --verbosity normal
```

This provides automated quality gates. Security scanning (e.g., `dotnet list package --vulnerable`) can be added as a follow-up.

---

## Implementation Order

| Priority | Fix | Effort |
|----------|-----|--------|
| 1 | Fix 1 — XSS (HIGH) | ~30 min |
| 2 | Fix 2 — CORS restriction (MEDIUM) | ~10 min |
| 3 | Fix 3 — Rate limiting (MEDIUM) | ~20 min |
| 4 | Fix 6 — Sensitivity validation (LOW) | ~5 min |
| 5 | Fix 4 — Pin NuGet versions (MEDIUM) | ~15 min |
| 6 | Fix 7 — .gitignore secrets (LOW) | ~2 min |
| 7 | Fix 5 — Document HTTP assumption (MEDIUM) | ~2 min |
| 8 | Fix 8 — CI/CD workflow (LOW) | ~10 min |
