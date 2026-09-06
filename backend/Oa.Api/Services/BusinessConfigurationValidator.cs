using System.Text.Json;
using Oa.Api.Domain;

namespace Oa.Api.Services;

public static class BusinessConfigurationValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static ServiceResult<string> ValidateAndNormalize(string domain, string contentJson, DemoData? data = null, string? previousJson = null)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
            return ServiceResult<string>.Failure("配置内容不能为空。", "CONFIG_001");

        try
        {
            return domain switch
            {
                ConfigurationDomains.Leave => ValidateLeave(contentJson),
                ConfigurationDomains.Expense => ValidateExpense(contentJson, previousJson),
                ConfigurationDomains.Travel => ValidateTravel(contentJson),
                ConfigurationDomains.Procurement => ValidateProcurement(contentJson, data, previousJson),
                ConfigurationDomains.Seal => ValidateSeal(contentJson, data, previousJson),
                ConfigurationDomains.Dictionary => ValidateDictionary(contentJson, previousJson),
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

    private static ServiceResult<string> ValidateExpense(string json, string? previousJson)
    {
        var config = JsonSerializer.Deserialize<ExpensePolicyConfig>(json, JsonOptions);
        if (config is null || config.Categories.Count == 0)
            return ServiceResult<string>.Failure("费用配置必须包含至少一个费用类别。", "CONFIG_001");

        foreach (var cat in config.Categories)
        {
            if (string.IsNullOrWhiteSpace(cat.Name))
                return ServiceResult<string>.Failure("费用类别名称不能为空。", "CONFIG_001");
            if (string.IsNullOrWhiteSpace(cat.Code))
                cat.Code = cat.Name.Trim();
            if (cat.SingleLimit is not null && cat.SingleLimit <= 0)
                return ServiceResult<string>.Failure($"费用类别【{cat.Name}】单笔限额必须大于 0。", "CONFIG_001");
        }

        var duplicates = config.Categories.GroupBy(c => c.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            return ServiceResult<string>.Failure($"费用类别编码重复：{string.Join(", ", duplicates)}。", "CONFIG_001");

        if (!string.IsNullOrWhiteSpace(previousJson))
        {
            try
            {
                var prevConfig = JsonSerializer.Deserialize<ExpensePolicyConfig>(previousJson, JsonOptions);
                if (prevConfig is not null)
                {
                    foreach (var prevCat in prevConfig.Categories)
                    {
                        if (string.IsNullOrWhiteSpace(prevCat.Code)) continue;
                        var matched = config.Categories.FirstOrDefault(c => c.Code.Equals(prevCat.Code, StringComparison.OrdinalIgnoreCase));
                        if (matched is null)
                            return ServiceResult<string>.Failure($"费用类别编码【{prevCat.Code}】创建后不可修改或删除。", "CONFIG_001");
                        if (!string.Equals(matched.Name, prevCat.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!matched.Aliases.Contains(prevCat.Name, StringComparer.OrdinalIgnoreCase))
                                matched.Aliases.Add(prevCat.Name);
                        }
                        foreach (var alias in prevCat.Aliases)
                        {
                            if (!matched.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                                matched.Aliases.Add(alias);
                        }
                    }
                }
            }
            catch { }
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

    private static ServiceResult<string> ValidateProcurement(string json, DemoData? data, string? previousJson)
    {
        var config = JsonSerializer.Deserialize<ProcurementPolicyConfig>(json, JsonOptions);
        if (config is null || config.Categories.Count == 0)
            return ServiceResult<string>.Failure("采购配置必须包含至少一个采购类别。", "CONFIG_001");

        foreach (var cat in config.Categories)
        {
            if (string.IsNullOrWhiteSpace(cat.Name))
                return ServiceResult<string>.Failure("采购类别名称不能为空。", "CONFIG_001");
            if (string.IsNullOrWhiteSpace(cat.Code))
                cat.Code = cat.Name.Trim();
        }

        var duplicates = config.Categories.GroupBy(c => c.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            return ServiceResult<string>.Failure($"采购类别编码重复：{string.Join(", ", duplicates)}。", "CONFIG_001");

        if (config.QuoteAttachmentThreshold < 0)
            return ServiceResult<string>.Failure("报价附件门槛金额不能为负数。", "CONFIG_001");

        if (!string.IsNullOrWhiteSpace(config.DefaultPurchaserUserId) && data is not null)
        {
            var user = data.FindEmployee(config.DefaultPurchaserUserId);
            if (user is null || user.Status != "ACTIVE")
                return ServiceResult<string>.Failure($"默认采购负责人【{config.DefaultPurchaserUserId}】不存在或已停用。", "CONFIG_001");
        }

        if (!string.IsNullOrWhiteSpace(previousJson))
        {
            try
            {
                var prevConfig = JsonSerializer.Deserialize<ProcurementPolicyConfig>(previousJson, JsonOptions);
                if (prevConfig is not null)
                {
                    foreach (var prevCat in prevConfig.Categories)
                    {
                        if (string.IsNullOrWhiteSpace(prevCat.Code)) continue;
                        var matched = config.Categories.FirstOrDefault(c => c.Code.Equals(prevCat.Code, StringComparison.OrdinalIgnoreCase));
                        if (matched is null)
                            return ServiceResult<string>.Failure($"采购类别编码【{prevCat.Code}】创建后不可修改或删除。", "CONFIG_001");
                        if (!string.Equals(matched.Name, prevCat.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!matched.Aliases.Contains(prevCat.Name, StringComparer.OrdinalIgnoreCase))
                                matched.Aliases.Add(prevCat.Name);
                        }
                        foreach (var alias in prevCat.Aliases)
                        {
                            if (!matched.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                                matched.Aliases.Add(alias);
                        }
                    }
                }
            }
            catch { }
        }

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }

    private static ServiceResult<string> ValidateSeal(string json, DemoData? data, string? previousJson)
    {
        var config = JsonSerializer.Deserialize<SealPolicyConfig>(json, JsonOptions);
        if (config is null || config.Seals.Count == 0)
            return ServiceResult<string>.Failure("用章配置必须包含至少一枚印章台账。", "CONFIG_001");

        foreach (var seal in config.Seals)
        {
            if (string.IsNullOrWhiteSpace(seal.Name))
                return ServiceResult<string>.Failure("印章名称不能为空。", "CONFIG_001");
            if (string.IsNullOrWhiteSpace(seal.Code))
                seal.Code = seal.Name.Trim();
            if (seal.MaxOutDays < 0)
                return ServiceResult<string>.Failure($"印章【{seal.Name}】外带最长天数不能为负数。", "CONFIG_001");
            if (!string.IsNullOrWhiteSpace(seal.CustodianUserId) && data is not null)
            {
                var user = data.FindEmployee(seal.CustodianUserId);
                if (user is null || user.Status != "ACTIVE")
                    return ServiceResult<string>.Failure($"印章【{seal.Name}】保管人【{seal.CustodianUserId}】不存在或已停用。", "CONFIG_001");
            }
        }

        var sealDuplicates = config.Seals.GroupBy(s => s.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (sealDuplicates.Count > 0)
            return ServiceResult<string>.Failure($"印章编码重复：{string.Join(", ", sealDuplicates)}。", "CONFIG_001");

        foreach (var category in config.DocumentCategories)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
                return ServiceResult<string>.Failure("文件类别名称不能为空。", "CONFIG_001");
            if (string.IsNullOrWhiteSpace(category.Code))
                category.Code = category.Name.Trim();
            var risk = category.RiskLevel.ToUpperInvariant();
            if (risk is not ("LOW" or "MEDIUM" or "HIGH"))
                return ServiceResult<string>.Failure($"文件类别【{category.Name}】风险等级必须为 LOW、MEDIUM 或 HIGH。", "CONFIG_001");
            category.RiskLevel = risk;
        }

        var docDuplicates = config.DocumentCategories.GroupBy(c => c.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (docDuplicates.Count > 0)
            return ServiceResult<string>.Failure($"文件类别编码重复：{string.Join(", ", docDuplicates)}。", "CONFIG_001");

        if (!string.IsNullOrWhiteSpace(previousJson))
        {
            try
            {
                var prevConfig = JsonSerializer.Deserialize<SealPolicyConfig>(previousJson, JsonOptions);
                if (prevConfig is not null)
                {
                    foreach (var prevSeal in prevConfig.Seals)
                    {
                        if (string.IsNullOrWhiteSpace(prevSeal.Code)) continue;
                        var matched = config.Seals.FirstOrDefault(s => s.Code.Equals(prevSeal.Code, StringComparison.OrdinalIgnoreCase));
                        if (matched is null)
                            return ServiceResult<string>.Failure($"印章编码【{prevSeal.Code}】创建后不可修改或删除。", "CONFIG_001");
                        if (!string.Equals(matched.Name, prevSeal.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!matched.Aliases.Contains(prevSeal.Name, StringComparer.OrdinalIgnoreCase))
                                matched.Aliases.Add(prevSeal.Name);
                        }
                    }
                    foreach (var prevDoc in prevConfig.DocumentCategories)
                    {
                        if (string.IsNullOrWhiteSpace(prevDoc.Code)) continue;
                        var matched = config.DocumentCategories.FirstOrDefault(d => d.Code.Equals(prevDoc.Code, StringComparison.OrdinalIgnoreCase));
                        if (matched is null)
                            return ServiceResult<string>.Failure($"文件类别编码【{prevDoc.Code}】创建后不可修改或删除。", "CONFIG_001");
                        if (!string.Equals(matched.Name, prevDoc.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!matched.Aliases.Contains(prevDoc.Name, StringComparer.OrdinalIgnoreCase))
                                matched.Aliases.Add(prevDoc.Name);
                        }
                    }
                }
            }
            catch { }
        }

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }

    private static ServiceResult<string> ValidateDictionary(string json, string? previousJson)
    {
        var config = JsonSerializer.Deserialize<DictionaryConfig>(json, JsonOptions);
        if (config is null)
            return ServiceResult<string>.Failure("字典配置内容不能为空。", "CONFIG_001");

        foreach (var item in config.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                return ServiceResult<string>.Failure("字典项名称不能为空。", "CONFIG_001");
            if (string.IsNullOrWhiteSpace(item.Code))
                item.Code = item.Name.Trim();
        }

        var duplicateCodes = config.Items.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateCodes.Count > 0)
            return ServiceResult<string>.Failure($"字典项编码重复：{string.Join(", ", duplicateCodes)}。", "CONFIG_001");

        if (!string.IsNullOrWhiteSpace(previousJson))
        {
            try
            {
                var prevConfig = JsonSerializer.Deserialize<DictionaryConfig>(previousJson, JsonOptions);
                if (prevConfig is not null)
                {
                    foreach (var prevItem in prevConfig.Items)
                    {
                        if (string.IsNullOrWhiteSpace(prevItem.Code)) continue;
                        var matched = config.Items.FirstOrDefault(i => i.Code.Equals(prevItem.Code, StringComparison.OrdinalIgnoreCase));
                        if (matched is null)
                            return ServiceResult<string>.Failure($"字典项编码【{prevItem.Code}】创建后不可修改或删除。", "CONFIG_001");
                    }
                }
            }
            catch { }
        }

        return ServiceResult<string>.Success(JsonSerializer.Serialize(config, JsonOptions));
    }
}
