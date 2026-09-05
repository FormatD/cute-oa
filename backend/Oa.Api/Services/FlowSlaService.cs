using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public static class FlowSlaAlertTypes
{
    public const string DueSoon = "DUE_SOON";
    public const string Overdue = "OVERDUE";
    public const string Escalated = "ESCALATED";
}

public sealed class FlowSlaService(OaDbContext db, DemoData data)
{
    private const string TenantId = IdentityDefaults.TenantId;

    public int DispatchAlerts(DateTimeOffset? instant = null)
    {
        var now = instant ?? DateTimeOffset.UtcNow;
        var candidates = db.FlowTaskSlas
            .Where(item => item.TenantId == TenantId && item.ActivatedAt != null && item.DueAt != null && item.CompletedAt == null && item.CancelledAt == null)
            .Join(db.FlowInstances.Where(item => item.TenantId == TenantId), sla => sla.FlowInstanceId, flow => flow.Id, (sla, flow) => new { Sla = sla, Flow = flow })
            .ToList();
        var created = 0;
        foreach (var candidate in candidates)
        {
            var alertType = AlertType(candidate.Sla, now);
            if (alertType is null) continue;
            var recipient = alertType == FlowSlaAlertTypes.Escalated ? ResolveEscalationRecipient(candidate.Sla) : data.FindEmployee(candidate.Sla.AssigneeId);
            if (recipient is null || recipient.Status != "ACTIVE") recipient = ProcessAdministrator();
            if (recipient is null) continue;
            if (db.FlowSlaAlertDeliveries.Any(item => item.TenantId == TenantId && item.TaskId == candidate.Sla.TaskId && item.RecipientId == recipient.Id && item.AlertType == alertType)) continue;

            var (title, content) = Message(alertType, candidate.Flow.BusinessNumber, candidate.Sla.AssigneeName, candidate.Sla.DueAt!.Value);
            db.Notifications.Add(new NotificationRecord
            {
                TenantId = TenantId,
                RecipientId = recipient.Id,
                Type = $"FLOW_SLA_{alertType}",
                Title = title,
                Content = content,
                ResourceType = ResourceType(candidate.Flow.BusinessType),
                ResourceId = candidate.Flow.BusinessId.ToString()
            });
            db.FlowSlaAlertDeliveries.Add(new FlowSlaAlertDeliveryRecord
            {
                TenantId = TenantId,
                FlowTaskSlaId = candidate.Sla.Id,
                TaskId = candidate.Sla.TaskId,
                RecipientId = recipient.Id,
                AlertType = alertType,
                DeliveredAt = now
            });
            created++;
        }
        if (created > 0) db.SaveChanges();
        return created;
    }

    private Employee? ResolveEscalationRecipient(FlowTaskSlaRecord task)
    {
        var assignee = data.FindEmployee(task.AssigneeId);
        var target = task.EscalationTarget;
        return target switch
        {
            "DIRECT_MANAGER" => assignee?.ManagerId is null ? null : data.FindEmployee(assignee.ManagerId),
            "PROCESS_ADMIN" => ProcessAdministrator(),
            _ when target.StartsWith("ROLE:", StringComparison.Ordinal) => data.ActiveEmployees.FirstOrDefault(item => data.HasRole(item, target[5..])),
            _ when target.StartsWith("USER:", StringComparison.Ordinal) => data.FindEmployee(target[5..]),
            _ => null
        };
    }

    private Employee? ProcessAdministrator() => data.ActiveEmployees.FirstOrDefault(item => data.HasPermission(item, OaPermissions.ProcessManage));

    private static string ResourceType(string businessType) => businessType switch
    {
        "Leave" => "LeaveRequest",
        "Expense" => "ExpenseClaim",
        "Travel" => "TravelRequest",
        "Purchase" => "PurchaseRequest",
        "Seal" => "SealRequest",
        _ => businessType
    };

    private static string? AlertType(FlowTaskSlaRecord task, DateTimeOffset now)
    {
        var dueAt = task.DueAt!.Value;
        if (now >= dueAt.AddHours(task.EscalateAfterHours)) return FlowSlaAlertTypes.Escalated;
        if (now >= dueAt) return FlowSlaAlertTypes.Overdue;
        if (task.ReminderBeforeHours > 0 && now >= dueAt.AddHours(-task.ReminderBeforeHours)) return FlowSlaAlertTypes.DueSoon;
        return null;
    }

    private static (string Title, string Content) Message(string alertType, string number, string assigneeName, DateTimeOffset dueAt) => alertType switch
    {
        FlowSlaAlertTypes.DueSoon => ("审批任务即将到期", $"{number} 将于 {ChinaTime(dueAt)} 到期，请及时处理。"),
        FlowSlaAlertTypes.Overdue => ("审批任务已逾期", $"{number} 已于 {ChinaTime(dueAt)} 逾期，当前办理人：{assigneeName}。"),
        _ => ("审批任务逾期升级", $"{number} 已逾期并触发升级，当前办理人：{assigneeName}，原截止时间：{ChinaTime(dueAt)}。")
    };

    private static string ChinaTime(DateTimeOffset value) => value.ToOffset(BusinessTime.ChinaOffset).ToString("yyyy-MM-dd HH:mm");
}

public sealed class FlowSlaWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<FlowSlaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("FlowSla:Enabled", true)) return;
        var intervalMinutes = Math.Clamp(configuration.GetValue("FlowSla:IntervalMinutes", 15), 1, 1_440);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var created = scope.ServiceProvider.GetRequiredService<FlowSlaService>().DispatchAlerts();
                if (created > 0) logger.LogInformation("Dispatched {Count} flow SLA alerts.", created);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Flow SLA alert dispatch failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
