using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Customers;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Erp.Infrastructure.Customers;

internal sealed class CustomerService(ErpDbContext db, CustomerPrivacyService privacy, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor) : ICustomerService
{
    public async Task<IReadOnlyList<CustomerSummaryDto>> SearchAsync(Guid tenantId, Guid storeId, string? query,
        CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsNoTracking().Where(x => x.TenantId == tenantId && x.HomeStoreId == storeId);
        var term = query?.Trim();
        if (term?.Length > 100) return [];
        if (!string.IsNullOrEmpty(term))
        {
            var digits = new string(term.Where(char.IsDigit).ToArray());
            if (digits.Length == 11)
            {
                try
                {
                    var hash = privacy.Hash(digits);
                    customers = customers.Where(x => x.MobileLookupHash == hash);
                }
                catch (ArgumentException) { return []; }
            }
            else if (digits.Length == 4 && term.All(char.IsDigit))
            {
                customers = customers.Where(x => x.MobileLastFour == digits);
            }
            else
            {
                var upper = term.ToUpperInvariant();
                customers = customers.Where(x => x.Name.Contains(term) ||
                    db.MemberCards.Any(card => card.CustomerId == x.Id && card.CardNo == upper));
            }
        }

        var rows = await customers.OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new { Customer = x, ActiveCards = db.MemberCards.Count(card => card.CustomerId == x.Id && card.Status == MemberCardStatus.Active) })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new CustomerSummaryDto(x.Customer.Id, x.Customer.Name,
            privacy.MaskProtectedMobile(x.Customer.MobileCiphertext), x.Customer.Status.ToString(), x.Customer.HomeStoreId,
            x.ActiveCards, x.Customer.CreatedAtUtc)).ToList();
    }

    public async Task<Result<CustomerDetailDto>> GetAsync(Guid tenantId, Guid storeId, Guid customerId,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId &&
            x.TenantId == tenantId && x.HomeStoreId == storeId, cancellationToken);
        return customer is null
            ? ResultFactory.Failure<CustomerDetailDto>("CUSTOMER_NOT_FOUND", "顾客不存在")
            : ResultFactory.Success(await ToDetailAsync(customer, cancellationToken));
    }

    public async Task<Result<CustomerDetailDto>> CreateAsync(Guid tenantId, CreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "缺少幂等请求号");
        ProtectedMobile mobile;
        CustomerGender gender;
        try
        {
            mobile = privacy.Protect(command.Mobile);
            if (string.IsNullOrWhiteSpace(command.Gender)) gender = CustomerGender.Unknown;
            else if (!Enum.TryParse<CustomerGender>(command.Gender, true, out gender) || !Enum.IsDefined(gender))
                throw new ArgumentException("性别值无效");
        }
        catch (ArgumentException exception) { return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", exception.Message); }

        var requestHash = RequestHash($"CUSTOMER_CREATE|{command.StoreId}|{command.Name}|{Convert.ToHexString(mobile.LookupHash)}|{gender}|{command.BirthDate}|{command.SourceCode}|{command.ServiceNotificationConsent}|{command.MarketingConsent}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync<CustomerDetailDto>(tenantId, command.CommandId, requestHash,
            id => GetAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;

        try
        {
            if (await db.Customers.AnyAsync(x => x.TenantId == tenantId && x.MobileLookupHash == mobile.LookupHash &&
                x.Status != CustomerStatus.Merged, cancellationToken))
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("DUPLICATE_CUSTOMER", "该手机号已存在顾客档案，请先查询并核对");
            }

            var now = clock.GetUtcNow();
            var timeZoneId = await db.Stores.Where(x => x.Id == command.StoreId && x.TenantId == tenantId)
                .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
            if (timeZoneId is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "门店时区配置无效");
            }
            var customer = new Customer(tenantId, command.StoreId, command.Name, mobile.Ciphertext, mobile.LookupHash,
                mobile.LastFour, gender, command.BirthDate, command.SourceCode, command.ServiceNotificationConsent,
                command.MarketingConsent, StoreDate(now, timeZoneId));
            db.Customers.Add(customer);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, customer.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.create", "Customer", customer.Id,
                null, customer.Status.ToString(), command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await ToDetailAsync(customer, cancellationToken));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("DUPLICATE_CUSTOMER", "顾客档案已被其他终端创建，请重新查询");
        }
    }

    public async Task<IReadOnlyList<MemberCardTypeDto>> ListCardTypesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.MemberCardTypes.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == MemberCardTypeStatus.Published)
            .OrderBy(x => x.Name).Select(x => new MemberCardTypeDto(x.Id, x.Code, x.Name, x.ValidityDays, x.Status.ToString()))
            .ToListAsync(cancellationToken);

    public async Task<Result<MemberCardTypeDto>> CreateCardTypeAsync(Guid tenantId, CreateMemberCardTypeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<MemberCardTypeDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var requestHash = RequestHash($"CARD_TYPE_CREATE|{command.Code}|{command.Name}|{command.ValidityDays}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync<MemberCardTypeDto>(tenantId, command.CommandId, requestHash, async id =>
        {
            var item = await db.MemberCardTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
            return item is null ? ResultFactory.Failure<MemberCardTypeDto>("MEMBER_CARD_TYPE_NOT_FOUND", "卡类不存在")
                : ResultFactory.Success(new MemberCardTypeDto(item.Id, item.Code, item.Name, item.ValidityDays, item.Status.ToString()));
        }, cancellationToken);
        if (replay is not null) return replay;

        try
        {
            var now = clock.GetUtcNow();
            var cardType = new MemberCardType(tenantId, command.Code, command.Name, command.ValidityDays);
            db.MemberCardTypes.Add(cardType);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, cardType.Id, now);
            AddAudit(tenantId, null, command.OperatorId, "membership.card_type.create", "MemberCardType", cardType.Id,
                null, cardType.Status.ToString(), command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new MemberCardTypeDto(cardType.Id, cardType.Code, cardType.Name, cardType.ValidityDays, cardType.Status.ToString()));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberCardTypeDto>("VALIDATION_FAILED", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberCardTypeDto>("DUPLICATE_MEMBER_CARD_TYPE", "卡类编号已经存在");
        }
    }

    public async Task<Result<CustomerDetailDto>> OpenMembershipAsync(Guid tenantId, OpenMembershipCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var normalizedCardNo = string.IsNullOrWhiteSpace(command.CardNo) ? CreateCardNo(command.CommandId) : command.CardNo.Trim().ToUpperInvariant();
        var requestHash = RequestHash($"MEMBERSHIP_OPEN|{command.StoreId}|{command.CustomerId}|{command.CardTypeId}|{normalizedCardNo}|{command.Note}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync<CustomerDetailDto>(tenantId, command.CommandId, requestHash,
            _ => GetAsync(tenantId, command.StoreId, command.CustomerId, cancellationToken), cancellationToken);
        if (replay is not null) return replay;

        try
        {
            var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == command.CustomerId && x.TenantId == tenantId &&
                x.HomeStoreId == command.StoreId && x.Status == CustomerStatus.Active, cancellationToken);
            if (customer is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("CUSTOMER_NOT_FOUND", "顾客不存在或当前不可开卡");
            }
            var cardType = await db.MemberCardTypes.SingleOrDefaultAsync(x => x.Id == command.CardTypeId && x.TenantId == tenantId &&
                x.Status == MemberCardTypeStatus.Published, cancellationToken);
            if (cardType is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("MEMBER_CARD_TYPE_NOT_FOUND", "卡类不存在或未发布");
            }

            var timeZoneId = await db.Stores.Where(x => x.Id == command.StoreId && x.TenantId == tenantId)
                .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
            if (timeZoneId is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "门店时区配置无效");
            }

            var now = clock.GetUtcNow();
            var validFrom = StoreDate(now, timeZoneId);
            DateOnly? validTo = cardType.ValidityDays is null ? null : validFrom.AddDays(cardType.ValidityDays.Value);
            var card = new MemberCard(tenantId, customer.Id, cardType.Id, command.StoreId, normalizedCardNo,
                validFrom, validTo, command.Note);
            db.MemberCards.Add(card);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, card.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "membership.card.open", "MemberCard", card.Id,
                null, card.Status.ToString(), command.CommandId, now);
            // These aggregates carry identifiers rather than EF navigation properties. Persist the card first inside
            // the same transaction so PostgreSQL can enforce the account foreign key deterministically.
            await db.SaveChangesAsync(cancellationToken);
            db.MemberAccounts.AddRange(Enum.GetValues<MemberAccountType>().Select(type =>
                new MemberAccount(tenantId, customer.Id, card.Id, type)));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(tenantId, command.StoreId, customer.Id, cancellationToken);
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("DUPLICATE_MEMBER_CARD", "会员卡号已经存在");
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VERSION_CONFLICT", "会员状态已变化，请刷新后重试");
        }
    }

    private async Task<CustomerDetailDto> ToDetailAsync(Customer customer, CancellationToken cancellationToken)
    {
        var cards = await db.MemberCards.AsNoTracking().Where(x => x.CustomerId == customer.Id)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var cardTypeNames = await db.MemberCardTypes.AsNoTracking().Where(x => x.TenantId == customer.TenantId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var cardIds = cards.Select(x => x.Id).ToList();
        var accounts = await db.MemberAccounts.AsNoTracking().Where(x => cardIds.Contains(x.CardId))
            .OrderBy(x => x.AccountType).ToListAsync(cancellationToken);
        var cardDtos = cards.Select(card => new MemberCardDto(card.Id, cardTypeNames.GetValueOrDefault(card.CardTypeId, "未知卡类"),
            CustomerPrivacyService.MaskCardNo(card.CardNo), card.Status.ToString(), card.ValidFrom, card.ValidTo,
            accounts.Where(x => x.CardId == card.Id).OrderBy(x => AccountOrder(x.AccountType)).Select(x => new MemberAccountDto(x.Id, x.AccountType.ToString(),
                x.BalanceUnits, x.Status.ToString())).ToList())).ToList();
        return new CustomerDetailDto(customer.Id, customer.Name,
            privacy.MaskProtectedMobile(customer.MobileCiphertext), customer.Gender.ToString(), customer.SourceCode,
            customer.ServiceNotificationConsent, customer.MarketingConsent, customer.Status.ToString(),
            customer.HomeStoreId, customer.Version, cardDtos);
    }

    private async Task<Result<T>?> ReplayAsync<T>(Guid tenantId, Guid commandId, byte[] requestHash,
        Func<Guid, Task<Result<T>>> load, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<T>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        return receipt is null ? ResultFactory.Failure<T>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新") : await load(receipt.EntityId);
    }

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] requestHash, Guid entityId, DateTimeOffset now) =>
        db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId, RequestHash = requestHash,
            ResponseStatus = 200, ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)),
            CreatedAtUtc = now, CompletedAtUtc = now,
        });

    private void AddAudit(Guid tenantId, Guid? storeId, Guid operatorId, string action, string entityType, Guid entityId,
        string? previous, string? current, Guid commandId, DateTimeOffset now) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action, EntityType = entityType,
            EntityId = entityId, PreviousState = previous, CurrentState = current, RequestId = commandId,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now,
        });

    private static byte[] RequestHash(string identity) => SHA256.HashData(Encoding.UTF8.GetBytes(identity));
    private static async Task RollbackIfActiveAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch (InvalidOperationException) { /* PostgreSQL can complete a failed serializable transaction at commit. */ }
    }
    private static DateOnly StoreDate(DateTimeOffset now, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);
    private static int AccountOrder(MemberAccountType type) => type switch
    {
        MemberAccountType.Principal => 0,
        MemberAccountType.Bonus => 1,
        MemberAccountType.Points => 2,
        _ => 99,
    };
    private static bool IsUniqueViolation(Exception exception) => FindPostgres(exception)?.SqlState == PostgresErrorCodes.UniqueViolation;
    private static bool IsDatabaseConcurrencyConflict(Exception exception) => FindPostgres(exception)?.SqlState is
        PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
    private static string CreateCardNo(Guid commandId) => $"M{commandId:N}"[..24].ToUpperInvariant();
    private sealed record CommandReceipt(Guid EntityId);
}
