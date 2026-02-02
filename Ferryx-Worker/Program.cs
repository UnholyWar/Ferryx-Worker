using Ferryx_Worker.Helper;
using Ferryx_Worker.SignalRService;

var builder = WebApplication.CreateBuilder(args);

// Worker port
builder.WebHost.UseUrls("http://0.0.0.0:16080");

builder.Services.AddControllers();

// Options
builder.Services.AddSingleton<FerryxHubOptions>(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("FerryxHubOptions");

    var rawToken = Environment.GetEnvironmentVariable("FERRYX_HUB_TOKEN");
    var group = Environment.GetEnvironmentVariable("FERRYX_GROUP");
    var hubUrl = Environment.GetEnvironmentVariable("FERRYX_HUB_URL");

    if (string.IsNullOrWhiteSpace(rawToken))
        throw new InvalidOperationException("FERRYX_HUB_TOKEN is required.");
    if (string.IsNullOrWhiteSpace(group))
        throw new InvalidOperationException("FERRYX_GROUP is required.");
    if (string.IsNullOrWhiteSpace(hubUrl))
        throw new InvalidOperationException("FERRYX_HUB_URL is required.");

    hubUrl = hubUrl.TrimEnd('/');

    // JWT mi? (a.b.c)
    string token;
    if (rawToken.Contains('.', StringComparison.Ordinal))
    {
        token = rawToken.Trim(); // JWT aynen kullan
        logger.LogInformation("Hub auth: using JWT from env (FERRYX_HUB_TOKEN).");
    }
    else
    {
        // KEY -> JWT üret
        token = JWTHelper.CreateJwtFromKey(rawToken.Trim());
        logger.LogInformation("Hub auth: env token looks like KEY; generated JWT from key.");
        logger.LogWarning("IMPORTANT: If you pass KEY, it MUST match hub's JwtKey exactly.");
    }

    return new FerryxHubOptions(hubUrl, token, group.Trim());
});

builder.Services.AddHostedService<OperationInitializerHostedService>();
builder.Services.AddHostedService<HubConnectorService>();

var app = builder.Build();

// Bu worker API'leri public değilse auth/https redirect gereksiz.
// app.UseHttpsRedirection();
app.MapControllers();
app.MapGet("/health", () => "OK");

app.Run();

public sealed record FerryxHubOptions(string HubUrl, string Token, string Group);
