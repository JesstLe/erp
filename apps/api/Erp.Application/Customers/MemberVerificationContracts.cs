using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record MemberVerificationChallengeDto(Guid Id, Guid OrderId, Guid CustomerId,
    long AuthorizedAmountMinor, string MaskedMobile, string Status, int AttemptsRemaining,
    DateTimeOffset ExpiresAtUtc, string? DevelopmentCode);

public sealed record IssueMemberVerificationCommand(Guid StoreId, Guid OrderId, long MemberAmountMinor,
    string FullMobile, Guid OperatorId);

public sealed record VerifyMemberChallengeCommand(Guid StoreId, Guid ChallengeId, string Code,
    Guid OperatorId);

public interface IMemberVerificationService
{
    Task<Result<MemberVerificationChallengeDto>> IssueAsync(Guid tenantId,
        IssueMemberVerificationCommand command, CancellationToken cancellationToken);
    Task<Result<MemberVerificationChallengeDto>> VerifyAsync(Guid tenantId,
        VerifyMemberChallengeCommand command, CancellationToken cancellationToken);
}
