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
        var operationPath =
            Environment.GetEnvironmentVariable("FERRYX_OPERATION_PATH")
            ?? "/ferryx/operation/run.sh";

        var operationDir =
            Path.GetDirectoryName(operationPath)
            ?? "/ferryx/operation";

        // 1) Directory yoksa oluştur
        if (!Directory.Exists(operationDir))
        {
            Directory.CreateDirectory(operationDir);
            _logger.LogInformation("[operation] directory created: {Dir}", operationDir);
        }

        // 2) run.sh yoksa örnekli oluştur
        if (!File.Exists(operationPath))
        {
            File.WriteAllText(
                operationPath,
                """
                #!/usr/bin/env bash

                echo "=== Ferryx Operation Script ==="
                echo "Target={{ferryx_Target}}"
                echo "Tag={{ferryx_Tag}}"
                echo "Env={{ferryx_Env}}"

                echo "Meta.test={{ferryx_Meta.test}}"

                # Buraya kendi operasyonunu yaz:
                # docker compose pull
                # docker compose up -d
                # curl -X POST https://webhook.example.com/deploy
                """
            );

            _logger.LogInformation("[operation] run.sh created with example placeholders: {Path}", operationPath);
        }
        else
        {
            _logger.LogInformation("[operation] run.sh exists, nothing to do");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
