using Erp.Application.Common;

namespace Erp.Application.Platform;

public sealed record SubmitMerchantRegistrationCommand(string MerchantName, string StoreName, string ContactName,
    string ContactMobile, string? ContactEmail, string DesiredOwnerAccount, string? Note,
    bool AcceptedTerms, string SourceIp);

public sealed record MerchantRegistrationReceiptDto(Guid Id, string ApplicationNo, string Status,
    DateTimeOffset CreatedAtUtc);

public sealed record PlatformLoginCommand(string Account, string Password, bool RememberMe);
public sealed record PlatformChangePasswordCommand(string CurrentPassword, string NewPassword);
public sealed record PlatformCurrentUserDto(Guid Id, string Account, string DisplayName, bool MustChangePassword);

public sealed record MerchantRegistrationApplicationDto(Guid Id, string ApplicationNo, string MerchantName,
    string StoreName, string ContactName, string MaskedMobile, string? MaskedEmail, string DesiredOwnerAccount,
    string? Note, string SourceIp, string Status, Guid? TenantId, string? ReviewReason,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ReviewedAtUtc, uint Version);

public sealed record MerchantRegistrationPageDto(IReadOnlyList<MerchantRegistrationApplicationDto> Items,
    int Total, int Page, int PageSize);

public sealed record ApproveMerchantRegistrationCommand(Guid ApplicationId, string InitialPassword,
    string Reason, uint ExpectedVersion);
public sealed record RejectMerchantRegistrationCommand(Guid ApplicationId, string Reason, uint ExpectedVersion);

public sealed record PlatformMerchantDto(Guid Id, string Code, string Name, string Status, int StoreCount,
    int EmployeeCount, int LoginAccountCount, DateTimeOffset CreatedAtUtc, uint Version);
public sealed record PlatformMerchantPageDto(IReadOnlyList<PlatformMerchantDto> Items, int Total, int Page,
    int PageSize);
public sealed record ChangeMerchantStatusCommand(Guid TenantId, bool Enable, string Reason, uint ExpectedVersion);

public sealed record LoginSecurityEventDto(Guid Id, string Scope, string EventType, string ResultCode,
    Guid? TenantId, string? TenantName, string Account, string IpAddress, string UserAgentSummary,
    string TraceId, DateTimeOffset OccurredAtUtc);
public sealed record LoginSecurityEventPageDto(IReadOnlyList<LoginSecurityEventDto> Items, int Total, int Page,
    int PageSize);

public interface IMerchantRegistrationService
{
    Task<Result<MerchantRegistrationReceiptDto>> SubmitAsync(SubmitMerchantRegistrationCommand command,
        CancellationToken cancellationToken);
}

public interface IPlatformIdentityService
{
    Task<Result<PlatformCurrentUserDto>> LoginAsync(PlatformLoginCommand command, CancellationToken cancellationToken);
    Task<PlatformCurrentUserDto?> GetCurrentAsync(CancellationToken cancellationToken);
    Task<Result<PlatformCurrentUserDto>> ChangePasswordAsync(PlatformChangePasswordCommand command,
        CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
}

public interface IPlatformAdminService
{
    Task<MerchantRegistrationPageDto> ListRegistrationsAsync(string? status, string? query, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<Result<MerchantRegistrationApplicationDto>> ApproveAsync(Guid platformUserId,
        ApproveMerchantRegistrationCommand command, CancellationToken cancellationToken);
    Task<Result<MerchantRegistrationApplicationDto>> RejectAsync(Guid platformUserId,
        RejectMerchantRegistrationCommand command, CancellationToken cancellationToken);
    Task<PlatformMerchantPageDto> ListMerchantsAsync(string? status, string? query, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<Result<PlatformMerchantDto>> ChangeMerchantStatusAsync(Guid platformUserId,
        ChangeMerchantStatusCommand command, CancellationToken cancellationToken);
    Task<LoginSecurityEventPageDto> ListSecurityEventsAsync(string? scope, string? resultCode, Guid? tenantId,
        string? account, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize,
        CancellationToken cancellationToken);
}
