using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class EmploymentContractService(OaDbContext db, DemoData data, FileService files, NotificationService notifications, IConfiguration configuration)
{
    private const string TenantId = IdentityDefaults.TenantId;
    private static readonly int[] AlertThresholds = [7, 30, 60, 90];

    public ServiceResult<PagedResponse<EmploymentContractView>> List(Employee actor, string? keyword, string? departmentId, string? contractType, string? status, int? expiryDays, int? page, int? pageSize)
    {
        var visibleIds = VisibleEmployees(actor).Select(item => item.Id).ToHashSet();
        var query = db.EmploymentContracts.AsNoTracking().Where(item => item.TenantId == TenantId && visibleIds.Contains(item.UserId));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(item => item.ContractNumber.Contains(value) || item.EmployeeName.Contains(value) || item.UserId.Contains(value) || item.DepartmentName.Contains(value));
        }
        if (!string.IsNullOrWhiteSpace(departmentId)) query = query.Where(item => item.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(contractType)) query = query.Where(item => item.ContractType == contractType);
        var records = query.OrderByDescending(item => item.CreatedAt).ToList();
        var views = MapRecords(records);
        if (!string.IsNullOrWhiteSpace(status)) views = views.Where(item => item.DisplayStatus == status || item.Status == status).ToList();
        if (expiryDays is not null) views = views.Where(item => item.DaysUntilEnd is not null && item.DaysUntilEnd >= 0 && item.DaysUntilEnd <= expiryDays).ToList();
        return ServiceResult<PagedResponse<EmploymentContractView>>.Success(Paging.Create(views, page, pageSize));
    }

    public ServiceResult<EmploymentContractView> Get(Employee actor, Guid id)
    {
        var record = db.EmploymentContracts.AsNoTracking().SingleOrDefault(item => item.Id == id && item.TenantId == TenantId);
        if (record is null) return ServiceResult<EmploymentContractView>.Failure("劳动合同不存在。", "DATA_001");
        var subject = data.FindEmployee(record.UserId);
        if (subject is null || !CanView(actor, subject)) return ServiceResult<EmploymentContractView>.Failure("无权查看该劳动合同。", "AUTH_002");
        return ServiceResult<EmploymentContractView>.Success(MapRecords([record]).Single());
    }

    public ServiceResult<EmploymentContractView> Create(Employee actor, SaveEmploymentContractRequest request)
    {
        var subject = data.FindEmployee(request.UserId);
        if (!CanManage(actor, subject)) return ServiceResult<EmploymentContractView>.Failure("无劳动合同创建权限或员工超出数据范围。", "AUTH_002");
        var validation = Validate(actor, request, null, false);
        if (!validation.IsSuccess) return ServiceResult<EmploymentContractView>.Failure(validation.Error!, validation.Code!);
        var (reviewRequired, reviewReason) = OpenEndedReview(subject!);
        var record = new EmploymentContractRecord
        {
            TenantId = TenantId, ContractNumber = GenerateNumber(), UserId = subject!.Id, EmployeeName = subject.Name,
            DepartmentId = subject.DepartmentId, DepartmentName = subject.DepartmentName, Status = EmploymentContractStatuses.Draft,
            CreatedBy = actor.Id, UpdatedBy = actor.Id, OpenEndedReviewRequired = reviewRequired, OpenEndedReviewReason = reviewReason
        };
        Apply(record, request);
        db.EmploymentContracts.Add(record);
        AddEvent(record, actor, "CREATED", $"创建合同草稿 {record.ContractNumber}", request.ChangeReason);
        Audit(actor, "EMPLOYMENT_CONTRACT_CREATED", record, $"创建 {subject.Name} 的劳动合同草稿");
        db.SaveChanges();
        return ServiceResult<EmploymentContractView>.Success(MapRecords([record]).Single());
    }

    public ServiceResult<EmploymentContractView> Update(Employee actor, Guid id, SaveEmploymentContractRequest request)
    {
        var record = db.EmploymentContracts.SingleOrDefault(item => item.Id == id && item.TenantId == TenantId);
        if (record is null) return ServiceResult<EmploymentContractView>.Failure("劳动合同不存在。", "DATA_001");
        var subject = data.FindEmployee(record.UserId);
        if (!CanManage(actor, subject)) return ServiceResult<EmploymentContractView>.Failure("无劳动合同维护权限。", "AUTH_002");
        if (record.Status != EmploymentContractStatuses.Draft) return ServiceResult<EmploymentContractView>.Failure("仅草稿合同可以编辑。", "CONTRACT_004");
        if (record.Version != request.Version) return ServiceResult<EmploymentContractView>.Failure("合同已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (request.UserId != record.UserId) return ServiceResult<EmploymentContractView>.Failure("合同员工创建后不可修改。", "CONTRACT_001");
        var validation = Validate(actor, request, id, false);
        if (!validation.IsSuccess) return ServiceResult<EmploymentContractView>.Failure(validation.Error!, validation.Code!);
        Apply(record, request); record.Version++; record.UpdatedBy = actor.Id; record.UpdatedAt = DateTimeOffset.UtcNow;
        var (reviewRequired, reviewReason) = OpenEndedReview(subject!);
        record.OpenEndedReviewRequired = reviewRequired; record.OpenEndedReviewReason = reviewReason;
        AddEvent(record, actor, "UPDATED", $"更新合同草稿 {record.ContractNumber}", request.ChangeReason);
        Audit(actor, "EMPLOYMENT_CONTRACT_UPDATED", record, $"更新 {record.EmployeeName} 的劳动合同草稿");
        var failure = SaveContractChanges<EmploymentContractView>();
        if (failure is not null) return failure;
        return ServiceResult<EmploymentContractView>.Success(MapRecords([record]).Single());
    }

    public ServiceResult<EmploymentContractView> Activate(Employee actor, Guid id, ActivateEmploymentContractRequest request)
    {
        var record = db.EmploymentContracts.SingleOrDefault(item => item.Id == id && item.TenantId == TenantId);
        if (record is null) return ServiceResult<EmploymentContractView>.Failure("劳动合同不存在。", "DATA_001");
        if (!CanManage(actor, data.FindEmployee(record.UserId))) return ServiceResult<EmploymentContractView>.Failure("无劳动合同激活权限。", "AUTH_002");
        if (record.Status != EmploymentContractStatuses.Draft) return ServiceResult<EmploymentContractView>.Failure("仅草稿合同可以激活。", "CONTRACT_004");
        if (record.Version != request.Version) return ServiceResult<EmploymentContractView>.Failure("合同已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) return ServiceResult<EmploymentContractView>.Failure("激活原因应为 1–500 个字符。", "CONTRACT_001");
        var attachments = Deserialize(record.AttachmentsJson);
        if (record.SignedDate is null || record.SignedDate > BusinessTime.ChinaToday() || attachments.Count == 0 || !FilesExist(attachments))
            return ServiceResult<EmploymentContractView>.Failure("激活前必须填写有效签订日期并关联至少一份签署附件。", "CONTRACT_005");
        if (OverlapsActive(record.UserId, record.StartDate, record.EndDate, record.Id)) return ServiceResult<EmploymentContractView>.Failure("该员工存在日期重叠的有效合同。", "CONTRACT_002");
        record.Status = EmploymentContractStatuses.Active; record.Version++; record.UpdatedBy = actor.Id; record.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(record, actor, "ACTIVATED", $"激活合同 {record.ContractNumber}", request.Reason.Trim());
        Audit(actor, "EMPLOYMENT_CONTRACT_ACTIVATED", record, $"激活 {record.EmployeeName} 的劳动合同");
        notifications.Enqueue(record.UserId, "EMPLOYMENT_CONTRACT", "劳动合同已归档", $"劳动合同 {record.ContractNumber} 已生效并归档。", "EmploymentContract", record.Id);
        var failure = SaveContractChanges<EmploymentContractView>();
        if (failure is not null) return failure;
        return ServiceResult<EmploymentContractView>.Success(MapRecords([record]).Single());
    }

    public ServiceResult<EmploymentContractView> Renew(Employee actor, Guid sourceId, SaveEmploymentContractRequest request)
    {
        var source = db.EmploymentContracts.SingleOrDefault(item => item.Id == sourceId && item.TenantId == TenantId);
        if (source is null) return ServiceResult<EmploymentContractView>.Failure("原劳动合同不存在。", "DATA_001");
        var subject = data.FindEmployee(source.UserId);
        if (!CanManage(actor, subject)) return ServiceResult<EmploymentContractView>.Failure("无劳动合同续签权限。", "AUTH_002");
        if (source.Status != EmploymentContractStatuses.Active) return ServiceResult<EmploymentContractView>.Failure("仅有效或已到期展示状态的合同可以续签。", "CONTRACT_004");
        if (source.Version != request.Version) return ServiceResult<EmploymentContractView>.Failure("原合同已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (source.EndDate is null) return ServiceResult<EmploymentContractView>.Failure("无明确结束日期的合同不能续签，请按合同变更或终止流程处理。", "CONTRACT_004");
        if (request.UserId != source.UserId || request.SignedDate is null || request.SignedDate > BusinessTime.ChinaToday()) return ServiceResult<EmploymentContractView>.Failure("续签员工或签订日期不合法。", "CONTRACT_001");
        if (source.EndDate is not null && request.StartDate <= source.EndDate) return ServiceResult<EmploymentContractView>.Failure("续签合同开始日期必须晚于原合同结束日期。", "CONTRACT_002");
        var validation = Validate(actor, request, null, true);
        if (!validation.IsSuccess) return ServiceResult<EmploymentContractView>.Failure(validation.Error!, validation.Code!);
        var attachments = request.Attachments?.Distinct().ToList() ?? [];
        if (attachments.Count == 0) return ServiceResult<EmploymentContractView>.Failure("续签必须关联签署附件。", "CONTRACT_005");
        if (OverlapsActive(source.UserId, request.StartDate, request.EndDate, source.Id)) return ServiceResult<EmploymentContractView>.Failure("续签日期与其他有效合同重叠。", "CONTRACT_002");
        var (reviewRequired, reviewReason) = OpenEndedReview(subject!, includeNextFixedTerm: request.ContractType == EmploymentContractTypes.FixedTerm);
        var renewed = new EmploymentContractRecord
        {
            TenantId = TenantId, ContractNumber = GenerateNumber(), UserId = source.UserId, EmployeeName = source.EmployeeName,
            DepartmentId = subject!.DepartmentId, DepartmentName = subject.DepartmentName, Status = EmploymentContractStatuses.Active,
            RenewalOfId = source.Id, RenewalSequence = source.RenewalSequence + 1, CreatedBy = actor.Id, UpdatedBy = actor.Id,
            OpenEndedReviewRequired = reviewRequired, OpenEndedReviewReason = reviewReason
        };
        Apply(renewed, request);
        source.Status = EmploymentContractStatuses.Superseded; source.Version++; source.UpdatedBy = actor.Id; source.UpdatedAt = DateTimeOffset.UtcNow;
        db.EmploymentContracts.Add(renewed);
        AddEvent(source, actor, "SUPERSEDED", $"合同由续签版本 {renewed.ContractNumber} 取代", request.ChangeReason);
        AddEvent(renewed, actor, "RENEWED_FROM", $"由合同 {source.ContractNumber} 续签", request.ChangeReason);
        Audit(actor, "EMPLOYMENT_CONTRACT_RENEWED", renewed, $"续签 {renewed.EmployeeName} 的劳动合同");
        notifications.Enqueue(renewed.UserId, "EMPLOYMENT_CONTRACT", "劳动合同已续签", $"新合同 {renewed.ContractNumber} 已归档。", "EmploymentContract", renewed.Id);
        var failure = SaveContractChanges<EmploymentContractView>();
        if (failure is not null) return failure;
        return ServiceResult<EmploymentContractView>.Success(MapRecords([renewed]).Single());
    }

    public ServiceResult<EmploymentContractView> Terminate(Employee actor, Guid id, TerminateEmploymentContractRequest request)
    {
        var record = db.EmploymentContracts.SingleOrDefault(item => item.Id == id && item.TenantId == TenantId);
        if (record is null) return ServiceResult<EmploymentContractView>.Failure("劳动合同不存在。", "DATA_001");
        if (!CanManage(actor, data.FindEmployee(record.UserId))) return ServiceResult<EmploymentContractView>.Failure("无劳动合同终止权限。", "AUTH_002");
        if (record.Status != EmploymentContractStatuses.Active) return ServiceResult<EmploymentContractView>.Failure("仅有效合同可以登记终止。", "CONTRACT_004");
        if (record.Version != request.Version) return ServiceResult<EmploymentContractView>.Failure("合同已被更新，请刷新后重试。", "CONCURRENCY_001");
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500 || request.TerminationDate < record.StartDate || request.TerminationDate > BusinessTime.ChinaToday())
            return ServiceResult<EmploymentContractView>.Failure("终止日期或原因不符合要求。", "CONTRACT_001");
        record.Status = EmploymentContractStatuses.Terminated; record.TerminationDate = request.TerminationDate; record.TerminationReason = reason;
        record.Version++; record.UpdatedBy = actor.Id; record.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(record, actor, "TERMINATED", $"登记终止合同 {record.ContractNumber}", reason);
        Audit(actor, "EMPLOYMENT_CONTRACT_TERMINATED", record, $"终止 {record.EmployeeName} 的劳动合同");
        notifications.Enqueue(record.UserId, "EMPLOYMENT_CONTRACT", "劳动合同状态已更新", $"合同 {record.ContractNumber} 已登记终止。", "EmploymentContract", record.Id);
        var failure = SaveContractChanges<EmploymentContractView>();
        if (failure is not null) return failure;
        return ServiceResult<EmploymentContractView>.Success(MapRecords([record]).Single());
    }

    public ServiceResult<ContractAlertAcknowledgementView> AcknowledgeAlert(Employee actor, Guid id, AcknowledgeContractAlertRequest request)
    {
        if (!AlertThresholds.Contains(request.ThresholdDays)) return ServiceResult<ContractAlertAcknowledgementView>.Failure("预警阈值不合法。", "CONTRACT_001");
        var record = db.EmploymentContracts.SingleOrDefault(item => item.Id == id && item.TenantId == TenantId);
        if (record is null) return ServiceResult<ContractAlertAcknowledgementView>.Failure("劳动合同不存在。", "DATA_001");
        if (!CanManage(actor, data.FindEmployee(record.UserId))) return ServiceResult<ContractAlertAcknowledgementView>.Failure("无劳动合同预警处理权限。", "AUTH_002");
        var threshold = CurrentThreshold(record);
        if (threshold is null || threshold != request.ThresholdDays) return ServiceResult<ContractAlertAcknowledgementView>.Failure("该合同当前不处于指定预警档位。", "CONTRACT_004");
        var existing = db.ContractAlertAcknowledgements.SingleOrDefault(item => item.ContractId == id && item.ThresholdDays == request.ThresholdDays);
        if (existing is null)
        {
            existing = new ContractAlertAcknowledgementRecord { TenantId = TenantId, ContractId = id, ThresholdDays = request.ThresholdDays, AcknowledgedBy = actor.Id, AcknowledgedByName = actor.Name };
            db.ContractAlertAcknowledgements.Add(existing);
            AddEvent(record, actor, "ALERT_ACKNOWLEDGED", $"确认 {request.ThresholdDays} 天到期预警", "已查看并进入合同处理流程");
            Audit(actor, "CONTRACT_ALERT_ACKNOWLEDGED", record, $"确认 {record.ContractNumber} 的到期预警");
            db.SaveChanges();
        }
        return ServiceResult<ContractAlertAcknowledgementView>.Success(ToAck(existing));
    }

    public ContractAlertSummaryView AlertSummary(Employee actor)
    {
        var visible = VisibleEmployees(actor).ToList();
        var visibleIds = visible.Select(item => item.Id).ToHashSet();
        var today = BusinessTime.ChinaToday();
        var contracts = db.EmploymentContracts.AsNoTracking().Where(item => item.TenantId == TenantId && visibleIds.Contains(item.UserId)).ToList();
        var active = contracts.Where(item => item.Status == EmploymentContractStatuses.Active && item.EndDate is not null).ToList();
        var currentAcks = db.ContractAlertAcknowledgements.AsNoTracking().Where(item => active.Select(contract => contract.Id).Contains(item.ContractId)).ToList();
        var missing = visible.Count(employee => employee.Status == "ACTIVE" && HireDate(employee.Id) <= today.AddMonths(-1) && !contracts.Any(contract => contract.UserId == employee.Id && contract.Status == EmploymentContractStatuses.Active));
        int Days(EmploymentContractRecord item) => item.EndDate!.Value.DayNumber - today.DayNumber;
        var unacknowledged = active.Count(item => CurrentThreshold(item) is int threshold && !currentAcks.Any(ack => ack.ContractId == item.Id && ack.ThresholdDays == threshold));
        var atRisk = missing + contracts.Count(item => item.Status is EmploymentContractStatuses.Draft or EmploymentContractStatuses.Active && (item.OpenEndedReviewRequired || item.Status == EmploymentContractStatuses.Active && item.EndDate is not null && Days(item) <= 90));
        return new ContractAlertSummaryView(missing, active.Count(item => Days(item) < 0), active.Count(item => Days(item) is >= 0 and <= 7), active.Count(item => Days(item) is > 7 and <= 30), active.Count(item => Days(item) is > 30 and <= 60), active.Count(item => Days(item) is > 60 and <= 90), contracts.Count(item => item.OpenEndedReviewRequired && item.Status is EmploymentContractStatuses.Draft or EmploymentContractStatuses.Active), unacknowledged, atRisk);
    }

    public int DispatchDueAlerts()
    {
        var candidates = db.EmploymentContracts.AsNoTracking().Where(item => item.TenantId == TenantId && item.Status == EmploymentContractStatuses.Active && item.EndDate != null).ToList();
        var existing = db.ContractAlertDeliveries.AsNoTracking().Where(item => candidates.Select(contract => contract.Id).Contains(item.ContractId)).Select(item => new { item.ContractId, item.RecipientId, item.ThresholdDays }).ToHashSet();
        var created = 0;
        foreach (var contract in candidates)
        {
            var threshold = CurrentThreshold(contract);
            var subject = data.FindEmployee(contract.UserId);
            if (threshold is null || subject is null) continue;
            foreach (var recipient in data.ActiveEmployees.Where(actor => data.HasPermission(actor, OaPermissions.ContractManage) && CanView(actor, subject)))
            {
                if (existing.Contains(new { ContractId = contract.Id, RecipientId = recipient.Id, ThresholdDays = threshold.Value })) continue;
                db.Notifications.Add(new NotificationRecord { TenantId = TenantId, RecipientId = recipient.Id, Type = $"CONTRACT_EXPIRY_{threshold}", Title = $"劳动合同将在 {threshold} 天内到期", Content = $"{contract.EmployeeName} 的合同 {contract.ContractNumber} 将于 {contract.EndDate:yyyy-MM-dd} 到期，请及时处理。", ResourceType = "EmploymentContract", ResourceId = contract.Id.ToString() });
                db.ContractAlertDeliveries.Add(new ContractAlertDeliveryRecord { TenantId = TenantId, ContractId = contract.Id, RecipientId = recipient.Id, ThresholdDays = threshold.Value });
                created++;
            }
        }
        if (created > 0) db.SaveChanges();
        return created;
    }

    public ServiceResult<GenerateContractDemoResult> GenerateDemoData(Employee actor)
    {
        if (!configuration.GetValue<bool>("DemoFeatures:AllowDataGeneration")) return ServiceResult<GenerateContractDemoResult>.Failure("当前环境已关闭模拟数据生成功能。", "DEMO_DISABLED");
        if (!data.HasPermission(actor, OaPermissions.ContractManage)) return ServiceResult<GenerateContractDemoResult>.Failure("无模拟合同生成权限。", "AUTH_002");
        var today = BusinessTime.ChinaToday();
        var employees = VisibleEmployees(actor).Where(item => item.Status == "ACTIVE").OrderBy(item => item.Id).ToList();
        var expiryOffsets = new[] { 7, 28, 55, 82, 180, 270, 365 };
        var created = 0; var skipped = 0;
        for (var index = 0; index < employees.Count; index++)
        {
            var subject = employees[index];
            if (db.EmploymentContracts.Any(item => item.TenantId == TenantId && item.UserId == subject.Id)) { skipped++; continue; }
            var demoFile = files.CreateDemoPdf(actor, $"{subject.Name}-模拟劳动合同.pdf");
            if (!demoFile.IsSuccess) return ServiceResult<GenerateContractDemoResult>.Failure(demoFile.Error!, demoFile.Code!);
            var openEnded = index == employees.Count - 1;
            var start = today.AddYears(-1);
            var (reviewRequired, reviewReason) = OpenEndedReview(subject);
            var record = new EmploymentContractRecord
            {
                TenantId = TenantId, ContractNumber = GenerateNumber(), UserId = subject.Id, EmployeeName = subject.Name,
                DepartmentId = subject.DepartmentId, DepartmentName = subject.DepartmentName, PositionName = subject.PositionName ?? "未分配岗位", WorkLocation = "总部办公室",
                ContractType = openEnded ? EmploymentContractTypes.OpenEnded : EmploymentContractTypes.FixedTerm, Status = EmploymentContractStatuses.Active,
                SignedDate = start, StartDate = start, EndDate = openEnded ? null : today.AddDays(expiryOffsets[index % expiryOffsets.Length]),
                AttachmentsJson = JsonSerializer.Serialize(new[] { demoFile.Value!.Id.ToString() }), Notes = "系统生成的演示合同，仅用于功能验证。",
                OpenEndedReviewRequired = reviewRequired, OpenEndedReviewReason = reviewReason, IsDemo = true, CreatedBy = actor.Id, UpdatedBy = actor.Id
            };
            db.EmploymentContracts.Add(record);
            AddEvent(record, actor, "DEMO_CREATED", $"生成模拟合同 {record.ContractNumber}", "演示数据初始化，不覆盖已有合同");
            Audit(actor, "EMPLOYMENT_CONTRACT_DEMO_CREATED", record, $"生成 {subject.Name} 的模拟劳动合同");
            db.SaveChanges(); created++;
        }
        return ServiceResult<GenerateContractDemoResult>.Success(new(created, skipped));
    }

    private ServiceResult<bool> Validate(Employee actor, SaveEmploymentContractRequest request, Guid? currentId, bool activating)
    {
        if (!EmploymentContractTypes.All.Contains(request.ContractType)) return ServiceResult<bool>.Failure("合同类型不合法。", "CONTRACT_001");
        var location = request.WorkLocation?.Trim() ?? string.Empty; var position = request.PositionName?.Trim() ?? string.Empty; var reason = request.ChangeReason?.Trim() ?? string.Empty;
        if (location.Length is < 1 or > 100 || position.Length is < 1 or > 100 || reason.Length is < 5 or > 500 || request.Notes?.Trim().Length > 1000)
            return ServiceResult<bool>.Failure("工作地点、岗位、备注或变更说明不符合要求。", "CONTRACT_001");
        if (request.SignedDate > BusinessTime.ChinaToday()) return ServiceResult<bool>.Failure("签订日期不能晚于今天。", "CONTRACT_001");
        if (request.ContractType == EmploymentContractTypes.FixedTerm && (request.EndDate is null || request.EndDate <= request.StartDate)) return ServiceResult<bool>.Failure("固定期限合同必须填写晚于开始日期的结束日期。", "CONTRACT_001");
        if (request.ContractType == EmploymentContractTypes.OpenEnded && request.EndDate is not null) return ServiceResult<bool>.Failure("无固定期限合同不能填写结束日期。", "CONTRACT_001");
        if (request.ContractType == EmploymentContractTypes.ProjectBased && (request.ProjectDescription?.Trim().Length is not (>= 5 and <= 500))) return ServiceResult<bool>.Failure("项目期限合同必须填写 5–500 字任务说明。", "CONTRACT_001");
        if (request.EndDate is not null && request.EndDate <= request.StartDate) return ServiceResult<bool>.Failure("合同结束日期必须晚于开始日期。", "CONTRACT_001");
        var attachments = request.Attachments?.Distinct().ToList() ?? [];
        if (!AttachmentsCanBeAssigned(actor, attachments, currentId))
            return ServiceResult<bool>.Failure("合同附件不存在、不属于当前操作人、重复或超过 10 个。", "CONTRACT_005");
        var probation = ValidateProbation(request, currentId);
        if (!probation.IsSuccess) return probation;
        if (activating && (request.SignedDate is null || attachments.Count == 0)) return ServiceResult<bool>.Failure("激活或续签必须有签订日期和签署附件。", "CONTRACT_005");
        return ServiceResult<bool>.Success(true);
    }

    private ServiceResult<bool> ValidateProbation(SaveEmploymentContractRequest request, Guid? currentId)
    {
        if (request.ProbationStartDate is null && request.ProbationEndDate is null) return ServiceResult<bool>.Success(true);
        if (request.ProbationStartDate is null || request.ProbationEndDate is null || request.ProbationEndDate < request.ProbationStartDate)
            return ServiceResult<bool>.Failure("试用期开始和结束日期必须同时填写且顺序正确。", "CONTRACT_003");
        if (request.ContractType == EmploymentContractTypes.ProjectBased) return ServiceResult<bool>.Failure("项目期限合同不得约定试用期。", "CONTRACT_003");
        if (request.ProbationStartDate < request.StartDate || request.EndDate is not null && request.ProbationEndDate > request.EndDate)
            return ServiceResult<bool>.Failure("试用期必须包含在劳动合同期限内。", "CONTRACT_003");
        var maxMonths = 6;
        if (request.ContractType == EmploymentContractTypes.FixedTerm)
        {
            var exclusiveEnd = request.EndDate!.Value.AddDays(1);
            maxMonths = exclusiveEnd < request.StartDate.AddMonths(3) ? 0 : exclusiveEnd < request.StartDate.AddYears(1) ? 1 : exclusiveEnd < request.StartDate.AddYears(3) ? 2 : 6;
        }
        if (maxMonths == 0 || request.ProbationEndDate.Value.AddDays(1) > request.ProbationStartDate.Value.AddMonths(maxMonths))
            return ServiceResult<bool>.Failure($"当前合同期限允许的试用期最长为 {maxMonths} 个月。", "CONTRACT_003");
        if (db.EmploymentContracts.Any(item => item.TenantId == TenantId && item.UserId == request.UserId && item.Id != currentId && item.ProbationStartDate != null))
            return ServiceResult<bool>.Failure("同一员工在本单位只能记录一次试用期。", "CONTRACT_003");
        return ServiceResult<bool>.Success(true);
    }

    private void Apply(EmploymentContractRecord record, SaveEmploymentContractRequest request)
    {
        record.ContractType = request.ContractType; record.SignedDate = request.SignedDate; record.StartDate = request.StartDate; record.EndDate = request.EndDate;
        record.ProbationStartDate = request.ProbationStartDate; record.ProbationEndDate = request.ProbationEndDate;
        record.WorkLocation = request.WorkLocation.Trim(); record.PositionName = request.PositionName.Trim(); record.ProjectDescription = request.ProjectDescription?.Trim();
        record.AttachmentsJson = JsonSerializer.Serialize(request.Attachments?.Distinct().ToList() ?? []); record.Notes = request.Notes?.Trim();
    }

    private IReadOnlyList<EmploymentContractView> MapRecords(IReadOnlyList<EmploymentContractRecord> records)
    {
        var ids = records.Select(item => item.Id).ToList();
        var events = db.EmploymentContractEvents.AsNoTracking().Where(item => ids.Contains(item.ContractId)).OrderByDescending(item => item.CreatedAt).ToList().ToLookup(item => item.ContractId);
        var acks = db.ContractAlertAcknowledgements.AsNoTracking().Where(item => ids.Contains(item.ContractId)).OrderByDescending(item => item.AcknowledgedAt).ToList().ToLookup(item => item.ContractId);
        var renewalIds = records.Where(item => item.RenewalOfId is not null).Select(item => item.RenewalOfId!.Value).Distinct().ToList();
        var renewalNumbers = db.EmploymentContracts.AsNoTracking().Where(item => renewalIds.Contains(item.Id)).ToDictionary(item => item.Id, item => item.ContractNumber);
        var today = BusinessTime.ChinaToday();
        return records.Select(item =>
        {
            int? days = item.EndDate is null ? null : item.EndDate.Value.DayNumber - today.DayNumber;
            var display = item.Status == EmploymentContractStatuses.Active && days < 0 ? EmploymentContractStatuses.Expired : item.Status == EmploymentContractStatuses.Active && days <= 90 ? EmploymentContractStatuses.Expiring : item.Status;
            var threshold = CurrentThreshold(item);
            var itemAcks = acks[item.Id].Select(ToAck).ToList();
            return new EmploymentContractView(item.Id, item.ContractNumber, item.UserId, item.EmployeeName, item.DepartmentId, item.DepartmentName, item.PositionName, item.WorkLocation, item.ContractType, item.Status, display, item.SignedDate, item.StartDate, item.EndDate, item.ProbationStartDate, item.ProbationEndDate, item.ProjectDescription, Deserialize(item.AttachmentsJson), item.Notes, item.RenewalOfId, item.RenewalOfId is Guid sourceId ? renewalNumbers.GetValueOrDefault(sourceId) : null, item.RenewalSequence, item.OpenEndedReviewRequired, item.OpenEndedReviewReason, item.TerminationDate, item.TerminationReason, days, threshold, threshold is not null && itemAcks.Any(ack => ack.ThresholdDays == threshold), item.IsDemo, item.Version, item.CreatedBy, item.CreatedAt, item.UpdatedAt, events[item.Id].Select(ToEvent).ToList(), itemAcks);
        }).ToList();
    }

    private IReadOnlyList<Employee> VisibleEmployees(Employee actor) => data.Employees.Where(subject => CanView(actor, subject)).ToList();
    private bool CanView(Employee actor, Employee subject)
    {
        if (actor.Id == subject.Id) return true;
        return data.EffectiveDataScope(actor, "Contract") switch
        {
            OaDataScopes.Company => true,
            OaDataScopes.Department => actor.DepartmentId == subject.DepartmentId,
            OaDataScopes.DepartmentAndChildren => data.IsDepartmentWithin(subject.DepartmentId, actor.DepartmentId),
            _ => false
        };
    }
    private bool CanManage(Employee actor, Employee? subject) => subject is not null && data.HasPermission(actor, OaPermissions.ContractManage) && CanView(actor, subject);
    private ServiceResult<T>? SaveContractChanges<T>()
    {
        try
        {
            db.SaveChanges();
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ServiceResult<T>.Failure("劳动合同已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        }
        catch (DbUpdateException exception) when (IsActivePeriodConflict(exception))
        {
            db.ChangeTracker.Clear();
            return ServiceResult<T>.Failure("该员工存在日期重叠的有效合同。", "CONTRACT_002");
        }
    }

    private static bool IsActivePeriodConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: "EX_employment_contract_active_period"
        };

    private bool FilesExist(IReadOnlyList<string> ids) { var parsed = ids.Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).ToList(); return parsed.All(id => id != Guid.Empty) && db.Files.Count(item => item.TenantId == TenantId && parsed.Contains(item.Id)) == parsed.Count; }
    private bool AttachmentsCanBeAssigned(Employee actor, IReadOnlyList<string> ids, Guid? currentId)
    {
        if (ids.Count > 10) return false;
        var parsed = ids.Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).ToList();
        if (parsed.Any(id => id == Guid.Empty) || parsed.Distinct().Count() != parsed.Count) return false;
        var retained = currentId is null
            ? new HashSet<Guid>()
            : Deserialize(db.EmploymentContracts.AsNoTracking().Where(item => item.Id == currentId && item.TenantId == TenantId).Select(item => item.AttachmentsJson).SingleOrDefault() ?? "[]")
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();
        var newlyAssigned = parsed.Where(id => !retained.Contains(id)).ToList();
        return newlyAssigned.Count == 0 || db.Files.Count(file => file.TenantId == TenantId && file.OwnerId == actor.Id && newlyAssigned.Contains(file.Id)) == newlyAssigned.Count;
    }
    private bool OverlapsActive(string userId, DateOnly start, DateOnly? end, Guid? excludedId) { var effectiveEnd = end ?? DateOnly.MaxValue; return db.EmploymentContracts.AsNoTracking().Any(item => item.TenantId == TenantId && item.UserId == userId && item.Id != excludedId && item.Status == EmploymentContractStatuses.Active && item.StartDate <= effectiveEnd && start <= (item.EndDate ?? DateOnly.MaxValue)); }
    private (bool Required, string? Reason) OpenEndedReview(Employee subject, bool includeNextFixedTerm = false)
    {
        var reasons = new List<string>();
        if (HireDate(subject.Id) <= BusinessTime.ChinaToday().AddYears(-10)) reasons.Add("在本单位连续工作已达到 10 年，请进行无固定期限合同法务评估");
        var fixedTerms = db.EmploymentContracts.AsNoTracking().Count(item => item.TenantId == TenantId && item.UserId == subject.Id && item.ContractType == EmploymentContractTypes.FixedTerm && item.Status != EmploymentContractStatuses.Draft);
        if (fixedTerms + (includeNextFixedTerm ? 1 : 0) >= 2) reasons.Add("连续固定期限合同记录达到二次，请进行续订条件法务评估");
        return (reasons.Count > 0, reasons.Count > 0 ? string.Join("；", reasons) : null);
    }
    private DateOnly HireDate(string userId)
    {
        var hireDate = db.PersonnelProfiles.AsNoTracking().Where(item => item.UserId == userId).Select(item => item.HireDate).SingleOrDefault();
        if (hireDate != default) return hireDate;
        var createdAt = db.Users.AsNoTracking().Where(item => item.Id == userId).Select(item => item.CreatedAt).Single();
        return DateOnly.FromDateTime(createdAt.LocalDateTime);
    }
    private int? CurrentThreshold(EmploymentContractRecord item) { if (item.Status != EmploymentContractStatuses.Active || item.EndDate is null) return null; var days = item.EndDate.Value.DayNumber - BusinessTime.ChinaToday().DayNumber; if (days < 0) return null; return days <= 7 ? 7 : days <= 30 ? 30 : days <= 60 ? 60 : days <= 90 ? 90 : null; }
    private string GenerateNumber() { string number; do number = $"LC-{BusinessTime.ChinaToday():yyyyMMdd}-{Random.Shared.Next(0, 1_000_000):D6}"; while (db.EmploymentContracts.Any(item => item.TenantId == TenantId && item.ContractNumber == number)); return number; }
    private void AddEvent(EmploymentContractRecord contract, Employee actor, string type, string summary, string reason) => db.EmploymentContractEvents.Add(new EmploymentContractEventRecord { TenantId = TenantId, ContractId = contract.Id, EventType = type, Summary = summary, Reason = reason, ChangedBy = actor.Id, ChangedByName = actor.Name, SnapshotJson = JsonSerializer.Serialize(new { contract.ContractNumber, contract.UserId, contract.ContractType, contract.Status, contract.SignedDate, contract.StartDate, contract.EndDate, contract.ProbationStartDate, contract.ProbationEndDate, contract.RenewalOfId, contract.TerminationDate, contract.Version }) });
    private void Audit(Employee actor, string action, EmploymentContractRecord contract, string summary) => db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "EmploymentContract", ResourceId = contract.Id.ToString(), Summary = summary });
    private static EmploymentContractEventView ToEvent(EmploymentContractEventRecord item) => new(item.Id, item.EventType, item.Summary, item.Reason, item.ChangedBy, item.ChangedByName, item.CreatedAt);
    private static ContractAlertAcknowledgementView ToAck(ContractAlertAcknowledgementRecord item) => new(item.Id, item.ThresholdDays, item.AcknowledgedBy, item.AcknowledgedByName, item.AcknowledgedAt);
    private static IReadOnlyList<string> Deserialize(string json) { try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch (JsonException) { return []; } }
}
