namespace Oa.Api.Services;

public sealed class PersonnelCaseAlertWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PersonnelCaseAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("PersonnelCaseAlerts:Enabled", true)) return;
        var intervalMinutes = Math.Clamp(configuration.GetValue("PersonnelCaseAlerts:IntervalMinutes", 60), 5, 1_440);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var created = scope.ServiceProvider.GetRequiredService<PersonnelCaseService>().DispatchTaskAlerts();
                if (created > 0) logger.LogInformation("Dispatched {Count} personnel case task alerts.", created);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Personnel case task alert dispatch failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
