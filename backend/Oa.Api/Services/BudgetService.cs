using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class BudgetService
{
    private const string TenantId = "demo";
    private readonly DemoData data;
    private readonly OaDbContext? db;
    private readonly List<Budget> _memoryBudgets = [];
    private readonly List<BudgetTransaction> _memoryTransactions = [];

    public BudgetService(DemoData data, OaDbContext? db = null)
    {
        this.data = data;
        this.db = db;
    }

    private HashSet<string>? GetVisibleDepartmentIds(Employee actor)
    {
        if (CanViewAllBudgets(actor))
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            actor.DepartmentId,
            actor.DepartmentName
        };

        if (actor.Role == "部门负责人" || actor.Role == "总经理" || data.HasRole(actor, "部门负责人") || data.HasRole(actor, "总经理"))
        {
            foreach (var dept in data.Departments)
            {
                if (data.IsDepartmentWithin(dept.Id, actor.DepartmentId))
                {
                    set.Add(dept.Id);
                    set.Add(dept.Name);
                }
            }
        }

        return set;
    }

    public IReadOnlyList<Budget> List(Employee actor, string? departmentId = null, int? year = null, int? month = null, string? expenseCategory = null, string? status = null)
    {
        var visibleDepts = GetVisibleDepartmentIds(actor);

        if (db is not null)
        {
            var query = db.Budgets.AsNoTracking().Where(b => b.TenantId == TenantId);

            if (visibleDepts is not null)
            {
                if (!string.IsNullOrWhiteSpace(departmentId))
                {
                    if (!visibleDepts.Contains(departmentId.Trim()))
                        return [];
                    query = query.Where(b => b.DepartmentId == departmentId.Trim());
                }
                else
                {
                    query = query.Where(b => visibleDepts.Contains(b.DepartmentId));
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(departmentId))
                    query = query.Where(b => b.DepartmentId == departmentId.Trim());
            }

            if (year.HasValue)
                query = query.Where(b => b.Year == year.Value);
            if (month.HasValue)
                query = query.Where(b => b.Month == month.Value);
            if (!string.IsNullOrWhiteSpace(expenseCategory))
                query = query.Where(b => b.ExpenseCategory == expenseCategory.Trim());
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(b => b.Status == status.Trim());

            return query.OrderByDescending(b => b.Year)
                .ThenBy(b => b.Month)
                .ThenBy(b => b.DepartmentId)
                .Select(b => MapToDomain(b))
                .ToList();
        }

        var list = _memoryBudgets.AsEnumerable();
        if (visibleDepts is not null)
        {
            if (!string.IsNullOrWhiteSpace(departmentId))
            {
                if (!visibleDepts.Contains(departmentId.Trim()))
                    return [];
                list = list.Where(b => b.DepartmentId == departmentId.Trim());
            }
            else
            {
                list = list.Where(b => visibleDepts.Contains(b.DepartmentId));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(departmentId))
                list = list.Where(b => b.DepartmentId == departmentId.Trim());
        }

        if (year.HasValue)
            list = list.Where(b => b.Year == year.Value);
        if (month.HasValue)
            list = list.Where(b => b.Month == month.Value);
        if (!string.IsNullOrWhiteSpace(expenseCategory))
            list = list.Where(b => b.ExpenseCategory == expenseCategory.Trim());
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BudgetStatus>(status, true, out var s))
            list = list.Where(b => b.Status == s);

        return list.OrderByDescending(b => b.Year).ThenBy(b => b.Month).ToList();
    }

    public ServiceResult<Budget> Get(Employee actor, Guid id)
    {
        Budget? budget;
        if (db is not null)
        {
            var record = db.Budgets.AsNoTracking().SingleOrDefault(b => b.TenantId == TenantId && b.Id == id);
            budget = record is null ? null : MapToDomain(record);
        }
        else
        {
            budget = _memoryBudgets.SingleOrDefault(b => b.Id == id);
        }

        if (budget is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");

        var visibleDepts = GetVisibleDepartmentIds(actor);
        if (visibleDepts is not null && !visibleDepts.Contains(budget.DepartmentId))
            return ServiceResult<Budget>.Failure("无权查看该部门预算池。", "AUTH_002");

        return ServiceResult<Budget>.Success(budget);
    }

    public ServiceResult<Budget> Create(Employee actor, CreateBudgetRequest request)
    {
        if (!CanManageBudgets(actor))
            return ServiceResult<Budget>.Failure("无预算编制或管理权限。", "AUTH_002");

        if (string.IsNullOrWhiteSpace(request.DepartmentId))
            return ServiceResult<Budget>.Failure("请指定预算归属部门。");
        if (request.Year < 2020 || request.Year > 2100)
            return ServiceResult<Budget>.Failure("预算年度不合法。");
        if (request.Month < 0 || request.Month > 12)
            return ServiceResult<Budget>.Failure("预算月份不合法（0为全年，1-12为月度）。");
        if (request.AllocatedAmount < 0)
            return ServiceResult<Budget>.Failure("编制预算金额不能为负数。");

        var normalizedCategory = string.IsNullOrWhiteSpace(request.ExpenseCategory) ? null : request.ExpenseCategory.Trim();
        var normalizedProject = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId.Trim();
        var normalizedDept = request.DepartmentId.Trim();

        if (db is not null)
        {
            var exists = db.Budgets.Any(b => b.TenantId == TenantId &&
                b.DepartmentId == normalizedDept &&
                b.ExpenseCategory == normalizedCategory &&
                b.ProjectId == normalizedProject &&
                b.Year == request.Year &&
                b.Month == request.Month);

            if (exists)
                return ServiceResult<Budget>.Failure("该维度（部门、科目、项目、年度与月份）的预算池已存在，请直接调整额度。", "BUDGET_EXISTS");

            var record = new BudgetRecord
            {
                TenantId = TenantId,
                DepartmentId = normalizedDept,
                ExpenseCategory = normalizedCategory,
                ProjectId = normalizedProject,
                Year = request.Year,
                Month = request.Month,
                AllocatedAmount = request.AllocatedAmount,
                CommittedAmount = 0m,
                ActualAmount = 0m,
                Status = "ACTIVE",
                CreatedBy = actor.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Budgets.Add(record);

            var tx = new BudgetTransactionRecord
            {
                TenantId = TenantId,
                BudgetId = record.Id,
                BusinessType = "Budget",
                BusinessId = record.Id,
                BusinessNumber = $"INIT-{request.Year}{(request.Month > 0 ? $"-{request.Month:D2}" : "")}",
                TransactionType = "ADJUSTED",
                Amount = request.AllocatedAmount,
                BalanceAfter = request.AllocatedAmount,
                Description = "期初预算编制录入",
                OperatorId = actor.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.BudgetTransactions.Add(tx);
            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "BUDGET_CREATED",
                ResourceType = "Budget",
                ResourceId = record.Id.ToString(),
                Summary = $"编制部门【{normalizedDept}】{request.Year}年{(request.Month > 0 ? $"{request.Month}月" : "全年")}预算 {request.AllocatedAmount:N2} 元"
            });
            db.SaveChanges();

            return ServiceResult<Budget>.Success(MapToDomain(record));
        }

        var memExists = _memoryBudgets.Any(b => b.DepartmentId == normalizedDept &&
            b.ExpenseCategory == normalizedCategory &&
            b.ProjectId == normalizedProject &&
            b.Year == request.Year &&
            b.Month == request.Month);
        if (memExists)
            return ServiceResult<Budget>.Failure("该维度的预算池已存在。", "BUDGET_EXISTS");

        var memItem = new Budget
        {
            TenantId = TenantId,
            DepartmentId = normalizedDept,
            ExpenseCategory = normalizedCategory,
            ProjectId = normalizedProject,
            Year = request.Year,
            Month = request.Month,
            AllocatedAmount = request.AllocatedAmount,
            CommittedAmount = 0m,
            ActualAmount = 0m,
            Status = BudgetStatus.Active,
            CreatedBy = actor.Id
        };
        _memoryBudgets.Add(memItem);
        _memoryTransactions.Add(new BudgetTransaction
        {
            TenantId = TenantId,
            BudgetId = memItem.Id,
            BusinessType = "Budget",
            BusinessId = memItem.Id,
            BusinessNumber = $"INIT-{request.Year}",
            TransactionType = BudgetTransactionType.Adjusted,
            Amount = request.AllocatedAmount,
            BalanceAfter = request.AllocatedAmount,
            Description = "期初预算编制录入",
            OperatorId = actor.Id
        });
        return ServiceResult<Budget>.Success(memItem);
    }

    public ServiceResult<Budget> Adjust(Employee actor, Guid id, AdjustBudgetRequest request)
    {
        if (!CanManageBudgets(actor))
            return ServiceResult<Budget>.Failure("无预算调整权限。", "AUTH_002");
        if (request.Amount == 0)
            return ServiceResult<Budget>.Failure("调整金额不能为 0。");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult<Budget>.Failure("请填写预算调整原因。");

        if (db is not null)
        {
            var record = db.Budgets.SingleOrDefault(b => b.TenantId == TenantId && b.Id == id);
            if (record is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");

            var newAllocated = record.AllocatedAmount + request.Amount;
            if (newAllocated < 0)
                return ServiceResult<Budget>.Failure("调整后总编制预算不能小于 0。");

            var occupied = record.CommittedAmount + record.ActualAmount;
            if (newAllocated < occupied)
                return ServiceResult<Budget>.Failure($"调整后预算额度 ({newAllocated:N2}) 不能小于已占用与实支总额 ({occupied:N2})。");

            record.AllocatedAmount = newAllocated;
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;

            var balanceAfter = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
            var tx = new BudgetTransactionRecord
            {
                TenantId = TenantId,
                BudgetId = record.Id,
                BusinessType = "Budget",
                BusinessId = record.Id,
                BusinessNumber = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
                TransactionType = "ADJUSTED",
                Amount = request.Amount,
                BalanceAfter = balanceAfter,
                Description = request.Reason.Trim(),
                OperatorId = actor.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.BudgetTransactions.Add(tx);
            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "BUDGET_ADJUSTED",
                ResourceType = "Budget",
                ResourceId = record.Id.ToString(),
                Summary = $"调整预算额度 {(request.Amount > 0 ? $"+{request.Amount:N2}" : $"{request.Amount:N2}")} 元，原因：{request.Reason.Trim()}"
            });
            db.SaveChanges();

            return ServiceResult<Budget>.Success(MapToDomain(record));
        }

        var memBudget = _memoryBudgets.SingleOrDefault(b => b.Id == id);
        if (memBudget is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
        var memNewAllocated = memBudget.AllocatedAmount + request.Amount;
        if (memNewAllocated < (memBudget.CommittedAmount + memBudget.ActualAmount))
            return ServiceResult<Budget>.Failure("调整后预算额度不能小于已占用与实支总额。");

        memBudget.AllocatedAmount = memNewAllocated;
        memBudget.ConcurrencyVersion++;
        memBudget.UpdatedAt = DateTimeOffset.UtcNow;
        _memoryTransactions.Add(new BudgetTransaction
        {
            TenantId = TenantId,
            BudgetId = memBudget.Id,
            BusinessType = "Budget",
            BusinessId = memBudget.Id,
            BusinessNumber = $"ADJ-{DateTime.UtcNow:yyyyMMdd}",
            TransactionType = BudgetTransactionType.Adjusted,
            Amount = request.Amount,
            BalanceAfter = memBudget.AvailableAmount,
            Description = request.Reason.Trim(),
            OperatorId = actor.Id
        });
        return ServiceResult<Budget>.Success(memBudget);
    }

    public BudgetCheckResult Check(Employee actor, BudgetCheckRequest request)
    {
        var targetDept = string.IsNullOrWhiteSpace(request.DepartmentId) ? (actor.DepartmentId ?? string.Empty) : request.DepartmentId.Trim();
        var visibleDepts = GetVisibleDepartmentIds(actor);
        if (visibleDepts is not null && !visibleDepts.Contains(targetDept))
        {
            return new BudgetCheckResult(
                IsAllowed: false,
                IsExceeded: true,
                AvailableAmount: 0m,
                AllocatedAmount: 0m,
                CommittedAmount: 0m,
                ActualAmount: 0m,
                RequestedAmount: request.Amount,
                WarningMessage: "无权预检该部门预算。",
                BlockWhenExceeded: true);
        }

        var targetYear = request.Year ?? DateTime.Today.Year;
        var targetMonth = request.Month ?? DateTime.Today.Month;
        var matching = FindMatchingBudget(TenantId, targetDept, request.ExpenseCategory, targetYear, targetMonth);

        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Expense, "ExpensePolicy");
        ExpensePolicyConfig? policy = null;
        if (configRecord is not null)
        {
            try { policy = System.Text.Json.JsonSerializer.Deserialize<ExpensePolicyConfig>(configRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions); }
            catch { }
        }
        var blockWhenExceeded = policy?.BlockWhenExceeded ?? false;

        if (matching is null)
        {
            // 若未配置该部门预算池，则不作超额阻断，保持开放自由申请
            return new BudgetCheckResult(
                IsAllowed: true,
                IsExceeded: false,
                AvailableAmount: 9999999m,
                AllocatedAmount: 0m,
                CommittedAmount: 0m,
                ActualAmount: 0m,
                RequestedAmount: request.Amount,
                WarningMessage: null,
                BlockWhenExceeded: blockWhenExceeded);
        }

        var available = matching.AllocatedAmount - matching.CommittedAmount - matching.ActualAmount;
        var isExceeded = request.Amount > available;
        var isAllowed = !isExceeded || !blockWhenExceeded;
        string? warning = null;
        if (isExceeded)
        {
            var diff = request.Amount - available;
            var dimName = matching.ExpenseCategory is not null ? $"【{matching.DepartmentId}/{matching.ExpenseCategory}】" : $"【{matching.DepartmentId}】";
            warning = blockWhenExceeded
                ? $"部门预算不足：{dimName} 剩余可用额度 {available:N2} 元，本次申请 {request.Amount:N2} 元（超额 {diff:N2} 元），已被系统严格阻断。"
                : $"部门预算预警：{dimName} 剩余可用额度 {available:N2} 元，本次申请超额 {diff:N2} 元，提交后将触发特批审批。";
        }

        return new BudgetCheckResult(
            IsAllowed: isAllowed,
            IsExceeded: isExceeded,
            AvailableAmount: available,
            AllocatedAmount: matching.AllocatedAmount,
            CommittedAmount: matching.CommittedAmount,
            ActualAmount: matching.ActualAmount,
            RequestedAmount: request.Amount,
            WarningMessage: warning,
            BlockWhenExceeded: blockWhenExceeded);
    }

    public ServiceResult<Guid?> Reserve(
        string tenantId,
        string departmentId,
        string? expenseCategory,
        decimal amount,
        string businessType,
        Guid businessId,
        string businessNumber,
        string operatorId,
        bool blockWhenExceeded = false)
    {
        if (amount <= 0) return ServiceResult<Guid?>.Success(null);

        var targetYear = DateTime.Today.Year;
        var targetMonth = DateTime.Today.Month;

        if (db is not null)
        {
            var record = FindMatchingBudgetRecord(tenantId, departmentId, expenseCategory, targetYear, targetMonth);
            if (record is null) return ServiceResult<Guid?>.Success(null); // 无预算池直接放行

            var available = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
            if (amount > available && blockWhenExceeded)
            {
                return ServiceResult<Guid?>.Failure(
                    $"部门预算不足：可用额度 {available:N2} 元，申请金额 {amount:N2} 元，超出 {(amount - available):N2} 元。",
                    "BUDGET_EXCEEDED");
            }

            record.CommittedAmount += amount;
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;

            var newBalance = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
            var tx = new BudgetTransactionRecord
            {
                TenantId = tenantId,
                BudgetId = record.Id,
                BusinessType = businessType,
                BusinessId = businessId,
                BusinessNumber = businessNumber,
                TransactionType = "RESERVED",
                Amount = amount,
                BalanceAfter = newBalance,
                Description = $"{businessType} 申请【{businessNumber}】预占额度",
                OperatorId = operatorId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.BudgetTransactions.Add(tx);
            db.SaveChanges();

            return ServiceResult<Guid?>.Success(record.Id);
        }

        var mem = FindMatchingBudget(tenantId, departmentId, expenseCategory, targetYear, targetMonth);
        if (mem is null) return ServiceResult<Guid?>.Success(null);

        var memAvailable = mem.AllocatedAmount - mem.CommittedAmount - mem.ActualAmount;
        if (amount > memAvailable && blockWhenExceeded)
        {
            return ServiceResult<Guid?>.Failure($"部门预算不足：可用额度 {memAvailable:N2} 元。", "BUDGET_EXCEEDED");
        }

        mem.CommittedAmount += amount;
        mem.ConcurrencyVersion++;
        mem.UpdatedAt = DateTimeOffset.UtcNow;
        _memoryTransactions.Add(new BudgetTransaction
        {
            TenantId = tenantId,
            BudgetId = mem.Id,
            BusinessType = businessType,
            BusinessId = businessId,
            BusinessNumber = businessNumber,
            TransactionType = BudgetTransactionType.Reserved,
            Amount = amount,
            BalanceAfter = mem.AvailableAmount,
            Description = $"{businessType} 申请【{businessNumber}】预占额度",
            OperatorId = operatorId
        });
        return ServiceResult<Guid?>.Success(mem.Id);
    }

    public ServiceResult<bool> Release(
        string tenantId,
        Guid? budgetId,
        string businessType,
        Guid businessId,
        string businessNumber,
        decimal amount,
        string operatorId,
        string? reason = null)
    {
        if (amount <= 0) return ServiceResult<bool>.Success(true);

        if (db is not null)
        {
            BudgetRecord? record = null;
            if (budgetId.HasValue)
            {
                record = db.Budgets.SingleOrDefault(b => b.TenantId == tenantId && b.Id == budgetId.Value);
            }
            if (record is null)
            {
                var prevTx = db.BudgetTransactions.AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId && t.TransactionType == "RESERVED")
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefault();
                if (prevTx is not null)
                {
                    record = db.Budgets.SingleOrDefault(b => b.TenantId == tenantId && b.Id == prevTx.BudgetId);
                }
            }

            if (record is null) return ServiceResult<bool>.Success(true);

            record.CommittedAmount = Math.Max(0m, record.CommittedAmount - amount);
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;

            var newBalance = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
            var tx = new BudgetTransactionRecord
            {
                TenantId = tenantId,
                BudgetId = record.Id,
                BusinessType = businessType,
                BusinessId = businessId,
                BusinessNumber = businessNumber,
                TransactionType = "RELEASED",
                Amount = amount,
                BalanceAfter = newBalance,
                Description = reason ?? $"{businessType} 单据【{businessNumber}】释放退还预占额度",
                OperatorId = operatorId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.BudgetTransactions.Add(tx);
            db.SaveChanges();
            return ServiceResult<bool>.Success(true);
        }

        var mem = budgetId.HasValue ? _memoryBudgets.SingleOrDefault(b => b.Id == budgetId.Value) : null;
        if (mem is null)
        {
            var prevTx = _memoryTransactions
                .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId && t.TransactionType == BudgetTransactionType.Reserved)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();
            if (prevTx is not null) mem = _memoryBudgets.SingleOrDefault(b => b.Id == prevTx.BudgetId);
        }
        if (mem is null) return ServiceResult<bool>.Success(true);

        mem.CommittedAmount = Math.Max(0m, mem.CommittedAmount - amount);
        mem.ConcurrencyVersion++;
        mem.UpdatedAt = DateTimeOffset.UtcNow;
        _memoryTransactions.Add(new BudgetTransaction
        {
            TenantId = tenantId,
            BudgetId = mem.Id,
            BusinessType = businessType,
            BusinessId = businessId,
            BusinessNumber = businessNumber,
            TransactionType = BudgetTransactionType.Released,
            Amount = amount,
            BalanceAfter = mem.AvailableAmount,
            Description = reason ?? $"{businessType} 单据【{businessNumber}】释放退还预占额度",
            OperatorId = operatorId
        });
        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<bool> Consume(
        string tenantId,
        Guid? budgetId,
        string businessType,
        Guid businessId,
        string businessNumber,
        decimal amount,
        string operatorId,
        string? description = null)
    {
        if (amount <= 0) return ServiceResult<bool>.Success(true);

        if (db is not null)
        {
            BudgetRecord? record = null;
            if (budgetId.HasValue)
            {
                record = db.Budgets.SingleOrDefault(b => b.TenantId == tenantId && b.Id == budgetId.Value);
            }
            if (record is null)
            {
                var prevTx = db.BudgetTransactions.AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId && t.TransactionType == "RESERVED")
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefault();
                if (prevTx is not null)
                {
                    record = db.Budgets.SingleOrDefault(b => b.TenantId == tenantId && b.Id == prevTx.BudgetId);
                }
            }

            if (record is null) return ServiceResult<bool>.Success(true);

            record.CommittedAmount = Math.Max(0m, record.CommittedAmount - amount);
            record.ActualAmount += amount;
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;

            var newBalance = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
            var tx = new BudgetTransactionRecord
            {
                TenantId = tenantId,
                BudgetId = record.Id,
                BusinessType = businessType,
                BusinessId = businessId,
                BusinessNumber = businessNumber,
                TransactionType = "CONSUMED",
                Amount = amount,
                BalanceAfter = newBalance,
                Description = description ?? $"{businessType} 单据【{businessNumber}】付款结转实支",
                OperatorId = operatorId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.BudgetTransactions.Add(tx);
            db.SaveChanges();
            return ServiceResult<bool>.Success(true);
        }

        var mem = budgetId.HasValue ? _memoryBudgets.SingleOrDefault(b => b.Id == budgetId.Value) : null;
        if (mem is null)
        {
            var prevTx = _memoryTransactions
                .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId && t.TransactionType == BudgetTransactionType.Reserved)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();
            if (prevTx is not null) mem = _memoryBudgets.SingleOrDefault(b => b.Id == prevTx.BudgetId);
        }
        if (mem is null) return ServiceResult<bool>.Success(true);

        mem.CommittedAmount = Math.Max(0m, mem.CommittedAmount - amount);
        mem.ActualAmount += amount;
        mem.ConcurrencyVersion++;
        mem.UpdatedAt = DateTimeOffset.UtcNow;
        _memoryTransactions.Add(new BudgetTransaction
        {
            TenantId = tenantId,
            BudgetId = mem.Id,
            BusinessType = businessType,
            BusinessId = businessId,
            BusinessNumber = businessNumber,
            TransactionType = BudgetTransactionType.Consumed,
            Amount = amount,
            BalanceAfter = mem.AvailableAmount,
            Description = description ?? $"{businessType} 单据【{businessNumber}】付款结转实支",
            OperatorId = operatorId
        });
        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<PagedResponse<BudgetTransaction>> GetTransactions(Employee actor, Guid budgetId, int? requestedPage, int? requestedPageSize)
    {
        Budget? budget;
        if (db is not null)
        {
            var bRecord = db.Budgets.AsNoTracking().SingleOrDefault(b => b.TenantId == TenantId && b.Id == budgetId);
            budget = bRecord is null ? null : MapToDomain(bRecord);
        }
        else
        {
            budget = _memoryBudgets.SingleOrDefault(b => b.Id == budgetId);
        }

        if (budget is null)
            return ServiceResult<PagedResponse<BudgetTransaction>>.Failure("预算池不存在。", "DATA_001");

        var visibleDepts = GetVisibleDepartmentIds(actor);
        if (visibleDepts is not null && !visibleDepts.Contains(budget.DepartmentId))
            return ServiceResult<PagedResponse<BudgetTransaction>>.Failure("无权查看该部门预算变动流水。", "AUTH_002");

        var pageSize = Math.Clamp(requestedPageSize ?? 20, 1, 100);

        if (db is not null)
        {
            var source = db.BudgetTransactions.AsNoTracking().Where(t => t.TenantId == TenantId && t.BudgetId == budgetId);
            var total = source.Count();
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)pageSize));
            var page = Math.Clamp(requestedPage ?? 1, 1, totalPages);

            var items = source.OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new BudgetTransaction
                {
                    Id = t.Id,
                    TenantId = t.TenantId,
                    BudgetId = t.BudgetId,
                    BusinessType = t.BusinessType,
                    BusinessId = t.BusinessId,
                    BusinessNumber = t.BusinessNumber,
                    TransactionType = Enum.Parse<BudgetTransactionType>(t.TransactionType, true),
                    Amount = t.Amount,
                    BalanceAfter = t.BalanceAfter,
                    Description = t.Description,
                    OperatorId = t.OperatorId,
                    CreatedAt = t.CreatedAt
                })
                .ToList();

            return ServiceResult<PagedResponse<BudgetTransaction>>.Success(new PagedResponse<BudgetTransaction>(items, total, page, pageSize, totalPages));
        }

        var memSource = _memoryTransactions.Where(t => t.BudgetId == budgetId).OrderByDescending(t => t.CreatedAt).ToList();
        var memTotal = memSource.Count;
        var memTotalPages = Math.Max(1, (int)Math.Ceiling(memTotal / (decimal)pageSize));
        var memPage = Math.Clamp(requestedPage ?? 1, 1, memTotalPages);
        var paged = memSource.Skip((memPage - 1) * pageSize).Take(pageSize).ToList();
        return ServiceResult<PagedResponse<BudgetTransaction>>.Success(new PagedResponse<BudgetTransaction>(paged, memTotal, memPage, pageSize, memTotalPages));
    }

    private BudgetRecord? FindMatchingBudgetRecord(string tenantId, string departmentId, string? expenseCategory, int year, int month)
    {
        if (db is null) return null;
        var query = db.Budgets.Where(b => b.TenantId == tenantId && b.Status == "ACTIVE" && b.Year == year);

        // 1. 部门 + 科目 + 月度
        if (!string.IsNullOrWhiteSpace(expenseCategory))
        {
            var exactMonth = query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == expenseCategory && b.Month == targetMonthSafe(month));
            if (exactMonth is not null) return exactMonth;

            // 2. 部门 + 科目 + 全年(month == 0)
            var exactYear = query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == expenseCategory && b.Month == 0);
            if (exactYear is not null) return exactYear;
        }

        // 3. 部门通用 + 月度
        var deptMonth = query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == null && b.Month == targetMonthSafe(month));
        if (deptMonth is not null) return deptMonth;

        // 4. 部门通用 + 全年
        var deptYear = query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == null && b.Month == 0);
        return deptYear;
    }

    private Budget? FindMatchingBudget(string tenantId, string departmentId, string? expenseCategory, int year, int month)
    {
        if (db is not null)
        {
            var record = FindMatchingBudgetRecord(tenantId, departmentId, expenseCategory, year, month);
            return record is null ? null : MapToDomain(record);
        }

        var query = _memoryBudgets.Where(b => b.TenantId == tenantId && b.Status == BudgetStatus.Active && b.Year == year);
        if (!string.IsNullOrWhiteSpace(expenseCategory))
        {
            var m1 = query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == expenseCategory && b.Month == targetMonthSafe(month));
            if (m1 is not null) return m1;

            var m2 = query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == expenseCategory && b.Month == 0);
            if (m2 is not null) return m2;
        }

        var m3 = query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == null && b.Month == targetMonthSafe(month));
        if (m3 is not null) return m3;

        return query.FirstOrDefault(b => b.DepartmentId == departmentId && b.ExpenseCategory == null && b.Month == 0);
    }

    private static int targetMonthSafe(int month) => (month >= 1 && month <= 12) ? month : 0;

    private bool CanManageBudgets(Employee actor) =>
        data.HasPermission(actor, OaPermissions.ExpenseAllView) ||
        actor.Role == "总经理" ||
        actor.Role == "财务经理" ||
        data.HasRole(actor, "财务经理");

    private bool CanViewAllBudgets(Employee actor) =>
        CanManageBudgets(actor) ||
        data.HasPermission(actor, OaPermissions.ExpensePay);

    private static Budget MapToDomain(BudgetRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        DepartmentId = r.DepartmentId,
        ExpenseCategory = r.ExpenseCategory,
        ProjectId = r.ProjectId,
        Year = r.Year,
        Month = r.Month,
        AllocatedAmount = r.AllocatedAmount,
        CommittedAmount = r.CommittedAmount,
        ActualAmount = r.ActualAmount,
        Status = Enum.TryParse<BudgetStatus>(r.Status, true, out var st) ? st : BudgetStatus.Active,
        ConcurrencyVersion = r.ConcurrencyVersion,
        CreatedBy = r.CreatedBy,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };
}
