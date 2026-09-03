namespace Oa.Api.Services;

public static class AnnualLeavePolicy
{
    public static decimal Calculate(DateOnly asOf, DateOnly? cumulativeWorkStartDate, int fallbackCumulativeWorkYears, DateOnly? companyHireDate)
    {
        var years = cumulativeWorkStartDate is null
            ? Math.Max(0, fallbackCumulativeWorkYears)
            : CompletedYears(cumulativeWorkStartDate.Value, asOf);
        var statutoryDays = years switch { < 1 => 0, < 10 => 5, < 20 => 10, _ => 15 };
        if (statutoryDays == 0 || companyHireDate is null || companyHireDate.Value.Year < asOf.Year) return statutoryDays;
        if (companyHireDate.Value.Year > asOf.Year || companyHireDate.Value > asOf) return 0;

        var daysInYear = DateTime.IsLeapYear(asOf.Year) ? 366 : 365;
        var remainingCalendarDays = daysInYear - companyHireDate.Value.DayOfYear + 1;
        return Math.Floor(remainingCalendarDays / (decimal)daysInYear * statutoryDays);
    }

    private static int CompletedYears(DateOnly start, DateOnly end)
    {
        if (start > end) return 0;
        var years = end.Year - start.Year;
        if (end < start.AddYears(years)) years--;
        return Math.Max(0, years);
    }
}
