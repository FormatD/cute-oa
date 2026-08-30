namespace Oa.Api.Services;

public sealed class ContractAlertWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ContractAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("ContractAlerts:Enabled", true)) return;
        var intervalMinutes = Math.Clamp(configuration.GetValue("ContractAlerts:IntervalMinutes", 360), 5, 1_440);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var created = scope.ServiceProvider.GetRequiredService<EmploymentContractService>().DispatchDueAlerts();
                if (created > 0) logger.LogInformation("Dispatched {Count} employment contract expiry alerts.", created);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Employment contract alert dispatch failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
