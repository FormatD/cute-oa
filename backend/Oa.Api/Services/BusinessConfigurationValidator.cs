using System.Text.Json;
using Oa.Api.Domain;

namespace Oa.Api.Services;

public static class BusinessConfigurationValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ServiceResult<string> ValidateAndNormalize(string domain, string contentJson, DemoData? data = null)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
            return ServiceResult<string>.Failure("配置内容不能为空。", "CONFIG_001");

        try
        {
            return domain switch
            {
                ConfigurationDomains.Leave => ValidateLeave(contentJson),
                ConfigurationDomains.Expense => ValidateExpense(contentJson),
                ConfigurationDomains.Travel => ValidateTravel(contentJson),
                ConfigurationDomains.Procurement => ValidateProcurement(contentJson, data),
                ConfigurationDomains.Seal => ValidateSeal(contentJson, data),
                ConfigurationDomains.Dictionary => ValidateDictionary(contentJson),
                _ => ServiceResult<string>.Failure($"不支持的业务领域：{domain}。", "CONFIG_001")
            };
        }
        catch (JsonException ex)
        {
            return ServiceResult<string>.Failure($"配置 JSON 格式非法：{ex.Message}", "CONFIG_001");
        }
    }

    private static ServiceResult<string> ValidateLeave(string json)
    {
        var config = JsonSerializer.Deserialize<LeavePolicyConfig>(json, JsonOptions);
        if (config is null || config.LeaveTypes.Count == 0)
            return ServiceResult<string>.Failure("假勤配置必须包含至少一种假别规则。", "CONFIG_001");

        foreach (var type in config.LeaveTypes)
        {
            if (string.IsNullOrWhiteSpace(type.Name))
                return ServiceResult<string>.Failure("假别名称不能为空。", "CONFIG_001");
            if (type.MinUnit <= 0 || type.MinUnit > 30 || (type.MinUnit != 0.5m && type.MinUnit != 1.0m && type.MinUnit % 0.5m != 0))
                return ServiceResult<string>.Failure($"假别【{type.Name}】最小申请单位不合法，应为 0.5 天或 1 天倍数。", "CONFIG_001");
            if (type.RequiresAttachment && type.AttachmentThresholdDays is not null && type.AttachmentThresholdDays <= 0)
                return ServiceResult<string>.Failure($"假别【{type.Name}】证明附件门槛天数必须大于 0。", "CONFIG_001");
        }

        if (config.AnnualLeaveBonus.Tier1BonusDays < 0 ||
            config.AnnualLeaveBonus.Tier2BonusDays < 0 ||
            config.AnnualLeaveBonus.Tier3BonusDays < 0)
        {
            return ServiceResult<string>.Failure("企业年假上浮规则不能为负数，法定年假最低标准不得被降低。", "CONFIG_001");
        }

        if (!config.AnnualLeaveBonus.LegalMinStandardProtected)
        {
            return ServiceResult<string>.Failure("中国法定年假最低标准受法律保护，必须勾选法定标准保护。", "CONFIG_001");
        }

        if (config.CompTimeValidityDays <= 0)
            return ServiceResult<string>.Failure("调休有效期天数必须大于 0。", "CONFIG_001");

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }

    private static ServiceResult<string> ValidateExpense(string json)
    {
        var config = JsonSerializer.Deserialize<ExpensePolicyConfig>(json, JsonOptions);
        if (config is null || config.Categories.Count == 0)
            return ServiceResult<string>.Failure("费用配置必须包含至少一个费用类别。", "CONFIG_001");

        foreach (var cat in config.Categories)
        {
            if (string.IsNullOrWhiteSpace(cat.Name))
                return ServiceResult<string>.Failure("费用类别名称不能为空。", "CONFIG_001");
            if (cat.SingleLimit is not null && cat.SingleLimit <= 0)
                return ServiceResult<string>.Failure($"费用类别【{cat.Name}】单笔限额必须大于 0。", "CONFIG_001");
        }

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }

    private static ServiceResult<string> ValidateTravel(string json)
    {
        var config = JsonSerializer.Deserialize<TravelPolicyConfig>(json, JsonOptions);
        if (config is null)
            return ServiceResult<string>.Failure("出差配置内容不能为空。", "CONFIG_001");

        foreach (var standard in config.Standards)
        {
            if (standard.HotelDailyLimit < 0)
                return ServiceResult<string>.Failure($"标准【{standard.CityTier} - {standard.Rank}】住宿限额不能为负数。", "CONFIG_001");
            if (standard.MealDailyAllowance < 0)
                return ServiceResult<string>.Failure($"标准【{standard.CityTier} - {standard.Rank}】餐补标准不能为负数。", "CONFIG_001");
        }

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }

    private static ServiceResult<string> ValidateProcurement(string json, DemoData? data)
    {
        var config = JsonSerializer.Deserialize<ProcurementPolicyConfig>(json, JsonOptions);
        if (config is null || config.Categories.Count == 0)
            return ServiceResult<string>.Failure("采购配置必须包含至少一个采购类别。", "CONFIG_001");

        if (config.QuoteAttachmentThreshold < 0)
            return ServiceResult<string>.Failure("报价附件门槛金额不能为负数。", "CONFIG_001");

        if (!string.IsNullOrWhiteSpace(config.DefaultPurchaserUserId) && data is not null)
        {
            var user = data.FindEmployee(config.DefaultPurchaserUserId);
            if (user is null || user.Status != "ACTIVE")
                return ServiceResult<string>.Failure($"默认采购负责人【{config.DefaultPurchaserUserId}】不存在或已停用。", "CONFIG_001");
        }

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }

    private static ServiceResult<string> ValidateSeal(string json, DemoData? data)
    {
        var config = JsonSerializer.Deserialize<SealPolicyConfig>(json, JsonOptions);
        if (config is null || config.Seals.Count == 0)
            return ServiceResult<string>.Failure("用章配置必须包含至少一枚印章台账。", "CONFIG_001");

        foreach (var seal in config.Seals)
        {
            if (string.IsNullOrWhiteSpace(seal.Name))
                return ServiceResult<string>.Failure("印章名称不能为空。", "CONFIG_001");
            if (seal.MaxOutDays < 0)
                return ServiceResult<string>.Failure($"印章【{seal.Name}】外带最长天数不能为负数。", "CONFIG_001");
            if (!string.IsNullOrWhiteSpace(seal.CustodianUserId) && data is not null)
            {
                var user = data.FindEmployee(seal.CustodianUserId);
                if (user is null || user.Status != "ACTIVE")
                    return ServiceResult<string>.Failure($"印章【{seal.Name}】保管人【{seal.CustodianUserId}】不存在或已停用。", "CONFIG_001");
            }
        }

        foreach (var category in config.DocumentCategories)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
                return ServiceResult<string>.Failure("文件类别名称不能为空。", "CONFIG_001");
            var risk = category.RiskLevel.ToUpperInvariant();
            if (risk is not ("LOW" or "MEDIUM" or "HIGH"))
                return ServiceResult<string>.Failure($"文件类别【{category.Name}】风险等级必须为 LOW、MEDIUM 或 HIGH。", "CONFIG_001");
            category.RiskLevel = risk;
        }

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }

    private static ServiceResult<string> ValidateDictionary(string json)
    {
        var config = JsonSerializer.Deserialize<DictionaryConfig>(json, JsonOptions);
        if (config is null)
            return ServiceResult<string>.Failure("字典配置内容不能为空。", "CONFIG_001");

        var duplicateCodes = config.Items.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateCodes.Count > 0)
            return ServiceResult<string>.Failure($"字典项编码重复：{string.Join(", ", duplicateCodes)}。", "CONFIG_001");

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }
}
