using System.Globalization;
using System.Text;
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

    private (string Id, string Name) NormalizeDepartment(string deptIdOrName)
    {
        var trimmed = deptIdOrName.Trim();
        var match = data.Departments.FirstOrDefault(d =>
            string.Equals(d.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return (match.Id, match.Name);
        return (trimmed, trimmed);
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
        var myNorm = NormalizeDepartment(actor.DepartmentId);
        set.Add(myNorm.Id);
        set.Add(myNorm.Name);

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

    public IReadOnlyList<Budget> List(Employee actor, string? departmentId = null, int? year = null, int? month = null, string? expenseCategory = null, string? status = null, string? projectId = null)
    {
        var visibleDepts = GetVisibleDepartmentIds(actor);

        if (db is not null)
        {
            var query = db.Budgets.AsNoTracking().Where(b => b.TenantId == TenantId);

            if (visibleDepts is not null)
            {
                if (!string.IsNullOrWhiteSpace(departmentId))
                {
                    var norm = NormalizeDepartment(departmentId);
                    if (!visibleDepts.Contains(norm.Id) && !visibleDepts.Contains(norm.Name))
                        return [];
                    query = query.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
                }
                else
                {
                    query = query.Where(b => visibleDepts.Contains(b.DepartmentId));
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(departmentId))
                {
                    var norm = NormalizeDepartment(departmentId);
                    query = query.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
                }
            }

            if (year.HasValue)
                query = query.Where(b => b.Year == year.Value);
            if (month.HasValue)
                query = query.Where(b => b.Month == month.Value);
            if (!string.IsNullOrWhiteSpace(expenseCategory))
                query = query.Where(b => b.ExpenseCategory == expenseCategory.Trim());
            if (!string.IsNullOrWhiteSpace(projectId))
                query = query.Where(b => b.ProjectId == projectId.Trim());
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(b => b.Status == status.Trim().ToUpperInvariant());

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
                var norm = NormalizeDepartment(departmentId);
                if (!visibleDepts.Contains(norm.Id) && !visibleDepts.Contains(norm.Name))
                    return [];
                list = list.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
            }
            else
            {
                list = list.Where(b => visibleDepts.Contains(b.DepartmentId));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(departmentId))
            {
                var norm = NormalizeDepartment(departmentId);
                list = list.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
            }
        }

        if (year.HasValue)
            list = list.Where(b => b.Year == year.Value);
        if (month.HasValue)
            list = list.Where(b => b.Month == month.Value);
        if (!string.IsNullOrWhiteSpace(expenseCategory))
            list = list.Where(b => b.ExpenseCategory == expenseCategory.Trim());
        if (!string.IsNullOrWhiteSpace(projectId))
            list = list.Where(b => b.ProjectId == projectId.Trim());
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BudgetStatus>(status, true, out var s))
            list = list.Where(b => b.Status == s);

        return list.OrderByDescending(b => b.Year).ThenBy(b => b.Month).ToList();
    }

    public ServiceResult<PagedResponse<Budget>> ListPaged(Employee actor, BudgetQuery query)
    {
        var visibleDepts = GetVisibleDepartmentIds(actor);
        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 100);

        if (db is not null)
        {
            var q = db.Budgets.AsNoTracking().Where(b => b.TenantId == TenantId);

            if (visibleDepts is not null)
            {
                if (!string.IsNullOrWhiteSpace(query.DepartmentId))
                {
                    var norm = NormalizeDepartment(query.DepartmentId);
                    if (!visibleDepts.Contains(norm.Id) && !visibleDepts.Contains(norm.Name))
                        return ServiceResult<PagedResponse<Budget>>.Success(new PagedResponse<Budget>([], 0, 1, pageSize, 0));
                    q = q.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
                }
                else
                {
                    q = q.Where(b => visibleDepts.Contains(b.DepartmentId));
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(query.DepartmentId))
                {
                    var norm = NormalizeDepartment(query.DepartmentId);
                    q = q.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
                }
            }

            if (query.Year.HasValue)
                q = q.Where(b => b.Year == query.Year.Value);
            if (query.Month.HasValue)
                q = q.Where(b => b.Month == query.Month.Value);
            if (!string.IsNullOrWhiteSpace(query.ExpenseCategory))
                q = q.Where(b => b.ExpenseCategory == query.ExpenseCategory.Trim());
            if (!string.IsNullOrWhiteSpace(query.ProjectId))
                q = q.Where(b => b.ProjectId == query.ProjectId.Trim());
            if (!string.IsNullOrWhiteSpace(query.Status))
                q = q.Where(b => b.Status == query.Status.Trim().ToUpperInvariant());

            var total = q.Count();
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)pageSize));
            var page = Math.Clamp(query.Page ?? 1, 1, totalPages);

            var items = q.OrderByDescending(b => b.UpdatedAt)
                .ThenByDescending(b => b.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => MapToDomain(b))
                .ToList();

            return ServiceResult<PagedResponse<Budget>>.Success(new PagedResponse<Budget>(items, total, page, pageSize, totalPages));
        }

        var memList = _memoryBudgets.AsEnumerable();
        if (visibleDepts is not null)
        {
            if (!string.IsNullOrWhiteSpace(query.DepartmentId))
            {
                var norm = NormalizeDepartment(query.DepartmentId);
                if (!visibleDepts.Contains(norm.Id) && !visibleDepts.Contains(norm.Name))
                    return ServiceResult<PagedResponse<Budget>>.Success(new PagedResponse<Budget>([], 0, 1, pageSize, 0));
                memList = memList.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
            }
            else
            {
                memList = memList.Where(b => visibleDepts.Contains(b.DepartmentId));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(query.DepartmentId))
            {
                var norm = NormalizeDepartment(query.DepartmentId);
                memList = memList.Where(b => b.DepartmentId == norm.Id || b.DepartmentId == norm.Name);
            }
        }

        if (query.Year.HasValue)
            memList = memList.Where(b => b.Year == query.Year.Value);
        if (query.Month.HasValue)
            memList = memList.Where(b => b.Month == query.Month.Value);
        if (!string.IsNullOrWhiteSpace(query.ExpenseCategory))
            memList = memList.Where(b => b.ExpenseCategory == query.ExpenseCategory.Trim());
        if (!string.IsNullOrWhiteSpace(query.ProjectId))
            memList = memList.Where(b => b.ProjectId == query.ProjectId.Trim());
        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<BudgetStatus>(query.Status, true, out var st))
            memList = memList.Where(b => b.Status == st);

        var memTotal = memList.Count();
        var memTotalPages = Math.Max(1, (int)Math.Ceiling(memTotal / (decimal)pageSize));
        var memPage = Math.Clamp(query.Page ?? 1, 1, memTotalPages);
        var memItems = memList.OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.Id)
            .Skip((memPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return ServiceResult<PagedResponse<Budget>>.Success(new PagedResponse<Budget>(memItems, memTotal, memPage, pageSize, memTotalPages));
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
        var norm = NormalizeDepartment(budget.DepartmentId);
        if (visibleDepts is not null && !visibleDepts.Contains(norm.Id) && !visibleDepts.Contains(norm.Name))
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

        var normDept = NormalizeDepartment(request.DepartmentId);
        var normalizedCategory = string.IsNullOrWhiteSpace(request.ExpenseCategory) ? null : request.ExpenseCategory.Trim();
        var normalizedProject = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId.Trim();
        var initialStatus = request.AutoPublish ? "ACTIVE" : "DRAFT";
        var memStatus = request.AutoPublish ? BudgetStatus.Active : BudgetStatus.Draft;

        if (db is not null)
        {
            var exists = db.Budgets.Any(b => b.TenantId == TenantId &&
                (b.DepartmentId == normDept.Id || b.DepartmentId == normDept.Name) &&
                b.ExpenseCategory == normalizedCategory &&
                b.ProjectId == normalizedProject &&
                b.Year == request.Year &&
                b.Month == request.Month);

            if (exists)
                return ServiceResult<Budget>.Failure("该维度（部门、科目、项目、年度与月份）的预算池已存在，请直接调整额度。", "BUDGET_EXISTS");

            var record = new BudgetRecord
            {
                TenantId = TenantId,
                DepartmentId = normDept.Id,
                ExpenseCategory = normalizedCategory,
                ProjectId = normalizedProject,
                Year = request.Year,
                Month = request.Month,
                AllocatedAmount = request.AllocatedAmount,
                CommittedAmount = 0m,
                ActualAmount = 0m,
                Status = initialStatus,
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
                Description = request.AutoPublish ? "期初预算编制录入并自动发布" : "期初预算编制录入（草稿）",
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
                Summary = $"编制部门【{normDept.Id}】{request.Year}年{(request.Month > 0 ? $"{request.Month}月" : "全年")}预算 {request.AllocatedAmount:N2} 元（状态：{initialStatus}）"
            });
            db.SaveChanges();

            return ServiceResult<Budget>.Success(MapToDomain(record));
        }

        var memExists = _memoryBudgets.Any(b => (b.DepartmentId == normDept.Id || b.DepartmentId == normDept.Name) &&
            b.ExpenseCategory == normalizedCategory &&
            b.ProjectId == normalizedProject &&
            b.Year == request.Year &&
            b.Month == request.Month);
        if (memExists)
            return ServiceResult<Budget>.Failure("该维度的预算池已存在。", "BUDGET_EXISTS");

        var memItem = new Budget
        {
            TenantId = TenantId,
            DepartmentId = normDept.Id,
            ExpenseCategory = normalizedCategory,
            ProjectId = normalizedProject,
            Year = request.Year,
            Month = request.Month,
            AllocatedAmount = request.AllocatedAmount,
            CommittedAmount = 0m,
            ActualAmount = 0m,
            Status = memStatus,
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
            Description = request.AutoPublish ? "期初预算编制录入并自动发布" : "期初预算编制录入（草稿）",
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
            if (request.ExpectedVersion.HasValue && record.ConcurrencyVersion != request.ExpectedVersion.Value)
                return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");

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
        if (request.ExpectedVersion.HasValue && memBudget.ConcurrencyVersion != request.ExpectedVersion.Value)
            return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");

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

    public ServiceResult<Budget> Publish(Employee actor, Guid id, BudgetStatusChangeRequest? request = null)
    {
        if (!CanManageBudgets(actor))
            return ServiceResult<Budget>.Failure("无预算管理权限。", "AUTH_002");

        if (db is not null)
        {
            var record = db.Budgets.SingleOrDefault(b => b.TenantId == TenantId && b.Id == id);
            if (record is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
            if (request?.ExpectedVersion.HasValue == true && record.ConcurrencyVersion != request.ExpectedVersion.Value)
                return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");

            if (record.Status == "ACTIVE") return ServiceResult<Budget>.Success(MapToDomain(record));
            if (record.Status is "FROZEN" or "CLOSED")
                return ServiceResult<Budget>.Failure($"当前状态（{record.Status}）不支持发布生效。", "STATE_001");

            record.Status = "ACTIVE";
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            db.BudgetTransactions.Add(new BudgetTransactionRecord
            {
                TenantId = TenantId,
                BudgetId = record.Id,
                BusinessType = "Budget",
                BusinessId = record.Id,
                BusinessNumber = $"PUB-{DateTime.UtcNow:yyyyMMdd}",
                TransactionType = "ADJUSTED",
                Amount = 0m,
                BalanceAfter = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount,
                Description = request?.Reason ?? "发布生效预算池",
                OperatorId = actor.Id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "BUDGET_PUBLISHED",
                ResourceType = "Budget",
                ResourceId = record.Id.ToString(),
                Summary = $"发布生效预算池【{record.DepartmentId}/{record.ExpenseCategory ?? "通用"}】"
            });
            db.SaveChanges();
            return ServiceResult<Budget>.Success(MapToDomain(record));
        }

        var mem = _memoryBudgets.SingleOrDefault(b => b.Id == id);
        if (mem is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
        if (request?.ExpectedVersion.HasValue == true && mem.ConcurrencyVersion != request.ExpectedVersion.Value)
            return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (mem.Status == BudgetStatus.Active) return ServiceResult<Budget>.Success(mem);
        if (mem.Status is BudgetStatus.Frozen or BudgetStatus.Closed)
            return ServiceResult<Budget>.Failure("当前状态不支持发布生效。", "STATE_001");

        mem.Status = BudgetStatus.Active;
        mem.ConcurrencyVersion++;
        mem.UpdatedAt = DateTimeOffset.UtcNow;
        return ServiceResult<Budget>.Success(mem);
    }

    public ServiceResult<Budget> Freeze(Employee actor, Guid id, BudgetStatusChangeRequest? request = null)
    {
        if (!CanManageBudgets(actor))
            return ServiceResult<Budget>.Failure("无预算管理权限。", "AUTH_002");

        if (db is not null)
        {
            var record = db.Budgets.SingleOrDefault(b => b.TenantId == TenantId && b.Id == id);
            if (record is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
            if (request?.ExpectedVersion.HasValue == true && record.ConcurrencyVersion != request.ExpectedVersion.Value)
                return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");

            if (record.Status == "FROZEN") return ServiceResult<Budget>.Success(MapToDomain(record));
            if (record.Status != "ACTIVE")
                return ServiceResult<Budget>.Failure($"只有执行中的预算池可以冻结（当前状态：{record.Status}）。", "STATE_001");

            record.Status = "FROZEN";
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            db.BudgetTransactions.Add(new BudgetTransactionRecord
            {
                TenantId = TenantId,
                BudgetId = record.Id,
                BusinessType = "Budget",
                BusinessId = record.Id,
                BusinessNumber = $"FRZ-{DateTime.UtcNow:yyyyMMdd}",
                TransactionType = "ADJUSTED",
                Amount = 0m,
                BalanceAfter = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount,
                Description = request?.Reason ?? "冻结预算池",
                OperatorId = actor.Id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "BUDGET_FROZEN",
                ResourceType = "Budget",
                ResourceId = record.Id.ToString(),
                Summary = $"冻结预算池【{record.DepartmentId}/{record.ExpenseCategory ?? "通用"}】"
            });
            db.SaveChanges();
            return ServiceResult<Budget>.Success(MapToDomain(record));
        }

        var mem = _memoryBudgets.SingleOrDefault(b => b.Id == id);
        if (mem is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
        if (request?.ExpectedVersion.HasValue == true && mem.ConcurrencyVersion != request.ExpectedVersion.Value)
            return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (mem.Status == BudgetStatus.Frozen) return ServiceResult<Budget>.Success(mem);
        if (mem.Status != BudgetStatus.Active)
            return ServiceResult<Budget>.Failure("只有执行中的预算池可以冻结。", "STATE_001");

        mem.Status = BudgetStatus.Frozen;
        mem.ConcurrencyVersion++;
        mem.UpdatedAt = DateTimeOffset.UtcNow;
        return ServiceResult<Budget>.Success(mem);
    }

    public ServiceResult<Budget> Unfreeze(Employee actor, Guid id, BudgetStatusChangeRequest? request = null)
    {
        if (!CanManageBudgets(actor))
            return ServiceResult<Budget>.Failure("无预算管理权限。", "AUTH_002");

        if (db is not null)
        {
            var record = db.Budgets.SingleOrDefault(b => b.TenantId == TenantId && b.Id == id);
            if (record is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
            if (request?.ExpectedVersion.HasValue == true && record.ConcurrencyVersion != request.ExpectedVersion.Value)
                return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");

            if (record.Status == "ACTIVE") return ServiceResult<Budget>.Success(MapToDomain(record));
            if (record.Status != "FROZEN")
                return ServiceResult<Budget>.Failure($"只有已冻结的预算池可以解冻（当前状态：{record.Status}）。", "STATE_001");

            record.Status = "ACTIVE";
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            db.BudgetTransactions.Add(new BudgetTransactionRecord
            {
                TenantId = TenantId,
                BudgetId = record.Id,
                BusinessType = "Budget",
                BusinessId = record.Id,
                BusinessNumber = $"UNFRZ-{DateTime.UtcNow:yyyyMMdd}",
                TransactionType = "ADJUSTED",
                Amount = 0m,
                BalanceAfter = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount,
                Description = request?.Reason ?? "解冻预算池",
                OperatorId = actor.Id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "BUDGET_UNFROZEN",
                ResourceType = "Budget",
                ResourceId = record.Id.ToString(),
                Summary = $"解冻预算池【{record.DepartmentId}/{record.ExpenseCategory ?? "通用"}】"
            });
            db.SaveChanges();
            return ServiceResult<Budget>.Success(MapToDomain(record));
        }

        var mem = _memoryBudgets.SingleOrDefault(b => b.Id == id);
        if (mem is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
        if (request?.ExpectedVersion.HasValue == true && mem.ConcurrencyVersion != request.ExpectedVersion.Value)
            return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (mem.Status == BudgetStatus.Active) return ServiceResult<Budget>.Success(mem);
        if (mem.Status != BudgetStatus.Frozen)
            return ServiceResult<Budget>.Failure("只有已冻结的预算池可以解冻。", "STATE_001");

        mem.Status = BudgetStatus.Active;
        mem.ConcurrencyVersion++;
        mem.UpdatedAt = DateTimeOffset.UtcNow;
        return ServiceResult<Budget>.Success(mem);
    }

    public ServiceResult<Budget> Close(Employee actor, Guid id, BudgetStatusChangeRequest? request = null)
    {
        if (!CanManageBudgets(actor))
            return ServiceResult<Budget>.Failure("无预算管理权限。", "AUTH_002");

        if (db is not null)
        {
            var record = db.Budgets.SingleOrDefault(b => b.TenantId == TenantId && b.Id == id);
            if (record is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
            if (request?.ExpectedVersion.HasValue == true && record.ConcurrencyVersion != request.ExpectedVersion.Value)
                return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");

            if (record.Status == "CLOSED") return ServiceResult<Budget>.Success(MapToDomain(record));
            if (record.Status == "DRAFT")
                return ServiceResult<Budget>.Failure("草稿状态预算池不能直接封账关闭。", "STATE_001");

            record.Status = "CLOSED";
            record.ConcurrencyVersion++;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            db.BudgetTransactions.Add(new BudgetTransactionRecord
            {
                TenantId = TenantId,
                BudgetId = record.Id,
                BusinessType = "Budget",
                BusinessId = record.Id,
                BusinessNumber = $"CLS-{DateTime.UtcNow:yyyyMMdd}",
                TransactionType = "ADJUSTED",
                Amount = 0m,
                BalanceAfter = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount,
                Description = request?.Reason ?? "封账关闭预算池",
                OperatorId = actor.Id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "BUDGET_CLOSED",
                ResourceType = "Budget",
                ResourceId = record.Id.ToString(),
                Summary = $"封账关闭预算池【{record.DepartmentId}/{record.ExpenseCategory ?? "通用"}】"
            });
            db.SaveChanges();
            return ServiceResult<Budget>.Success(MapToDomain(record));
        }

        var mem = _memoryBudgets.SingleOrDefault(b => b.Id == id);
        if (mem is null) return ServiceResult<Budget>.Failure("预算池不存在。", "DATA_001");
        if (request?.ExpectedVersion.HasValue == true && mem.ConcurrencyVersion != request.ExpectedVersion.Value)
            return ServiceResult<Budget>.Failure("预算池已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (mem.Status == BudgetStatus.Closed) return ServiceResult<Budget>.Success(mem);
        if (mem.Status == BudgetStatus.Draft)
            return ServiceResult<Budget>.Failure("草稿状态预算池不能直接封账关闭。", "STATE_001");

        mem.Status = BudgetStatus.Closed;
        mem.ConcurrencyVersion++;
        mem.UpdatedAt = DateTimeOffset.UtcNow;
        return ServiceResult<Budget>.Success(mem);
    }

    public BudgetCheckResult Check(Employee actor, BudgetCheckRequest request)
    {
        var targetDept = string.IsNullOrWhiteSpace(request.DepartmentId) ? (actor.DepartmentId ?? string.Empty) : request.DepartmentId.Trim();
        var visibleDepts = GetVisibleDepartmentIds(actor);
        var normDept = NormalizeDepartment(targetDept);
        if (visibleDepts is not null && !visibleDepts.Contains(normDept.Id) && !visibleDepts.Contains(normDept.Name))
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
                BlockWhenExceeded: true,
                BudgetId: null,
                HasConfiguredBudget: false);
        }

        var targetYear = request.Year ?? DateTime.Today.Year;
        var targetMonth = request.Month ?? DateTime.Today.Month;
        var matching = FindMatchingBudget(TenantId, targetDept, request.ExpenseCategory, targetYear, targetMonth, request.ProjectId);

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
            // 若未配置该部门预算池，则不作超额阻断，保持开放自由申请；明确返回 HasConfiguredBudget = false, AvailableAmount = 0m
            return new BudgetCheckResult(
                IsAllowed: true,
                IsExceeded: false,
                AvailableAmount: 0m,
                AllocatedAmount: 0m,
                CommittedAmount: 0m,
                ActualAmount: 0m,
                RequestedAmount: request.Amount,
                WarningMessage: "当前部门或科目未配置预算池，采用免预算放行策略。",
                BlockWhenExceeded: blockWhenExceeded,
                BudgetId: null,
                HasConfiguredBudget: false);
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
            BlockWhenExceeded: blockWhenExceeded,
            BudgetId: matching.Id,
            HasConfiguredBudget: true);
    }

    public ServiceResult<BudgetReserveResult> Reserve(
        string tenantId,
        string departmentId,
        string? expenseCategory,
        decimal amount,
        string businessType,
        Guid businessId,
        string businessNumber,
        string operatorId,
        bool blockWhenExceeded = false,
        int? targetYear = null,
        int? targetMonth = null,
        string? projectId = null,
        string? actionKeySuffix = null)
    {
        if (amount <= 0) return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(null));

        var year = targetYear ?? DateTime.Today.Year;
        var month = targetMonth ?? DateTime.Today.Month;
        var keySuffix = string.IsNullOrWhiteSpace(actionKeySuffix) ? (expenseCategory ?? "general") : actionKeySuffix;
        var actionKey = $"Reserve:{businessType}:{businessId}:{keySuffix}";

        if (db is not null)
        {
            using var dbTx = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
            try
            {
                var existingTx = db.BudgetTransactions.AsNoTracking().FirstOrDefault(t => t.TenantId == tenantId && t.ActionKey == actionKey);
                if (existingTx is not null)
                    return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(existingTx.BudgetId));

                var record = FindMatchingBudgetRecord(tenantId, departmentId, expenseCategory, year, month, projectId);
                if (record is null) return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(null)); // 无预算池直接放行

                // Exclusive row lock on matching budget pool
                db.Database.ExecuteSqlInterpolated($"SELECT \"Id\" FROM budget WHERE \"TenantId\" = {tenantId} AND \"Id\" = {record.Id} FOR UPDATE");
                db.Entry(record).Reload();

                var available = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
                var isOverBudget = amount > available;
                var overAmount = isOverBudget ? (amount - available) : 0m;

                if (isOverBudget && blockWhenExceeded)
                {
                    return ServiceResult<BudgetReserveResult>.Failure(
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
                    Description = isOverBudget
                        ? $"{businessType} 申请【{businessNumber}】超额预占 (超出 {overAmount:N2} 元)"
                        : $"{businessType} 申请【{businessNumber}】预占额度",
                    OperatorId = operatorId,
                    ActionKey = actionKey,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.BudgetTransactions.Add(tx);
                db.SaveChanges();
                dbTx?.Commit();

                return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(record.Id, isOverBudget, overAmount));
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
            {
                dbTx?.Rollback();
                var existingTx = db.BudgetTransactions.AsNoTracking().FirstOrDefault(t => t.TenantId == tenantId && t.ActionKey == actionKey);
                if (existingTx is not null) return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(existingTx.BudgetId));
                return ServiceResult<BudgetReserveResult>.Failure("并发预算预占冲突，请重试。", "CONCURRENCY_001");
            }
        }

        lock (_memoryTransactions)
        {
            var memExisting = _memoryTransactions.FirstOrDefault(t => t.TenantId == tenantId && t.ActionKey == actionKey);
            if (memExisting is not null) return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(memExisting.BudgetId));

            var mem = FindMatchingBudget(tenantId, departmentId, expenseCategory, year, month, projectId);
            if (mem is null) return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(null));

            var memAvailable = mem.AllocatedAmount - mem.CommittedAmount - mem.ActualAmount;
            var memIsOver = amount > memAvailable;
            var memOverAmount = memIsOver ? (amount - memAvailable) : 0m;
            if (memIsOver && blockWhenExceeded)
            {
                return ServiceResult<BudgetReserveResult>.Failure($"部门预算不足：可用额度 {memAvailable:N2} 元。", "BUDGET_EXCEEDED");
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
                OperatorId = operatorId,
                ActionKey = actionKey
            });
            return ServiceResult<BudgetReserveResult>.Success(new BudgetReserveResult(mem.Id, memIsOver, memOverAmount));
        }
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
            using var dbTx = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
            try
            {
                var prevTxs = db.BudgetTransactions.AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId)
                    .ToList();

                if (prevTxs.Count == 0) return ServiceResult<bool>.Success(true);

                if (budgetId.HasValue)
                {
                    var targetBudgetId = budgetId.Value;
                    var actionKey = $"Release:{businessType}:{businessId}:{targetBudgetId}";
                    var existingRel = prevTxs.FirstOrDefault(t => t.ActionKey == actionKey);
                    if (existingRel is not null) return ServiceResult<bool>.Success(true);

                    var poolTxs = prevTxs.Where(t => t.BudgetId == targetBudgetId).ToList();
                    var reservedTotal = poolTxs.Where(t => t.TransactionType == "RESERVED").Sum(t => t.Amount);
                    var releasedTotal = poolTxs.Where(t => t.TransactionType == "RELEASED").Sum(t => t.Amount);
                    var consumedTotal = poolTxs.Where(t => t.TransactionType == "CONSUMED").Sum(t => t.Amount);
                    var netCommitted = Math.Max(0m, reservedTotal - releasedTotal - consumedTotal);
                    var actualRelease = Math.Min(amount, netCommitted);

                    if (actualRelease > 0)
                    {
                        db.Database.ExecuteSqlInterpolated($"SELECT \"Id\" FROM budget WHERE \"TenantId\" = {tenantId} AND \"Id\" = {targetBudgetId} FOR UPDATE");
                        var record = db.Budgets.SingleOrDefault(b => b.TenantId == tenantId && b.Id == targetBudgetId);
                        if (record is not null)
                        {
                            db.Entry(record).Reload();
                            record.CommittedAmount = Math.Max(0m, record.CommittedAmount - actualRelease);
                            record.ConcurrencyVersion++;
                            record.UpdatedAt = DateTimeOffset.UtcNow;

                            var newBalance = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
                            db.BudgetTransactions.Add(new BudgetTransactionRecord
                            {
                                TenantId = tenantId,
                                BudgetId = record.Id,
                                BusinessType = businessType,
                                BusinessId = businessId,
                                BusinessNumber = businessNumber,
                                TransactionType = "RELEASED",
                                Amount = actualRelease,
                                BalanceAfter = newBalance,
                                Description = reason ?? $"{businessType} 单据【{businessNumber}】释放退还预占额度",
                                OperatorId = operatorId,
                                ActionKey = actionKey,
                                CreatedAt = DateTimeOffset.UtcNow
                            });
                            db.SaveChanges();
                        }
                    }
                }
                else
                {
                    var pools = prevTxs.GroupBy(t => t.BudgetId).ToList();
                    foreach (var grp in pools)
                    {
                        var poolId = grp.Key;
                        var actionKey = $"Release:{businessType}:{businessId}:{poolId}";
                        if (grp.Any(t => t.ActionKey == actionKey)) continue;

                        var reserved = grp.Where(t => t.TransactionType == "RESERVED").Sum(t => t.Amount);
                        var released = grp.Where(t => t.TransactionType == "RELEASED").Sum(t => t.Amount);
                        var consumed = grp.Where(t => t.TransactionType == "CONSUMED").Sum(t => t.Amount);
                        var net = Math.Max(0m, reserved - released - consumed);
                        if (net <= 0) continue;

                        db.Database.ExecuteSqlInterpolated($"SELECT \"Id\" FROM budget WHERE \"TenantId\" = {tenantId} AND \"Id\" = {poolId} FOR UPDATE");
                        var record = db.Budgets.SingleOrDefault(b => b.TenantId == tenantId && b.Id == poolId);
                        if (record is not null)
                        {
                            db.Entry(record).Reload();
                            record.CommittedAmount = Math.Max(0m, record.CommittedAmount - net);
                            record.ConcurrencyVersion++;
                            record.UpdatedAt = DateTimeOffset.UtcNow;

                            var newBalance = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
                            db.BudgetTransactions.Add(new BudgetTransactionRecord
                            {
                                TenantId = tenantId,
                                BudgetId = record.Id,
                                BusinessType = businessType,
                                BusinessId = businessId,
                                BusinessNumber = businessNumber,
                                TransactionType = "RELEASED",
                                Amount = net,
                                BalanceAfter = newBalance,
                                Description = reason ?? $"{businessType} 单据【{businessNumber}】释放退还预占额度",
                                OperatorId = operatorId,
                                ActionKey = actionKey,
                                CreatedAt = DateTimeOffset.UtcNow
                            });
                            db.SaveChanges();
                        }
                    }
                }

                dbTx?.Commit();
                return ServiceResult<bool>.Success(true);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
            {
                dbTx?.Rollback();
                return ServiceResult<bool>.Success(true);
            }
        }

        lock (_memoryTransactions)
        {
            var prevTxs = _memoryTransactions
                .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId)
                .ToList();

            if (prevTxs.Count == 0) return ServiceResult<bool>.Success(true);

            if (budgetId.HasValue)
            {
                var targetBudgetId = budgetId.Value;
                var actionKey = $"Release:{businessType}:{businessId}:{targetBudgetId}";
                if (prevTxs.Any(t => t.ActionKey == actionKey)) return ServiceResult<bool>.Success(true);

                var poolTxs = prevTxs.Where(t => t.BudgetId == targetBudgetId).ToList();
                var reservedTotal = poolTxs.Where(t => t.TransactionType == BudgetTransactionType.Reserved).Sum(t => t.Amount);
                var releasedTotal = poolTxs.Where(t => t.TransactionType == BudgetTransactionType.Released).Sum(t => t.Amount);
                var consumedTotal = poolTxs.Where(t => t.TransactionType == BudgetTransactionType.Consumed).Sum(t => t.Amount);
                var netCommitted = Math.Max(0m, reservedTotal - releasedTotal - consumedTotal);
                var actualRelease = Math.Min(amount, netCommitted);

                if (actualRelease > 0)
                {
                    var mem = _memoryBudgets.SingleOrDefault(b => b.Id == targetBudgetId);
                    if (mem is not null)
                    {
                        mem.CommittedAmount = Math.Max(0m, mem.CommittedAmount - actualRelease);
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
                            Amount = actualRelease,
                            BalanceAfter = mem.AvailableAmount,
                            Description = reason ?? $"{businessType} 单据【{businessNumber}】释放退还预占额度",
                            OperatorId = operatorId,
                            ActionKey = actionKey
                        });
                    }
                }
            }
            else
            {
                var pools = prevTxs.GroupBy(t => t.BudgetId).ToList();
                foreach (var grp in pools)
                {
                    var poolId = grp.Key;
                    var actionKey = $"Release:{businessType}:{businessId}:{poolId}";
                    if (grp.Any(t => t.ActionKey == actionKey)) continue;

                    var reserved = grp.Where(t => t.TransactionType == BudgetTransactionType.Reserved).Sum(t => t.Amount);
                    var released = grp.Where(t => t.TransactionType == BudgetTransactionType.Released).Sum(t => t.Amount);
                    var consumed = grp.Where(t => t.TransactionType == BudgetTransactionType.Consumed).Sum(t => t.Amount);
                    var net = Math.Max(0m, reserved - released - consumed);
                    if (net <= 0) continue;

                    var mem = _memoryBudgets.SingleOrDefault(b => b.Id == poolId);
                    if (mem is not null)
                    {
                        mem.CommittedAmount = Math.Max(0m, mem.CommittedAmount - net);
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
                            Amount = net,
                            BalanceAfter = mem.AvailableAmount,
                            Description = reason ?? $"{businessType} 单据【{businessNumber}】释放退还预占额度",
                            OperatorId = operatorId,
                            ActionKey = actionKey
                        });
                    }
                }
            }

            return ServiceResult<bool>.Success(true);
        }
    }

    public ServiceResult<bool> Consume(
        string tenantId,
        Guid? budgetId,
        string businessType,
        Guid businessId,
        string businessNumber,
        decimal amount,
        string operatorId,
        string? description = null,
        string? actionKeySuffix = null)
    {
        if (amount <= 0) return ServiceResult<bool>.Success(true);

        if (db is not null)
        {
            using var dbTx = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
            try
            {
                var prevTxs = db.BudgetTransactions.AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId)
                    .ToList();

                var poolsToConsume = new List<(Guid PoolId, decimal ConsumeAmount)>();

                if (budgetId.HasValue)
                {
                    poolsToConsume.Add((budgetId.Value, amount));
                }
                else
                {
                    var pools = prevTxs.GroupBy(t => t.BudgetId).ToList();
                    var remaining = amount;
                    foreach (var grp in pools)
                    {
                        if (remaining <= 0) break;
                        var reserved = grp.Where(t => t.TransactionType == "RESERVED").Sum(t => t.Amount);
                        var released = grp.Where(t => t.TransactionType == "RELEASED").Sum(t => t.Amount);
                        var consumed = grp.Where(t => t.TransactionType == "CONSUMED").Sum(t => t.Amount);
                        var net = Math.Max(0m, reserved - released - consumed);
                        if (net <= 0) continue;

                        var poolConsume = Math.Min(remaining, net);
                        poolsToConsume.Add((grp.Key, poolConsume));
                        remaining -= poolConsume;
                    }
                    if (remaining > 0 && poolsToConsume.Count > 0)
                    {
                        var last = poolsToConsume[^1];
                        poolsToConsume[^1] = (last.PoolId, last.ConsumeAmount + remaining);
                    }
                    else if (poolsToConsume.Count == 0)
                    {
                        var prevRes = prevTxs.Where(t => t.TransactionType == "RESERVED").OrderByDescending(t => t.CreatedAt).FirstOrDefault();
                        if (prevRes is not null)
                            poolsToConsume.Add((prevRes.BudgetId, amount));
                    }
                }

                foreach (var (pId, pAmount) in poolsToConsume)
                {
                    var actionKey = $"Consume:{businessType}:{businessId}:{pId}:{(string.IsNullOrWhiteSpace(actionKeySuffix) ? (description ?? amount.ToString("F2")) : actionKeySuffix)}";
                    var existingConsume = prevTxs.FirstOrDefault(t => t.ActionKey == actionKey);
                    if (existingConsume is not null) continue;

                    db.Database.ExecuteSqlInterpolated($"SELECT \"Id\" FROM budget WHERE \"TenantId\" = {tenantId} AND \"Id\" = {pId} FOR UPDATE");
                    var record = db.Budgets.SingleOrDefault(b => b.TenantId == tenantId && b.Id == pId);
                    if (record is null) continue;
                    db.Entry(record).Reload();

                    record.CommittedAmount = Math.Max(0m, record.CommittedAmount - pAmount);
                    record.ActualAmount += pAmount;
                    record.ConcurrencyVersion++;
                    record.UpdatedAt = DateTimeOffset.UtcNow;

                    var newBalance = record.AllocatedAmount - record.CommittedAmount - record.ActualAmount;
                    db.BudgetTransactions.Add(new BudgetTransactionRecord
                    {
                        TenantId = tenantId,
                        BudgetId = record.Id,
                        BusinessType = businessType,
                        BusinessId = businessId,
                        BusinessNumber = businessNumber,
                        TransactionType = "CONSUMED",
                        Amount = pAmount,
                        BalanceAfter = newBalance,
                        Description = description ?? $"{businessType} 单据【{businessNumber}】付款结转实支",
                        OperatorId = operatorId,
                        ActionKey = actionKey,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    db.SaveChanges();
                }

                dbTx?.Commit();
                return ServiceResult<bool>.Success(true);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
            {
                dbTx?.Rollback();
                return ServiceResult<bool>.Success(true);
            }
        }

        lock (_memoryTransactions)
        {
            var prevTxs = _memoryTransactions
                .Where(t => t.TenantId == tenantId && t.BusinessType == businessType && t.BusinessId == businessId)
                .ToList();

            var poolsToConsume = new List<(Guid PoolId, decimal ConsumeAmount)>();
            if (budgetId.HasValue)
            {
                poolsToConsume.Add((budgetId.Value, amount));
            }
            else
            {
                var pools = prevTxs.GroupBy(t => t.BudgetId).ToList();
                var remaining = amount;
                foreach (var grp in pools)
                {
                    if (remaining <= 0) break;
                    var reserved = grp.Where(t => t.TransactionType == BudgetTransactionType.Reserved).Sum(t => t.Amount);
                    var released = grp.Where(t => t.TransactionType == BudgetTransactionType.Released).Sum(t => t.Amount);
                    var consumed = grp.Where(t => t.TransactionType == BudgetTransactionType.Consumed).Sum(t => t.Amount);
                    var net = Math.Max(0m, reserved - released - consumed);
                    if (net <= 0) continue;

                    var poolConsume = Math.Min(remaining, net);
                    poolsToConsume.Add((grp.Key, poolConsume));
                    remaining -= poolConsume;
                }
                if (remaining > 0 && poolsToConsume.Count > 0)
                {
                    var last = poolsToConsume[^1];
                    poolsToConsume[^1] = (last.PoolId, last.ConsumeAmount + remaining);
                }
                else if (poolsToConsume.Count == 0)
                {
                    var prevRes = prevTxs.Where(t => t.TransactionType == BudgetTransactionType.Reserved).OrderByDescending(t => t.CreatedAt).FirstOrDefault();
                    if (prevRes is not null)
                        poolsToConsume.Add((prevRes.BudgetId, amount));
                }
            }

            foreach (var (pId, pAmount) in poolsToConsume)
            {
                var actionKey = $"Consume:{businessType}:{businessId}:{pId}:{(string.IsNullOrWhiteSpace(actionKeySuffix) ? (description ?? amount.ToString("F2")) : actionKeySuffix)}";
                if (prevTxs.Any(t => t.ActionKey == actionKey)) continue;

                var mem = _memoryBudgets.SingleOrDefault(b => b.Id == pId);
                if (mem is null) continue;

                mem.CommittedAmount = Math.Max(0m, mem.CommittedAmount - pAmount);
                mem.ActualAmount += pAmount;
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
                    Amount = pAmount,
                    BalanceAfter = mem.AvailableAmount,
                    Description = description ?? $"{businessType} 单据【{businessNumber}】付款结转实支",
                    OperatorId = operatorId,
                    ActionKey = actionKey
                });
            }

            return ServiceResult<bool>.Success(true);
        }
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
                    ActionKey = t.ActionKey,
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

    private BudgetRecord? FindMatchingBudgetRecord(string tenantId, string departmentId, string? expenseCategory, int year, int month, string? projectId = null)
    {
        if (db is null) return null;
        var normDept = NormalizeDepartment(departmentId);
        var normCat = string.IsNullOrWhiteSpace(expenseCategory) ? null : expenseCategory.Trim();
        var normProj = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();
        var tMonth = targetMonthSafe(month);

        var candidates = db.Budgets
            .Where(b => b.TenantId == tenantId &&
                        b.Status == "ACTIVE" &&
                        b.Year == year &&
                        (b.DepartmentId == normDept.Id || b.DepartmentId == normDept.Name))
            .ToList();

        BudgetRecord? best = null;
        int bestScore = int.MaxValue;

        foreach (var c in candidates)
        {
            int monthScore;
            if (tMonth > 0)
            {
                if (c.Month == tMonth) monthScore = 1;
                else if (c.Month == 0) monthScore = 2;
                else continue;
            }
            else
            {
                if (c.Month == 0) monthScore = 1;
                else continue;
            }

            int catScore;
            if (normCat is not null)
            {
                if (string.Equals(c.ExpenseCategory, normCat, StringComparison.OrdinalIgnoreCase)) catScore = 1;
                else if (c.ExpenseCategory is null) catScore = 2;
                else continue;
            }
            else
            {
                if (c.ExpenseCategory is null) catScore = 1;
                else continue;
            }

            int projScore;
            if (normProj is not null)
            {
                if (string.Equals(c.ProjectId, normProj, StringComparison.OrdinalIgnoreCase)) projScore = 1;
                else if (c.ProjectId is null) projScore = 2;
                else continue;
            }
            else
            {
                if (c.ProjectId is null) projScore = 1;
                else continue;
            }

            var score = (monthScore * 100) + (catScore * 10) + projScore;
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best;
    }

    private Budget? FindMatchingBudget(string tenantId, string departmentId, string? expenseCategory, int year, int month, string? projectId = null)
    {
        if (db is not null)
        {
            var record = FindMatchingBudgetRecord(tenantId, departmentId, expenseCategory, year, month, projectId);
            return record is null ? null : MapToDomain(record);
        }

        var normDept = NormalizeDepartment(departmentId);
        var normCat = string.IsNullOrWhiteSpace(expenseCategory) ? null : expenseCategory.Trim();
        var normProj = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();
        var tMonth = targetMonthSafe(month);

        var candidates = _memoryBudgets
            .Where(b => b.TenantId == tenantId &&
                        b.Status == BudgetStatus.Active &&
                        b.Year == year &&
                        (b.DepartmentId == normDept.Id || b.DepartmentId == normDept.Name))
            .ToList();

        Budget? best = null;
        int bestScore = int.MaxValue;

        foreach (var c in candidates)
        {
            int monthScore;
            if (tMonth > 0)
            {
                if (c.Month == tMonth) monthScore = 1;
                else if (c.Month == 0) monthScore = 2;
                else continue;
            }
            else
            {
                if (c.Month == 0) monthScore = 1;
                else continue;
            }

            int catScore;
            if (normCat is not null)
            {
                if (string.Equals(c.ExpenseCategory, normCat, StringComparison.OrdinalIgnoreCase)) catScore = 1;
                else if (c.ExpenseCategory is null) catScore = 2;
                else continue;
            }
            else
            {
                if (c.ExpenseCategory is null) catScore = 1;
                else continue;
            }

            int projScore;
            if (normProj is not null)
            {
                if (string.Equals(c.ProjectId, normProj, StringComparison.OrdinalIgnoreCase)) projScore = 1;
                else if (c.ProjectId is null) projScore = 2;
                else continue;
            }
            else
            {
                if (c.ProjectId is null) projScore = 1;
                else continue;
            }

            var score = (monthScore * 100) + (catScore * 10) + projScore;
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best;
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

    public string ExportBudgetsCsv(Employee actor, int? year = null, string? departmentId = null, int? month = null, string? expenseCategory = null)
    {
        if (!CanViewAllBudgets(actor))
            throw new UnauthorizedAccessException("无预算执行数据导出权限。");

        if (db is null) return string.Empty;

        var q = db.Budgets.AsNoTracking().Where(b => b.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(departmentId)) q = q.Where(b => b.DepartmentId == departmentId);
        if (year.HasValue && year.Value > 0) q = q.Where(b => b.Year == year.Value);
        if (month.HasValue && month.Value > 0) q = q.Where(b => b.Month == month.Value);
        if (!string.IsNullOrWhiteSpace(expenseCategory)) q = q.Where(b => b.ExpenseCategory == expenseCategory);

        var list = q.OrderByDescending(b => b.Year).ThenByDescending(b => b.Month).ThenBy(b => b.DepartmentId).Take(5000).ToList();

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("部门,费用科目,所属年度,所属月份,项目,编制预算,已预占额,已消耗额,可用余额,执行率(%),状态");

        var rowCount = 0;
        foreach (var b in list)
        {
            var avail = b.AllocatedAmount - b.CommittedAmount - b.ActualAmount;
            var execRate = b.AllocatedAmount > 0 ? (b.ActualAmount / b.AllocatedAmount * 100m) : 0m;
            sb.AppendLine(string.Join(",",
                EscapeCsv(b.DepartmentId),
                EscapeCsv(b.ExpenseCategory ?? "全部科目"),
                b.Year.ToString(),
                b.Month == 0 ? "全年" : $"{b.Month}月",
                EscapeCsv(b.ProjectId ?? "全局"),
                b.AllocatedAmount.ToString("F2", CultureInfo.InvariantCulture),
                b.CommittedAmount.ToString("F2", CultureInfo.InvariantCulture),
                b.ActualAmount.ToString("F2", CultureInfo.InvariantCulture),
                avail.ToString("F2", CultureInfo.InvariantCulture),
                execRate.ToString("F1", CultureInfo.InvariantCulture),
                EscapeCsv(b.Status)));
            rowCount++;
        }

        db.AuditLogs.Add(new AuditRecord
        {
            TenantId = TenantId,
            ActorId = actor.Id,
            Action = "FINANCE_EXPORT",
            ResourceType = "Budget",
            ResourceId = "ALL",
            Summary = $"导出预算执行对账明细，筛选条件: [dept={departmentId}, year={year}, month={month}]，共计 {rowCount} 行"
        });
        db.SaveChanges();

        return sb.ToString();
    }

    public static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        var val = field;
        if (val.StartsWith('=') || val.StartsWith('+') || val.StartsWith('-') || val.StartsWith('@') || val.StartsWith('\t') || val.StartsWith('\r'))
        {
            val = "'" + val;
        }
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
            return $"\"{val.Replace("\"", "\"\"")}\"";
        return $"\"{val}\"";
    }
}
