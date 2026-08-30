using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public interface IWorkCalendar
{
    bool IsWorkingDay(DateOnly date);
}

public sealed class ChinaWorkCalendar : IWorkCalendar
{
    private static readonly HashSet<DateOnly> Holidays = BuildDates(
        (2026, 1, 1, 3), (2026, 2, 15, 23), (2026, 4, 4, 6),
        (2026, 5, 1, 5), (2026, 6, 19, 21), (2026, 9, 25, 27), (2026, 10, 1, 7));
    private static readonly HashSet<DateOnly> MakeupWorkdays =
    [
        new(2026, 1, 4), new(2026, 2, 14), new(2026, 2, 28), new(2026, 5, 9), new(2026, 9, 20), new(2026, 10, 10)
    ];

    public bool IsWorkingDay(DateOnly date) => MakeupWorkdays.Contains(date) || (!Holidays.Contains(date) && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

    private static HashSet<DateOnly> BuildDates(params (int Year, int Month, int Start, int End)[] ranges)
    {
        var dates = new HashSet<DateOnly>();
        foreach (var (year, month, start, end) in ranges)
            for (var day = start; day <= end; day++) dates.Add(new DateOnly(year, month, day));
        return dates;
    }
}

public sealed record WorkCalendarEntry(DateOnly Date, bool IsWorkingDay, string? Note, string Source);
public sealed record UpdateWorkCalendarRequest(bool IsWorkingDay, string? Note);

public sealed class WorkCalendarService(OaDbContext db, DemoData? directory = null) : IWorkCalendar
{
    private const string TenantId = "demo";
    private readonly ChinaWorkCalendar baseline = new();
    private readonly DemoData data = directory ?? new DemoData();

    public bool IsWorkingDay(DateOnly date) =>
        db.WorkCalendarEntries.SingleOrDefault(item => item.TenantId == TenantId && item.Date == date)?.IsWorkingDay ?? baseline.IsWorkingDay(date);

    public IReadOnlyList<WorkCalendarEntry> List(int year) =>
        db.WorkCalendarEntries.Where(item => item.TenantId == TenantId && item.Date.Year == year)
            .OrderBy(item => item.Date).Select(item => new WorkCalendarEntry(item.Date, item.IsWorkingDay, item.Note, "企业维护")).ToList();

    public ServiceResult<WorkCalendarEntry> Update(Employee actor, DateOnly date, UpdateWorkCalendarRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.CalendarManage)) return ServiceResult<WorkCalendarEntry>.Failure("无工作日历维护权限。", "AUTH_002");
        if (request.Note?.Trim().Length > 200) return ServiceResult<WorkCalendarEntry>.Failure("日历备注不能超过 200 个字符。");
        var record = db.WorkCalendarEntries.SingleOrDefault(item => item.TenantId == TenantId && item.Date == date);
        if (record is null) { record = new WorkCalendarRecord { TenantId = TenantId, Date = date }; db.WorkCalendarEntries.Add(record); }
        record.IsWorkingDay = request.IsWorkingDay;
        record.Note = request.Note?.Trim();
        record.UpdatedBy = actor.Id;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = "WORK_CALENDAR_UPDATED", ResourceType = "WorkCalendar", ResourceId = date.ToString("yyyy-MM-dd"), Summary = request.IsWorkingDay ? "设置为工作日" : "设置为非工作日" });
        db.SaveChanges();
        return ServiceResult<WorkCalendarEntry>.Success(new WorkCalendarEntry(date, record.IsWorkingDay, record.Note, "企业维护"));
    }
}
