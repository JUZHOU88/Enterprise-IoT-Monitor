using System.Security.Cryptography;
using System.Text;
using IntegratedMonitor.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseWebRoot("wwwroot");

var configuredKey = Environment.GetEnvironmentVariable("MONITOR_DEVICE_KEY");
if (string.IsNullOrWhiteSpace(configuredKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Set MONITOR_DEVICE_KEY outside Development.");
    configuredKey = "local-demo-key";
    Console.WriteLine("WARNING: Development-only device key. Do not expose this server online.");
}
var deviceKey = configuredKey;

builder.Services.AddSingleton(new TelemetryStore());
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", storage = "in-memory" }));

app.MapPost("/api/telemetry", (HttpRequest request, TelemetryInput input, TelemetryStore store) =>
{
    if (!request.Headers.TryGetValue("X-Device-Key", out var supplied) ||
        !ConstantTimeMatch(supplied.ToString(), deviceKey))
        return Results.Unauthorized();

    try
    {
        var result = store.Ingest(input);
        return Results.Json(new
        {
            accepted = true, created = result.Created,
            eventId = result.Reading.EventId, alert = result.Reading.Alert
        }, statusCode: result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 429); }
});

app.MapGet("/api/devices", (TelemetryStore store) => Results.Ok(store.GetDevices()));
app.MapGet("/api/devices/{deviceId}/readings",
    (string deviceId, int? limit, TelemetryStore store) =>
{
    try
    {
        var result = store.GetReadings(deviceId, limit ?? 50);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    catch (ArgumentOutOfRangeException)
    {
        return Results.BadRequest(new { error = "limit must be 1..200" });
    }
});

app.Run();

static bool ConstantTimeMatch(string supplied, string expected)
{
    var given = Encoding.UTF8.GetBytes(supplied);
    var target = Encoding.UTF8.GetBytes(expected);
    return given.Length == target.Length &&
        CryptographicOperations.FixedTimeEquals(given, target);
}
