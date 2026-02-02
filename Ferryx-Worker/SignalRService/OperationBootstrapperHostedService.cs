using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ferryx_Worker.SignalRService;

public sealed class OperationInitializerHostedService : IHostedService
{
    private readonly ILogger<OperationInitializerHostedService> _logger;

    public OperationInitializerHostedService(
        ILogger<OperationInitializerHostedService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var group = Environment.GetEnvironmentVariable("FERRYX_GROUP") ?? "default";

        var opDir = Path.Combine("/ferryx/operation", group);
        var runPath = Path.Combine(opDir, "run.sh");

        if (!Directory.Exists(opDir))
        {
            Directory.CreateDirectory(opDir);
            _logger.LogInformation("[operation] created: {Dir}", opDir);
        }

        if (!File.Exists(runPath))
        {
            File.WriteAllText(
                runPath,
                """
            #!/usr/bin/env bash

            echo "Target={{ferryx_Target}}"
            echo "Tag={{ferryx_Tag}}"
            echo "Env={{ferryx_Env}}"
            echo "Meta.test={{ferryx_Meta.test}}"
            """
            );

            _logger.LogInformation("[operation] run.sh created: {Path}", runPath);
        }

        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
