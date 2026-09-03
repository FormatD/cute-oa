using System.Text.Json;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public static class PersonnelProfileProvisioning
{
    public static PersonnelProfileRecord Create(
        UserRecord user,
        string employeeNumber,
        DateOnly hireDate,
        string employmentType,
        string personnelStatus,
        DateOnly? probationEndDate,
        DateOnly? cumulativeWorkStartDate)
    {
        return new PersonnelProfileRecord
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            EmployeeNumber = employeeNumber,
            EmploymentType = employmentType,
            PersonnelStatus = personnelStatus,
            HireDate = hireDate,
            ProbationEndDate = probationEndDate,
            CumulativeWorkStartDate = cumulativeWorkStartDate
        };
    }

    public static PersonnelEventRecord CreateInitialEvent(PersonnelProfileRecord profile, string actorId, string actorName, string summary)
    {
        return new PersonnelEventRecord
        {
            TenantId = profile.TenantId,
            UserId = profile.UserId,
            EventType = "IMPORTED",
            EffectiveDate = profile.HireDate,
            Summary = summary,
            ChangedBy = actorId,
            ChangedByName = actorName,
            SnapshotJson = JsonSerializer.Serialize(new
            {
                profile.EmployeeNumber,
                profile.EmploymentType,
                profile.PersonnelStatus,
                profile.HireDate,
                profile.ProbationEndDate,
                profile.CumulativeWorkStartDate
            })
        };
    }

    public static string GenerateDemoEmployeeNumber(string userId)
    {
        var suffix = new string(userId.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        var value = $"EMP-{suffix}";
        return value[..Math.Min(32, value.Length)];
    }

    public static int CompletedYears(DateOnly start, DateOnly end)
    {
        var years = end.Year - start.Year;
        if (end < start.AddYears(years)) years--;
        return Math.Clamp(years, 0, 60);
    }
}
