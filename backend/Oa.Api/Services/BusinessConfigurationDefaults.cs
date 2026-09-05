using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public static class BusinessConfigurationDefaults
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static BusinessConfigurationRecord? ResolveEffectiveConfig(OaDbContext? db, string domain, string code, string tenantId = "demo", DateTimeOffset? asOf = null)
    {
        if (db is null) return null;
        try
        {
            var targetTime = (asOf ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var record = db.BusinessConfigurations.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Domain == domain && item.Code == code)
                .Where(item => item.Status == ConfigurationStatus.Effective || item.Status == ConfigurationStatus.Scheduled)
                .Where(item => item.EffectiveFrom <= targetTime && (item.EffectiveTo == null || item.EffectiveTo > targetTime))
                .OrderByDescending(item => item.EffectiveFrom)
                .FirstOrDefault();

            if (record is null)
            {
                EnsureDefaultConfigurations(db, tenantId);
                record = db.BusinessConfigurations.AsNoTracking()
                    .Where(item => item.TenantId == tenantId && item.Domain == domain && item.Code == code)
                    .Where(item => item.Status == ConfigurationStatus.Effective)
                    .OrderByDescending(item => item.EffectiveFrom)
                    .FirstOrDefault();
            }

            return record;
        }
        catch
        {
            return null;
        }
    }

    public static LeavePolicyConfig CreateDefaultLeavePolicy() => new()
    {
        LeaveTypes =
        [
            new() { Type = "Annual", Name = "年假", IsEnabled = true, MinUnit = 0.5m, RequiresAttachment = false },
            new() { Type = "Personal", Name = "事假", IsEnabled = true, MinUnit = 0.5m, RequiresAttachment = false },
            new() { Type = "Sick", Name = "病假", IsEnabled = true, MinUnit = 0.5m, RequiresAttachment = true, AttachmentThresholdDays = 2.0m },
            new() { Type = "CompTime", Name = "调休", IsEnabled = true, MinUnit = 0.5m, RequiresAttachment = false },
            new() { Type = "Marriage", Name = "婚假", IsEnabled = true, MinUnit = 1.0m, RequiresAttachment = false },
            new() { Type = "Maternity", Name = "产假", IsEnabled = true, MinUnit = 1.0m, RequiresAttachment = false },
            new() { Type = "Paternity", Name = "陪产假", IsEnabled = true, MinUnit = 1.0m, RequiresAttachment = false },
            new() { Type = "Bereavement", Name = "丧假", IsEnabled = true, MinUnit = 1.0m, RequiresAttachment = false }
        ],
        AllowCrossYear = false,
        CompTimeValidityDays = 365,
        AnnualLeaveBonus = new()
        {
            LegalMinStandardProtected = true,
            Tier1BonusDays = 0,
            Tier2BonusDays = 0,
            Tier3BonusDays = 0
        }
    };

    public static ExpensePolicyConfig CreateDefaultExpensePolicy() => new()
    {
        Categories =
        [
            new() { Name = "交通", IsEnabled = true, SingleLimit = 5000, RequiresReceipt = true, RequiresReasonWhenExceeded = true, BlockWhenExceeded = false },
            new() { Name = "住宿", IsEnabled = true, SingleLimit = 3000, RequiresReceipt = true, RequiresReasonWhenExceeded = true, BlockWhenExceeded = false },
            new() { Name = "餐饮招待", IsEnabled = true, SingleLimit = 2000, RequiresReceipt = true, RequiresReasonWhenExceeded = true, BlockWhenExceeded = false },
            new() { Name = "办公", IsEnabled = true, SingleLimit = 10000, RequiresReceipt = true, RequiresReasonWhenExceeded = true, BlockWhenExceeded = false },
            new() { Name = "通讯", IsEnabled = true, SingleLimit = 1000, RequiresReceipt = true, RequiresReasonWhenExceeded = true, BlockWhenExceeded = false },
            new() { Name = "培训", IsEnabled = true, SingleLimit = 20000, RequiresReceipt = true, RequiresReasonWhenExceeded = true, BlockWhenExceeded = false },
            new() { Name = "其他", IsEnabled = true, SingleLimit = 5000, RequiresReceipt = true, RequiresReasonWhenExceeded = true, BlockWhenExceeded = false }
        ]
    };

    public static TravelPolicyConfig CreateDefaultTravelPolicy() => new()
    {
        CityTiers =
        [
            new() { TierName = "一线城市", Cities = ["北京", "上海", "广州", "深圳"] },
            new() { TierName = "二线城市", Cities = ["杭州", "南京", "成都", "武汉", "西安", "苏州", "天津", "重庆"] },
            new() { TierName = "其他城市", Cities = [] }
        ],
        EmployeeRanks = ["员工", "部门负责人", "总经理"],
        Standards =
        [
            new() { CityTier = "一线城市", Rank = "员工", HotelDailyLimit = 450, MealDailyAllowance = 100, TransportationStandard = "高铁二等座/飞机经济舱" },
            new() { CityTier = "一线城市", Rank = "部门负责人", HotelDailyLimit = 650, MealDailyAllowance = 150, TransportationStandard = "高铁一等座/飞机经济舱" },
            new() { CityTier = "一线城市", Rank = "总经理", HotelDailyLimit = 900, MealDailyAllowance = 200, TransportationStandard = "高铁商务座/飞机公务舱" },
            new() { CityTier = "二线城市", Rank = "员工", HotelDailyLimit = 350, MealDailyAllowance = 80, TransportationStandard = "高铁二等座/飞机经济舱" },
            new() { CityTier = "二线城市", Rank = "部门负责人", HotelDailyLimit = 500, MealDailyAllowance = 120, TransportationStandard = "高铁一等座/飞机经济舱" },
            new() { CityTier = "二线城市", Rank = "总经理", HotelDailyLimit = 700, MealDailyAllowance = 160, TransportationStandard = "高铁商务座/飞机公务舱" },
            new() { CityTier = "其他城市", Rank = "员工", HotelDailyLimit = 260, MealDailyAllowance = 60, TransportationStandard = "高铁二等座/飞机经济舱" },
            new() { CityTier = "其他城市", Rank = "部门负责人", HotelDailyLimit = 380, MealDailyAllowance = 100, TransportationStandard = "高铁一等座/飞机经济舱" },
            new() { CityTier = "其他城市", Rank = "总经理", HotelDailyLimit = 500, MealDailyAllowance = 130, TransportationStandard = "高铁商务座/飞机公务舱" }
        ]
    };

    public static ProcurementPolicyConfig CreateDefaultProcurementPolicy() => new()
    {
        Categories =
        [
            new() { Name = "办公用品", IsEnabled = true },
            new() { Name = "IT设备", IsEnabled = true },
            new() { Name = "软件服务", IsEnabled = true },
            new() { Name = "行政物资", IsEnabled = true },
            new() { Name = "市场物料", IsEnabled = true },
            new() { Name = "生产物料", IsEnabled = true },
            new() { Name = "专业服务", IsEnabled = true },
            new() { Name = "其他", IsEnabled = true }
        ],
        QuoteAttachmentThreshold = 5000m,
        AmountTiers =
        [
            new() { Name = "常规采购", MaxAmount = 10000m },
            new() { Name = "重要采购", MaxAmount = 50000m },
            new() { Name = "重大采购", MaxAmount = null }
        ],
        DefaultPurchaserUserId = "u-admin",
        RequiresAcceptance = true,
        AcceptanceRoleOrAssignee = "PURCHASE_MANAGE"
    };

    public static SealPolicyConfig CreateDefaultSealPolicy() => new()
    {
        Seals =
        [
            new() { Name = "公章", SealType = "公章", CustodianUserId = "u-admin", IsEnabled = true, AllowOut = true, MaxOutDays = 7 },
            new() { Name = "合同专用章", SealType = "合同专用章", CustodianUserId = "u-admin", IsEnabled = true, AllowOut = true, MaxOutDays = 7 },
            new() { Name = "财务专用章", SealType = "财务专用章", CustodianUserId = "u-lin", IsEnabled = true, AllowOut = false, MaxOutDays = 0 },
            new() { Name = "法人章", SealType = "法人章", CustodianUserId = "u-wang", IsEnabled = true, AllowOut = false, MaxOutDays = 0 },
            new() { Name = "人事专用章", SealType = "人事专用章", CustodianUserId = "u-sun", IsEnabled = true, AllowOut = true, MaxOutDays = 5 },
            new() { Name = "其他", SealType = "其他", CustodianUserId = "u-admin", IsEnabled = true, AllowOut = true, MaxOutDays = 3 }
        ],
        DocumentCategories =
        [
            new() { Name = "合同协议", IsEnabled = true, RiskLevel = "MEDIUM" },
            new() { Name = "招投标文件", IsEnabled = true, RiskLevel = "MEDIUM" },
            new() { Name = "公文函件", IsEnabled = true, RiskLevel = "LOW" },
            new() { Name = "资质证明", IsEnabled = true, RiskLevel = "LOW" },
            new() { Name = "财务报表", IsEnabled = true, RiskLevel = "MEDIUM" },
            new() { Name = "人事材料", IsEnabled = true, RiskLevel = "LOW" },
            new() { Name = "其他", IsEnabled = true, RiskLevel = "LOW" }
        ],
        RiskRules = new()
        {
            HighRiskMetric = 3m,
            MediumRiskMetric = 2m,
            LowRiskMetric = 1m
        }
    };

    public static DictionaryConfig CreateDefaultAnnouncementTypeDict() => new()
    {
        Items =
        [
            new() { Code = "COMPANY_NEWS", Name = "公司新闻", SortOrder = 10, IsEnabled = true },
            new() { Code = "HR_NOTICE", Name = "人事通知", SortOrder = 20, IsEnabled = true },
            new() { Code = "POLICY_RELEASE", Name = "制度发布", SortOrder = 30, IsEnabled = true },
            new() { Code = "SECURITY_ALERT", Name = "安全通报", SortOrder = 40, IsEnabled = true },
            new() { Code = "OTHER", Name = "其他公告", SortOrder = 50, IsEnabled = true }
        ]
    };

    public static DictionaryConfig CreateDefaultContractTypeDict() => new()
    {
        Items =
        [
            new() { Code = "FIXED_TERM", Name = "固定期限劳动合同", SortOrder = 10, IsEnabled = true },
            new() { Code = "OPEN_ENDED", Name = "无固定期限劳动合同", SortOrder = 20, IsEnabled = true },
            new() { Code = "PROJECT_BASED", Name = "以完成一定工作任务为期限", SortOrder = 30, IsEnabled = true },
            new() { Code = "INTERNSHIP", Name = "实习协议", SortOrder = 40, IsEnabled = true },
            new() { Code = "LABOR_DISPATCH", Name = "劳务派遣协议", SortOrder = 50, IsEnabled = true }
        ]
    };

    public static DictionaryConfig CreateDefaultAttachmentTypeDict() => new()
    {
        Items =
        [
            new() { Code = "INVOICE", Name = "发票报销凭证", SortOrder = 10, IsEnabled = true },
            new() { Code = "SICK_LEAVE_PROOF", Name = "病假病情证明/病历", SortOrder = 20, IsEnabled = true },
            new() { Code = "PURCHASE_QUOTE", Name = "采购比价/报价单", SortOrder = 30, IsEnabled = true },
            new() { Code = "CONTRACT_DRAFT", Name = "合同正文/草案", SortOrder = 40, IsEnabled = true },
            new() { Code = "IDENTITY_PROOF", Name = "身份及资质证明", SortOrder = 50, IsEnabled = true },
            new() { Code = "OTHER", Name = "其他证明附件", SortOrder = 60, IsEnabled = true }
        ]
    };

    public static DictionaryConfig CreateDefaultApprovalCommentPresetDict() => new()
    {
        Items =
        [
            new() { Code = "AGREE_DEFAULT", Name = "同意，按流程办理", SortOrder = 10, IsEnabled = true },
            new() { Code = "AGREE_CONDITIONAL", Name = "同意，请严格按照公司制度执行", SortOrder = 20, IsEnabled = true },
            new() { Code = "REVISE_INFO", Name = "信息不完整，请补充相关凭证后重新提交", SortOrder = 30, IsEnabled = true },
            new() { Code = "REJECT_BUDGET", Name = "超出部门预算，暂不予批准", SortOrder = 40, IsEnabled = true },
            new() { Code = "REJECT_COMPLIANCE", Name = "不符合规章制度要求，予以驳回", SortOrder = 50, IsEnabled = true }
        ]
    };

    public static IReadOnlyList<(string Domain, string Code, string Name, string Description, string ContentJson)> GetSeedConfigs() =>
    [
        (ConfigurationDomains.Leave, "LeavePolicy", "假勤管理规则", "定义公司假别、最小申请单位、病假附件门槛及年假计算规则", JsonSerializer.Serialize(CreateDefaultLeavePolicy(), JsonOptions)),
        (ConfigurationDomains.Expense, "ExpensePolicy", "费用报销规则", "定义费用类别启停、单笔限额标准及超标要求", JsonSerializer.Serialize(CreateDefaultExpensePolicy(), JsonOptions)),
        (ConfigurationDomains.Travel, "TravelPolicy", "差旅管理标准", "定义差旅城市等级、员工职级及住宿、交通与餐补报销标准", JsonSerializer.Serialize(CreateDefaultTravelPolicy(), JsonOptions)),
        (ConfigurationDomains.Procurement, "ProcurementPolicy", "采购管理规则", "定义采购类别、报价附件门槛及默认采购负责人", JsonSerializer.Serialize(CreateDefaultProcurementPolicy(), JsonOptions)),
        (ConfigurationDomains.Seal, "SealPolicy", "用章管理规则", "定义印章台账、保管人、外带限制及文件类别风险评级", JsonSerializer.Serialize(CreateDefaultSealPolicy(), JsonOptions)),
        (ConfigurationDomains.Dictionary, "AnnouncementType", "公告类型字典", "公司公告分类字典项维护", JsonSerializer.Serialize(CreateDefaultAnnouncementTypeDict(), JsonOptions)),
        (ConfigurationDomains.Dictionary, "ContractType", "合同类型字典", "劳动用工及人事合同分类字典项维护", JsonSerializer.Serialize(CreateDefaultContractTypeDict(), JsonOptions)),
        (ConfigurationDomains.Dictionary, "AttachmentType", "附件类型字典", "系统支持与推荐的附件凭证分类", JsonSerializer.Serialize(CreateDefaultAttachmentTypeDict(), JsonOptions)),
        (ConfigurationDomains.Dictionary, "ApprovalCommentPreset", "常用审批意见字典", "审批流常用快速回复与驳回理由短语", JsonSerializer.Serialize(CreateDefaultApprovalCommentPresetDict(), JsonOptions))
    ];

    public static void EnsureDefaultConfigurations(OaDbContext db, string tenantId = "demo")
    {
        try
        {
            if (db.Database.IsNpgsql())
            {
                var tableExists = db.Database.SqlQueryRaw<int>("""
                    SELECT 1 FROM information_schema.tables 
                    WHERE table_name = 'business_configuration' AND table_schema = CURRENT_SCHEMA()
                """).Any();
                if (!tableExists) return;
            }
        }
        catch
        {
            return;
        }

        var seedList = GetSeedConfigs();
        var now = DateTimeOffset.UtcNow;
        var defaultEffectiveDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var anyAdded = false;
        foreach (var (domain, code, name, description, contentJson) in seedList)
        {
            var exists = db.BusinessConfigurations.Any(item => item.TenantId == tenantId && item.Domain == domain && item.Code == code);
            if (!exists)
            {
                db.BusinessConfigurations.Add(new BusinessConfigurationRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Domain = domain,
                    Code = code,
                    Name = name,
                    Description = description,
                    Version = 1,
                    Status = ConfigurationStatus.Effective,
                    EffectiveFrom = defaultEffectiveDate,
                    EffectiveTo = null,
                    ContentJson = contentJson,
                    CreatedBy = "system",
                    CreatedByName = "系统初始化",
                    CreatedAt = now,
                    UpdatedBy = "system",
                    UpdatedByName = "系统初始化",
                    UpdatedAt = now,
                    PublishedBy = "system",
                    PublishedByName = "系统初始化",
                    PublishedAt = now,
                    ConcurrencyVersion = 1
                });
                anyAdded = true;
            }
        }

        if (anyAdded)
        {
            db.SaveChanges();
        }
    }
}

