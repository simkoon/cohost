using System.Net.Security;
using CoHost.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;

if (args.Contains("--healthcheck"))
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

var options = ServerOptions.FromConfiguration(builder.Configuration);
var tls = CertBootstrap.LoadOrCreate(Path.Combine(options.DataDir, "certs"), options.Hostnames);
var keys = SigningKeys.LoadOrCreate(Path.Combine(options.DataDir, "keys"));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(tls);
builder.Services.AddSingleton(keys);

// One HTTPS listener only; no plaintext endpoint exists (D21).
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.ListenAnyIP(options.Port, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
        listen.UseHttps(tls.Certificate);
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.Logger.LogInformation("TLS certificate SHA-256 fingerprint: {Fingerprint} ({Origin})", tls.Sha256Hex, tls.Generated ? "generated" : "loaded");
app.Logger.LogInformation("Token signing key kid: {Kid} ({Origin})", keys.Kid, keys.Generated ? "generated" : "loaded");
if (options.UsesDefaultDbPassword)
{
    app.Logger.LogWarning("Using the default database password. Set COHOST_DB_PASSWORD in .env before production use.");
}

app.MapHealthChecks("/healthz/live", new() { Predicate = _ => false });
app.MapGet("/api/keys", () => Results.Json(keys.Jwks));

await app.RunAsync();
return 0;

internal static class HealthProbe
{
    /// <summary>Docker HEALTHCHECK mode: probe our own loopback liveness endpoint (chiseled image has no curl).</summary>
    public static async Task<int> RunAsync()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("COHOST_PORT"), out var p) ? p : ServerOptions.DefaultPort;
        using var handler = new SocketsHttpHandler
        {
            // Certificate validation is disabled for the loopback self-probe only; only the status code is used.
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await client.GetAsync($"https://127.0.0.1:{port}/healthz/live");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return 1;
        }
    }
}

public partial class Program;
