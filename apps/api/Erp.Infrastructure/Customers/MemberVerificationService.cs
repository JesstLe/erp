using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Erp.Application.Common;
using Erp.Application.Customers;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Erp.Infrastructure.Customers;

internal sealed class MemberVerificationCodeService
{
    private readonly byte[] key;

    public MemberVerificationCodeService(IConfiguration configuration, IHostEnvironment environment)
    {
        IsDevelopment = environment.IsDevelopment();
        var configured = configuration["MemberVerification:CodePepper"];
        if (!IsDevelopment && (string.IsNullOrWhiteSpace(configured) || configured.Length < 32 ||
            configured.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("生产环境必须配置至少32字符的 MemberVerification:CodePepper");
        key = SHA256.HashData(Encoding.UTF8.GetBytes(configured ??
            "erp-development-only-member-verification-pepper-v1"));
    }

    public bool IsDevelopment { get; }
    public static string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000)
        .ToString("D6", CultureInfo.InvariantCulture);
    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(16);

    public byte[] Hash(byte[] salt, string code)
    {
        var normalized = code.Trim();
        if (normalized.Length != 6 || !normalized.All(char.IsDigit))
            throw new ArgumentException("请输入6位数字验证码");
        var codeBytes = Encoding.UTF8.GetBytes(normalized);
        var payload = new byte[salt.Length + codeBytes.Length];
        Buffer.BlockCopy(salt, 0, payload, 0, salt.Length);
        Buffer.BlockCopy(codeBytes, 0, payload, salt.Length, codeBytes.Length);
        return HMACSHA256.HashData(key, payload);
    }
}

internal sealed class MemberVerificationService(ErpDbContext db, CustomerPrivacyService privacy,
    MemberVerificationCodeService codes, TimeProvider clock, IHttpContextAccessor httpContextAccessor)
    : IMemberVerificationService
{
    public async Task<Result<MemberVerificationChallengeDto>> IssueAsync(Guid tenantId,
        IssueMemberVerificationCommand command, CancellationToken cancellationToken)
    {
        if (!codes.IsDevelopment)
            return ResultFactory.Failure<MemberVerificationChallengeDto>("VERIFICATION_DELIVERY_UNAVAILABLE",
                "生产验证码发送渠道尚未配置");
        if (command.MemberAmountMinor < 50_000)
            return ResultFactory.Failure<MemberVerificationChallengeDto>("VALIDATION_FAILED",
                "低于500元的会员扣款只需核对完整手机号，无需发送验证码");

        byte[] mobileHash;
        try { mobileHash = privacy.Hash(command.FullMobile); }
        catch (ArgumentException exception)
        {
            return ResultFactory.Failure<MemberVerificationChallengeDto>("VALIDATION_FAILED", exception.Message);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var order = await db.ServiceOrders.SingleOrDefaultAsync(x => x.Id == command.OrderId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId &&
                x.Status == ServiceOrderStatus.PendingPayment, cancellationToken);
            if (order?.CustomerId is null)
                return await FailureAndRollback(transaction, "MEMBER_CUSTOMER_REQUIRED",
                    "消费单必须关联有效会员后才能使用会员账户", cancellationToken);
            if (command.MemberAmountMinor > order.ReceivableMinor)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED",
                    "验证码金额不能超过消费单应收金额", cancellationToken);
            var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == order.CustomerId &&
                x.TenantId == tenantId && x.Status == CustomerStatus.Active,
                cancellationToken);
            if (customer is null || !CryptographicOperations.FixedTimeEquals(customer.MobileLookupHash, mobileHash))
                return await FailureAndRollback(transaction, "MEMBER_MOBILE_MISMATCH",
                    "完整手机号与当前会员不一致", cancellationToken);

            var now = clock.GetUtcNow();
            var recentCount = await db.MemberVerificationChallenges.CountAsync(x => x.TenantId == tenantId &&
                x.CustomerId == customer.Id && x.CreatedAtUtc >= now.AddHours(-1), cancellationToken);
            if (recentCount >= 5)
                return await FailureAndRollback(transaction, "MEMBER_VERIFICATION_RATE_LIMITED",
                    "该会员验证码请求过于频繁，请稍后再试", cancellationToken);

            var previous = await db.MemberVerificationChallenges.Where(x => x.TenantId == tenantId &&
                x.OrderId == order.Id && (x.Status == MemberVerificationStatus.Active ||
                    x.Status == MemberVerificationStatus.Verified)).ToListAsync(cancellationToken);
            foreach (var item in previous) item.Supersede();

            var code = MemberVerificationCodeService.GenerateCode();
            var salt = MemberVerificationCodeService.GenerateSalt();
            var challenge = new MemberVerificationChallenge(tenantId, command.StoreId, customer.Id,
                order.Id, command.MemberAmountMinor, salt, codes.Hash(salt, code), customer.MobileLastFour,
                command.OperatorId, now.AddMinutes(5));
            db.MemberVerificationChallenges.Add(challenge);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "membership.verification.issue",
                challenge.Id, null, challenge.Status.ToString(), now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(challenge, privacy.MaskProtectedMobile(customer.MobileCiphertext), code));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<MemberVerificationChallengeDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<MemberVerificationChallengeDto>> VerifyAsync(Guid tenantId,
        VerifyMemberChallengeCommand command, CancellationToken cancellationToken)
    {
        var challenge = await db.MemberVerificationChallenges.SingleOrDefaultAsync(x =>
            x.Id == command.ChallengeId && x.TenantId == tenantId && x.StoreId == command.StoreId,
            cancellationToken);
        if (challenge is null)
            return ResultFactory.Failure<MemberVerificationChallengeDto>("MEMBER_VERIFICATION_NOT_FOUND",
                "会员验证码挑战不存在");
        byte[] candidateHash;
        try { candidateHash = codes.Hash(challenge.CodeSalt, command.Code); }
        catch (ArgumentException exception)
        {
            return ResultFactory.Failure<MemberVerificationChallengeDto>("VALIDATION_FAILED", exception.Message);
        }

        var now = clock.GetUtcNow();
        try
        {
            var matched = challenge.Verify(candidateHash, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId,
                matched ? "membership.verification.success" : "membership.verification.failure",
                challenge.Id, MemberVerificationStatus.Active.ToString(), challenge.Status.ToString(), now);
            await db.SaveChangesAsync(cancellationToken);
            var maskedMobile = matched
                ? await db.Customers.AsNoTracking().Where(x => x.Id == challenge.CustomerId && x.TenantId == tenantId)
                    .Select(x => x.MobileCiphertext).SingleAsync(cancellationToken)
                : null;
            return matched
                ? ResultFactory.Success(ToDto(challenge, privacy.MaskProtectedMobile(maskedMobile!), null))
                : ResultFactory.Failure<MemberVerificationChallengeDto>("MEMBER_VERIFICATION_CODE_INVALID",
                    $"验证码错误，还可尝试 {challenge.AttemptsRemaining} 次");
        }
        catch (DomainRuleException exception)
        {
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Failure<MemberVerificationChallengeDto>(exception.Code, exception.Message);
        }
    }

    private static MemberVerificationChallengeDto ToDto(MemberVerificationChallenge challenge, string maskedMobile,
        string? developmentCode) => new(challenge.Id, challenge.OrderId, challenge.CustomerId,
        challenge.AuthorizedAmountMinor, maskedMobile, challenge.Status.ToString(),
        challenge.AttemptsRemaining, challenge.ExpiresAtUtc, developmentCode);

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, Guid entityId,
        string? previous, string? current, DateTimeOffset now) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId,
            StoreId = storeId,
            OperatorId = operatorId,
            Action = action,
            EntityType = "MemberVerificationChallenge",
            EntityId = entityId,
            PreviousState = previous,
            CurrentState = current,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background",
            OccurredAtUtc = now,
        });

    private static async Task<Result<MemberVerificationChallengeDto>> FailureAndRollback(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code, string message,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return ResultFactory.Failure<MemberVerificationChallengeDto>(code, message);
    }
}
