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

        private HubConnection? _conn;

        public HubConnectorService(FerryxHubOptions opt, ILogger<HubConnectorService> logger)
        {
            _opt = opt;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _conn = new HubConnectionBuilder()
                .WithUrl(_opt.HubUrl + "/hubs/deploy", o =>
                {
                    // Server: access_token query string'ten alıyor
                    o.AccessTokenProvider = () => Task.FromResult(_opt.Token)!;
                })
                .WithAutomaticReconnect()
                .Build();

            // Kritik: Group path'i sanitize et + opDir var mı oluştur + unique temp + Group placeholder
            // Not: Eğer senin pakette On<T>(..., Func<T,Task>) overload yoksa, aşağıdaki blok compile etmez.
            // O durumda alttaki alternatif "Task.Run" yorumunu kullan.
            _conn.On<DeployRequest>("NewDeploy", async req =>
            {
                var group = SanitizeGroup(_opt.Group);

                var opDir = Path.Combine("/ferryx/operation", group);
                Directory.CreateDirectory(opDir);

                var runPath = Path.Combine(opDir, "run.sh");
                if (!File.Exists(runPath))
                {
                    _logger.LogWarning("run.sh there is no: {Path}", runPath);
                    return;
                }

                var id = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
                var tempPath = Path.Combine(opDir, $"runtemp.{id}.sh");

                try
                {
                    var script = await File.ReadAllTextAsync(runPath, stoppingToken);

                    script = script.Replace("{{ferryx_Env}}", req.Env ?? "", StringComparison.Ordinal);
                    script = script.Replace("{{ferryx_Target}}", req.Target ?? "", StringComparison.Ordinal);
                    script = script.Replace("{{ferryx_Tag}}", req.Tag ?? "", StringComparison.Ordinal);
                    script = script.Replace("{{ferryx_Group}}", group, StringComparison.Ordinal);

                    script = ReplaceMetaPlaceholders(script, req.Meta);

                    await File.WriteAllTextAsync(tempPath, script, stoppingToken);

                    if (OperatingSystem.IsLinux())
                        await RunProcessAsync("/bin/chmod", new[] { "+x", tempPath }, _logger, stoppingToken);

                    _logger.LogInformation(
                        "Deploy run: Group={Group}, Target={Target}, Tag={Tag}, Env={Env}",
                        group, req.Target, req.Tag, req.Env
                    );

                    var exit = await RunProcessAsync("/bin/bash", new[] { tempPath }, _logger, stoppingToken);
                    _logger.LogInformation("runtemp exit code: {ExitCode}", exit);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // shutdown esnasında sessiz geç
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NewDeploy run failed");
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Temp silinemedi: {Temp}", tempPath);
                    }
                }
            });

            /*
            // Eğer üstteki async handler compile etmezse bunu kullan:
            _conn.On<DeployRequest>("NewDeploy", req =>
            {
                _ = Task.Run(async () =>
                {
                    // yukarıdaki handler içeriğinin aynısını buraya koy
                }, stoppingToken);
            });
            */

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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
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
                try { await _conn.StopAsync(cancellationToken); } catch { /* ignore */ }
                try { await _conn.DisposeAsync(); } catch { /* ignore */ }
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

        private static async Task<int> RunProcessAsync(
            string fileName,
            IEnumerable<string> args,
            ILogger logger,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) logger.LogInformation("[sh] {Line}", e.Data);
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) logger.LogWarning("[sh] {Line}", e.Data);
            };

            if (!p.Start())
                throw new InvalidOperationException("Process start edilemedi.");

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            await p.WaitForExitAsync(ct);
            return p.ExitCode;
        }

        private static string SanitizeGroup(string g)
        {
            if (string.IsNullOrWhiteSpace(g)) return "default";

            var cleaned = new string(g
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_')
                .ToArray());

            return cleaned.Length == 0 ? "default" : cleaned;
        }
    }

}
