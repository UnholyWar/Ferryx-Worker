using Ferryx_Worker.Helper;
using Ferryx_Worker.SignalRService;

var builder = WebApplication.CreateBuilder(args);

// Uygulama portu (16000+)
builder.WebHost.UseUrls("http://0.0.0.0:16080");

builder.Services.AddControllers();

// Zorunlu ayarlar (el altında dursun)
builder.Services.AddSingleton<FerryxHubOptions>(_ =>
{
    var rawToken = Environment.GetEnvironmentVariable("FERRYX_HUB_TOKEN");
    var group = Environment.GetEnvironmentVariable("FERRYX_GROUP");
    var hubUrl = Environment.GetEnvironmentVariable("FERRYX_HUB_URL");

    if (string.IsNullOrWhiteSpace(rawToken))
        throw new InvalidOperationException("FERRYX_HUB_TOKEN is required.");
    if (string.IsNullOrWhiteSpace(group))
        throw new InvalidOperationException("FERRYX_GROUP is required.");
    if (string.IsNullOrWhiteSpace(hubUrl))
        throw new InvalidOperationException("FERRYX_HUB_URL is required.");

    // 🔑 Eğer token JWT değilse → KEY kabul et → JWT üret
    var token = rawToken.Contains('.')
        ? rawToken
        : JWTHelper.CreateJwtFromKey(rawToken);

    var jwt=Uri.EscapeDataString(token);
    return new FerryxHubOptions(hubUrl, token, group, jwt);

});





builder.Services.AddHostedService<OperationInitializerHostedService>();
// SignalR bağlanma servisi
builder.Services.AddHostedService<HubConnectorService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => "OK");

app.Run();


public sealed record FerryxHubOptions(string HubUrl, string Token, string Group, string JWT);
