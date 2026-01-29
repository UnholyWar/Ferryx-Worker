using Ferryx_Worker.SignalRService;

var builder = WebApplication.CreateBuilder(args);

// Uygulama portu (16000+)
builder.WebHost.UseUrls("http://0.0.0.0:16080");

builder.Services.AddControllers();

// Zorunlu ayarlar (el altında dursun)
builder.Services.AddSingleton<FerryxHubOptions>(_ =>
{
    var token = Environment.GetEnvironmentVariable("FERRYX_HUB_TOKEN");
    var group = Environment.GetEnvironmentVariable("FERRYX_GROUP");
    var hubUrl = Environment.GetEnvironmentVariable("FERRYX_HUB_URL");

    if (string.IsNullOrWhiteSpace(token))
        throw new InvalidOperationException("FERRYX_HUB_TOKEN is required.");
    if (string.IsNullOrWhiteSpace(group))
        throw new InvalidOperationException("FERRYX_GROUP is required.");
    if (string.IsNullOrWhiteSpace(hubUrl))
        throw new InvalidOperationException("FERRYX_HUB_URL is required.");

    return new FerryxHubOptions(hubUrl, token, group);
});

// SignalR bağlanma servisi
builder.Services.AddHostedService<HubConnectorService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => "OK");

app.Run();

public sealed record FerryxHubOptions(string HubUrl, string Token, string Group);
