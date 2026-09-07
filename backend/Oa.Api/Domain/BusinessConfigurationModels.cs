using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oa.Api.Domain;

public static class ConfigurationDomains
{
    public const string Leave = "Leave";
    public const string Expense = "Expense";
    public const string Travel = "Travel";
    public const string Procurement = "Procurement";
    public const string Seal = "Seal";
    public const string Dictionary = "Dictionary";

    public static IReadOnlyList<string> All { get; } = [Leave, Expense, Travel, Procurement, Seal, Dictionary];
}

public static class ConfigurationStatus
{
    public const string Draft = "DRAFT";
    public const string Scheduled = "SCHEDULED";
    public const string Effective = "EFFECTIVE";
    public const string Retired = "RETIRED";

    public static IReadOnlyList<string> All { get; } = [Draft, Scheduled, Effective, Retired];
}

public sealed record BusinessConfigurationView(
    Guid Id,
    string TenantId,
    string Domain,
    string Code,
    string Name,
    string? Description,
    int Version,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ContentJson,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string UpdatedBy,
    string UpdatedByName,
    DateTimeOffset UpdatedAt,
    string? PublishedBy,
    string? PublishedByName,
    DateTimeOffset? PublishedAt,
    int ConcurrencyVersion,
    int ReferenceCount = 0);

public sealed record BusinessConfigurationListItem(
    Guid Id,
    string TenantId,
    string Domain,
    string Code,
    string Name,
    string? Description,
    int Version,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string UpdatedByName,
    DateTimeOffset UpdatedAt,
    string? PublishedByName,
    DateTimeOffset? PublishedAt,
    int ConcurrencyVersion,
    int ReferenceCount);

public sealed record ConfigurationVersionSummary(
    Guid Id,
    int Version,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? PublishedByName,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    int ReferenceCount);

public sealed record CreateBusinessConfigurationRequest(
    string Domain,
    string Code,
    string Name,
    string? Description,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ContentJson);

public sealed record UpdateBusinessConfigurationRequest(
    string Name,
    string? Description,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ContentJson,
    int ConcurrencyVersion);

public sealed record PublishBusinessConfigurationRequest(
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null,
    int? ConcurrencyVersion = null);

public sealed record RetireBusinessConfigurationRequest(
    DateTimeOffset? EffectiveTo = null,
    int? ConcurrencyVersion = null);

public sealed record BusinessConfigurationFilterQuery(
    string? Domain = null,
    string? Code = null,
    string? Keyword = null,
    string? Status = null,
    DateTimeOffset? EffectiveAsOf = null,
    int Page = 1,
    int PageSize = 20);

// --- Strongly-typed domain configuration payloads ---

public sealed class LeaveTypePolicyConfig
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public decimal MinUnit { get; set; } = 0.5m;
    public bool RequiresAttachment { get; set; }
    public decimal? AttachmentThresholdDays { get; set; }
}

public sealed class AnnualLeaveBonusConfig
{
    public bool LegalMinStandardProtected { get; set; } = true;
    public decimal Tier1BonusDays { get; set; }
    public decimal Tier2BonusDays { get; set; }
    public decimal Tier3BonusDays { get; set; }
}

public sealed class LeavePolicyConfig
{
    public List<LeaveTypePolicyConfig> LeaveTypes { get; set; } = [];
    public bool AllowCrossYear { get; set; }
    public int CompTimeValidityDays { get; set; } = 365;
    public AnnualLeaveBonusConfig AnnualLeaveBonus { get; set; } = new();
}

public sealed class ExpenseCategoryPolicyConfig
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public decimal? SingleLimit { get; set; }
    public bool RequiresReceipt { get; set; } = true;
    public bool RequiresReasonWhenExceeded { get; set; } = true;
    public bool BlockWhenExceeded { get; set; }
}

public sealed class ExpensePolicyConfig
{
    public List<ExpenseCategoryPolicyConfig> Categories { get; set; } = [];
    public bool BlockWhenExceeded { get; set; }
}

public sealed class TravelCityTierConfig
{
    public string TierName { get; set; } = string.Empty;
    public List<string> Cities { get; set; } = [];
}

public sealed class TravelStandardItemConfig
{
    public string CityTier { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public decimal HotelDailyLimit { get; set; }
    public decimal MealDailyAllowance { get; set; }
    public string TransportationStandard { get; set; } = string.Empty;
    public bool BlockWhenExceeded { get; set; }
}

public sealed class TravelPolicyConfig
{
    public List<TravelCityTierConfig> CityTiers { get; set; } = [];
    public List<string> EmployeeRanks { get; set; } = [];
    public List<TravelStandardItemConfig> Standards { get; set; } = [];
    public bool BlockWhenExceeded { get; set; }
}

public sealed class ProcurementCategoryPolicyConfig
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
}

public sealed class ProcurementAmountTierConfig
{
    public string Name { get; set; } = string.Empty;
    public decimal? MaxAmount { get; set; }
}

public sealed class ProcurementPolicyConfig
{
    public List<ProcurementCategoryPolicyConfig> Categories { get; set; } = [];
    public decimal QuoteAttachmentThreshold { get; set; } = 5000m;
    public List<ProcurementAmountTierConfig> AmountTiers { get; set; } = [];
    public string DefaultPurchaserUserId { get; set; } = string.Empty;
    public bool RequiresAcceptance { get; set; } = true;
    public string AcceptanceRoleOrAssignee { get; set; } = string.Empty;
    public bool BlockWhenExceeded { get; set; }
}

public sealed class SealRegistryItemConfig
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public string SealType { get; set; } = string.Empty;
    public string CustodianUserId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool AllowOut { get; set; } = true;
    public int MaxOutDays { get; set; } = 7;
}

public sealed class SealDocumentCategoryConfig
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public string RiskLevel { get; set; } = "LOW";
}

public sealed class SealRiskRulesConfig
{
    public decimal HighRiskMetric { get; set; } = 3m;
    public decimal MediumRiskMetric { get; set; } = 2m;
    public decimal LowRiskMetric { get; set; } = 1m;
}

public sealed class SealPolicyConfig
{
    public List<SealRegistryItemConfig> Seals { get; set; } = [];
    public List<SealDocumentCategoryConfig> DocumentCategories { get; set; } = [];
    public SealRiskRulesConfig RiskRules { get; set; } = new();
}

public sealed class DictionaryItemConfig
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Description { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public int? Version { get; set; }
}

public sealed class DictionaryConfig
{
    public List<DictionaryItemConfig> Items { get; set; } = [];
}

// Rule Snapshot models for historical document persistence
public sealed class BusinessRuleSnapshot
{
    public Guid ConfigVersionId { get; set; }
    public int ConfigVersionNumber { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ResolvedAt { get; set; }
    public JsonElement Parameters { get; set; }
}

public sealed class EffectiveLeaveTypeOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal MinUnit { get; set; } = 0.5m;
    public bool RequiresAttachment { get; set; }
    public decimal? AttachmentThresholdDays { get; set; }
}

public sealed class EffectiveExpenseCategoryOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal? SingleLimit { get; set; }
    public bool RequiresReceipt { get; set; } = true;
    public bool RequiresReasonWhenExceeded { get; set; } = true;
    public bool BlockWhenExceeded { get; set; }
}

public sealed class EffectiveProcurementCategoryOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class EffectiveProcurementAmountTierOption
{
    public string Name { get; set; } = string.Empty;
    public decimal? MaxAmount { get; set; }
}

public sealed class EffectiveSealOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public string SealType { get; set; } = string.Empty;
    public bool AllowOut { get; set; } = true;
    public int MaxOutDays { get; set; } = 7;
}

public sealed class EffectiveSealDocCategoryOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public string RiskLevel { get; set; } = "LOW";
}

public sealed class EffectiveDictionaryOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class EffectiveBusinessConfigurationBundle
{
    public List<EffectiveLeaveTypeOption> LeaveTypes { get; set; } = [];
    public List<EffectiveExpenseCategoryOption> ExpenseCategories { get; set; } = [];
    public List<TravelCityTierConfig> TravelCityTiers { get; set; } = [];
    public List<string> TravelEmployeeRanks { get; set; } = [];
    public List<TravelStandardItemConfig> TravelStandards { get; set; } = [];
    public List<EffectiveProcurementCategoryOption> ProcurementCategories { get; set; } = [];
    public List<EffectiveProcurementAmountTierOption> ProcurementAmountTiers { get; set; } = [];
    public decimal ProcurementQuoteThreshold { get; set; } = 5000m;
    public List<EffectiveSealOption> Seals { get; set; } = [];
    public List<EffectiveSealDocCategoryOption> SealDocumentCategories { get; set; } = [];
    public List<EffectiveDictionaryOption> ContractTypes { get; set; } = [];
    public List<EffectiveDictionaryOption> AttachmentTypes { get; set; } = [];
    public List<EffectiveDictionaryOption> ApprovalCommentPresets { get; set; } = [];
    public List<EffectiveDictionaryOption> AnnouncementTypes { get; set; } = [];
}
