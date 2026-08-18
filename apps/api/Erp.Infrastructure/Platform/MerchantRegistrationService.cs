using Erp.Application.Common;
using Erp.Application.Platform;
using Erp.Domain.Common;
using Erp.Domain.Platform;
using Erp.Infrastructure.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Platform;

internal sealed class MerchantRegistrationService(
    ErpDbContext db,
    CustomerPrivacyService customerPrivacy,
    PlatformRegistrationPrivacyService registrationPrivacy,
    TimeProvider timeProvider) : IMerchantRegistrationService
{
    public async Task<Result<MerchantRegistrationReceiptDto>> SubmitAsync(
        SubmitMerchantRegistrationCommand command, CancellationToken cancellationToken)
    {
        if (!command.AcceptedTerms)
            return ResultFactory.Failure<MerchantRegistrationReceiptDto>("TERMS_NOT_ACCEPTED",
                "请先阅读并同意服务及隐私条款");
        try
        {
            var mobile = customerPrivacy.Protect(command.ContactMobile);
            var email = registrationPrivacy.ProtectEmail(command.ContactEmail);
            var normalizedAccount = command.DesiredOwnerAccount.Trim().ToUpperInvariant();
            if (await db.MerchantRegistrationApplications.AsNoTracking().AnyAsync(application =>
                    application.Status == MerchantRegistrationStatus.PendingReview &&
                    (application.ContactMobileHash == mobile.LookupHash ||
                     application.NormalizedDesiredOwnerAccount == normalizedAccount), cancellationToken))
                return ResultFactory.Failure<MerchantRegistrationReceiptDto>("REGISTRATION_ALREADY_PENDING",
                    "该手机号或负责人账号已有待审核申请");

            var now = timeProvider.GetUtcNow();
            var application = new MerchantRegistrationApplication(CreateApplicationNo(now),
                command.MerchantName, command.StoreName, command.ContactName, mobile.Ciphertext,
                mobile.LookupHash, mobile.LastFour, email?.Ciphertext, email?.LookupHash,
                command.DesiredOwnerAccount, command.Note, NormalizeIp(command.SourceIp), now);
            db.MerchantRegistrationApplications.Add(application);
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(new MerchantRegistrationReceiptDto(application.Id,
                application.ApplicationNo, application.Status.ToString(), application.CreatedAtUtc));
        }
        catch (Exception exception) when (exception is DomainRuleException or ArgumentException)
        {
            return ResultFactory.Failure<MerchantRegistrationReceiptDto>("VALIDATION_FAILED", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ResultFactory.Failure<MerchantRegistrationReceiptDto>("REGISTRATION_ALREADY_PENDING",
                "该手机号或负责人账号已有待审核申请");
        }
    }

    private static string CreateApplicationNo(DateTimeOffset now) =>
        $"MR{now:yyyyMMdd}{Guid.NewGuid():N}"[..24].ToUpperInvariant();

    private static string NormalizeIp(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is > 0 and <= 64 ? normalized : "unknown";
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
