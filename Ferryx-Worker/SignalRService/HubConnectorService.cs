using Microsoft.AspNetCore.SignalR.Client;


namespace Ferryx_Worker.SignalRService
{
    public sealed class HubConnectorService : BackgroundService
    {
        private readonly FerryxHubOptions _opt;
        private readonly ILogger<HubConnectorService> _logger;

        private Microsoft.AspNetCore.SignalR.Client.HubConnection? _conn;

        public HubConnectorService(FerryxHubOptions opt, ILogger<HubConnectorService> logger)
        {
            _opt = opt;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var builder = new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder();

            _conn = builder
                .WithUrl(_opt.HubUrl+"/hubs/deploy", (Microsoft.AspNetCore.Http.Connections.Client.HttpConnectionOptions o) =>
                {
                    // Server: access_token query string'ten alıyor
                    o.AccessTokenProvider = () => Task.FromResult(_opt.Token)!;
                })
                .WithAutomaticReconnect()
                .Build();

            _conn.On<DeployRequest>("NewDeploy", req =>
            {
                _logger.LogInformation("NewDeploy received: Target={Target}, Tag={Tag}, Env={Env}",
                    req.Target, req.Tag, req.Env);
            });

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to hub: {HubUrl}", _opt.HubUrl);
                    await _conn.StartAsync(stoppingToken);

                    _logger.LogInformation("Connected. ConnectionId={Id}", _conn.ConnectionId);

                    await _conn.InvokeAsync("JoinGroup", _opt.Group, cancellationToken: stoppingToken);
                    _logger.LogInformation("Joined group: {Group}", _opt.Group);

                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect/join group. Retrying in 5s...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_conn is not null)
            {
                await _conn.StopAsync(cancellationToken);
                await _conn.DisposeAsync();
            }
            await base.StopAsync(cancellationToken);
        }

        public sealed class DeployRequest
        {
            public string Env { get; init; } = "";
            public string Target { get; init; } = "";
            public string? Tag { get; init; }
            public Dictionary<string, object>? Meta { get; init; }
        }
    }

    public sealed record FerryxHubOptions(string HubUrl, string Token, string Group);
}
