using PollyVsHttpClient;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddHttpClient("standard");
builder.Services.AddHttpClient("short-timeout", client => client.Timeout = TimeSpan.FromMilliseconds(500));

builder.Services.AddHostedService<PollyVsHttpClientHostedService>();

var app = builder.Build();

app.MapGet("/fast", () =>
{
    return new { Healtly = true, Timestamp = DateTimeOffset.Now.ToString("O")};
});

app.MapGet("/slow", async () =>
{
    await Task.Delay(5_000);
    return new { Healtly = true, Timestamp = DateTimeOffset.Now.ToString("O")};
});

app.Run();
