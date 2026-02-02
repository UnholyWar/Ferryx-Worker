using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;


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

            _conn.On<DeployRequest>("NewDeploy", async req =>
            {
                var opDir = Environment.GetEnvironmentVariable("FERRYX_OPERATION_DIR") ?? "/ferryx/operation";
                var runPath = Path.Combine(opDir, "run.sh");

                if (!File.Exists(runPath))
                {
                    _logger.LogWarning("run.sh yok: {Path}", runPath);
                    return;
                }

                // temp dosya adı
                var id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                var tempPath = Path.Combine(opDir, $"runtemp.{id}.sh");

                try
                {
                    // run.sh oku -> temp'e yaz (replace ile)
                    var script = await File.ReadAllTextAsync(runPath);

                    script = script.Replace("{{ferryx_Env}}", req.Env ?? "", StringComparison.Ordinal);
                    script = script.Replace("{{ferryx_Target}}", req.Target ?? "", StringComparison.Ordinal);
                    script = script.Replace("{{ferryx_Tag}}", req.Tag ?? "", StringComparison.Ordinal);

                    script = ReplaceMetaPlaceholders(script, req.Meta);

                    await File.WriteAllTextAsync(tempPath, script);

                    // +x (linux)
                    if (OperatingSystem.IsLinux())
                    {
                        await RunProcessAsync("/bin/chmod", new[] { "+x", tempPath }, _logger);
                    }

                    // ÇALIŞTIR
                    _logger.LogInformation("Deploy run: Target={Target}, Tag={Tag}, Env={Env}", req.Target, req.Tag, req.Env);
                    var exit = await RunProcessAsync("/bin/bash", new[] { tempPath }, _logger);

                    _logger.LogInformation("runtemp exit code: {ExitCode}", exit);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NewDeploy run failed");
                }
                finally
                {
                    // temp sil
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Temp silinemedi: {Temp}", tempPath); }
                }
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













        private static string ReplaceMetaPlaceholders(string script, Dictionary<string, object>? meta)
        {
            if (meta is null) return script;

            // {{ferryx_Meta.test}}  {{ferryx_Meta.build.number}} vb.
            var rx = new Regex(@"\{\{ferryx_Meta\.([^}]+)\}\}", RegexOptions.Compiled);
            return rx.Replace(script, m =>
            {
                var path = m.Groups[1].Value;
                return TryGetMetaValue(meta, path) ?? "";
            });
        }

        private static string? TryGetMetaValue(Dictionary<string, object> meta, string dottedPath)
        {
            object? current = meta;

            foreach (var part in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current is Dictionary<string, object> dict)
                {
                    if (!dict.TryGetValue(part, out var next)) return null;
                    current = next;
                }
                else if (current is JsonElement je)
                {
                    if (je.ValueKind != JsonValueKind.Object) return null;
                    if (!je.TryGetProperty(part, out var prop)) return null;
                    current = prop;
                }
                else return null;
            }

            return current switch
            {
                null => null,
                JsonElement je => je.ToString(),
                _ => current.ToString()
            };
        }

        private static async Task<int> RunProcessAsync(string fileName, IEnumerable<string> args, ILogger logger)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = new Process { StartInfo = psi };

            p.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("[sh] {Line}", e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("[sh] {Line}", e.Data); };

            if (!p.Start()) throw new InvalidOperationException("Process start edilemedi.");

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            await p.WaitForExitAsync();
            return p.ExitCode;
        }

    }

    public sealed record FerryxHubOptions(string HubUrl, string Token, string Group);
}
