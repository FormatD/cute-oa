namespace Oa.Api.Services;

public static class BusinessTime
{
    public static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public static DateOnly ChinaToday(DateTimeOffset? now = null) =>
        DateOnly.FromDateTime((now ?? DateTimeOffset.UtcNow).ToOffset(ChinaOffset).DateTime);

    public static DateOnly ChinaMonth(DateTimeOffset? now = null)
    {
        var today = ChinaToday(now);
        return new DateOnly(today.Year, today.Month, 1);
    }
}
