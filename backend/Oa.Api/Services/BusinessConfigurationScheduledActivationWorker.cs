using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Oa.Api.Services;

public sealed class BusinessConfigurationScheduledActivationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BusinessConfigurationScheduledActivationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("BusinessConfigActivation:Enabled", true)) return;
        var intervalSeconds = Math.Clamp(configuration.GetValue("BusinessConfigActivation:IntervalSeconds", 15), 5, 300);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var activated = scope.ServiceProvider.GetRequiredService<BusinessConfigurationService>().ActivateScheduledConfigurations();
                if (activated > 0)
                {
                    logger.LogInformation("Activated {Count} scheduled business configuration versions.", activated);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Scheduled business configuration activation failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
