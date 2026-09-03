namespace Oa.Api.Domain;

public static class EmploymentTypes
{
    public const string FullTime = "FULL_TIME";
    public const string PartTime = "PART_TIME";
    public const string Intern = "INTERN";
    public const string Contractor = "CONTRACTOR";
    public static IReadOnlyList<string> All { get; } = [FullTime, PartTime, Intern, Contractor];
}

public static class PersonnelStatuses
{
    public const string Probation = "PROBATION";
    public const string Active = "ACTIVE";
    public const string Offboarding = "OFFBOARDING";
    public const string Terminated = "TERMINATED";
    public static IReadOnlyList<string> All { get; } = [Probation, Active, Offboarding, Terminated];
}

public sealed record PersonnelEventView(
    Guid Id,
    string EventType,
    DateOnly EffectiveDate,
    string Summary,
    string ChangedBy,
    string ChangedByName,
    DateTimeOffset CreatedAt);

public sealed record PersonnelProfileView(
    string UserId,
    string EmployeeNumber,
    string Name,
    string DepartmentId,
    string DepartmentName,
    string? PositionId,
    string? PositionName,
    string? ManagerId,
    string? ManagerName,
    string WorkEmail,
    string WorkPhone,
    string WorkLocation,
    string EmploymentType,
    string PersonnelStatus,
    string AccountStatus,
    DateOnly HireDate,
    DateOnly? ProbationEndDate,
    DateOnly? RegularizedDate,
    DateOnly? CumulativeWorkStartDate,
    int CumulativeWorkYears,
    DateOnly? DepartureDate,
    string? DepartureReason,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PersonnelEventView> Events);

public sealed record UpdatePersonnelProfileRequest(
    string DepartmentId,
    string? PositionId,
    string? ManagerId,
    string? WorkEmail,
    string? WorkPhone,
    string? WorkLocation,
    string EmploymentType,
    string PersonnelStatus,
    DateOnly HireDate,
    DateOnly? ProbationEndDate,
    DateOnly? RegularizedDate,
    DateOnly? CumulativeWorkStartDate,
    DateOnly? DepartureDate,
    string? DepartureReason,
    int Version,
    DateOnly EffectiveDate,
    string ChangeReason);
