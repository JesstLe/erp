using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record CustomerSummaryDto(Guid Id, string DisplayName, string MaskedMobile, string Status,
    Guid HomeStoreId, string HomeStoreName, int ActiveCardCount, DateTimeOffset CreatedAtUtc);
public sealed record CashierCustomerSummaryDto(Guid Id, string DisplayName, string Mobile, string Status,
    Guid HomeStoreId, string HomeStoreName, int ActiveCardCount, DateOnly? BirthDate, string? Residence,
    long PrincipalBalanceMinor, long BonusBalanceMinor, DateTimeOffset CreatedAtUtc);

public sealed record MemberCardTypeDto(Guid Id, string Code, string Name, int? ValidityDays, string Status);
public sealed record MemberAccountDto(Guid Id, string AccountType, long BalanceUnits, string Status);
public sealed record MemberCardDto(Guid Id, string CardTypeName, string MaskedCardNo, string Status,
    DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<MemberAccountDto> Accounts);
public sealed record MergedCustomerAliasDto(Guid Id, string DisplayName, string MaskedMobile,
    DateTimeOffset? MergedAtUtc);
public sealed record CustomerDetailDto(Guid Id, string DisplayName, string MaskedMobile, string Gender,
    DateOnly? BirthDate, string? Residence, string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent,
    string Status, Guid HomeStoreId, string HomeStoreName, uint Version, IReadOnlyList<MemberCardDto> Cards,
    IReadOnlyList<MergedCustomerAliasDto> MergedAliases);
public sealed record CustomerMobileRevealDto(Guid CustomerId, string Mobile, DateTimeOffset RevealedAtUtc);
public sealed record CustomerExportDto(byte[] Content, string FileName, int RowCount, bool IncludesFullMobile);

public sealed record CreateCustomerCommand(Guid StoreId, string Name, string Mobile, string? Gender,
    DateOnly? BirthDate, string? Residence, string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent,
    Guid CommandId, Guid OperatorId);
public sealed record UpdateCustomerCommand(Guid StoreId, Guid CustomerId, string Name, string Mobile,
    string? Gender, DateOnly? BirthDate, string? Residence, string? SourceCode, bool ServiceNotificationConsent,
    bool MarketingConsent, uint ExpectedVersion, Guid CommandId, Guid OperatorId);
public sealed record ChangeCustomerStatusCommand(Guid StoreId, Guid CustomerId, bool Restore, string Reason,
    uint ExpectedVersion, Guid CommandId, Guid OperatorId);
public sealed record CustomerMergePreviewDto(Guid SourceCustomerId, string SourceDisplayName,
    string SourceMaskedMobile, uint SourceVersion, Guid TargetCustomerId, string TargetDisplayName,
    string TargetMaskedMobile, uint TargetVersion,
    int SourceCardCount, long SourcePrincipalBalanceMinor, long SourceBonusBalanceMinor,
    long SourcePointsBalance, int SourceOrderCount, int SourceServiceRecordCount,
    IReadOnlyList<string> Blockers, bool CanMerge);
public sealed record PreviewCustomerMergeCommand(Guid StoreId, Guid SourceCustomerId, Guid TargetCustomerId);
public sealed record MergeCustomerCommand(Guid StoreId, Guid SourceCustomerId, Guid TargetCustomerId,
    uint ExpectedSourceVersion, uint ExpectedTargetVersion, string Reason, Guid CommandId, Guid OperatorId);

public sealed record CreateMemberCardTypeCommand(string Name, int? ValidityDays,
    Guid CommandId, Guid OperatorId);

public sealed record OpenMembershipCommand(Guid StoreId, Guid CustomerId, Guid CardTypeId,
    string? CardNo, string? Note, Guid CommandId, Guid OperatorId);
public sealed record RevealCustomerMobileCommand(Guid StoreId, Guid CustomerId, string Purpose,
    Guid CommandId, Guid OperatorId);
public sealed record ExportCustomersCommand(Guid StoreId, string? Query, bool IncludeFullMobile,
    bool CanExportFullMobile, bool CanExportAllStores, string Purpose, Guid CommandId, Guid OperatorId);

public interface ICustomerService
{
    Task<PageResult<CustomerSummaryDto>> SearchAsync(Guid tenantId, Guid storeId, string? query, int page,
        int pageSize, CancellationToken cancellationToken);
    Task<PageResult<CashierCustomerSummaryDto>> SearchForCashierAsync(Guid tenantId, Guid storeId, string? query,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> GetAsync(Guid tenantId, Guid storeId, Guid customerId,
        bool includeFinancialDetails, CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> CreateAsync(Guid tenantId, CreateCustomerCommand command, CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> UpdateAsync(Guid tenantId, UpdateCustomerCommand command,
        CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> ChangeStatusAsync(Guid tenantId, ChangeCustomerStatusCommand command,
        CancellationToken cancellationToken);
    Task<Result<CustomerMergePreviewDto>> PreviewMergeAsync(Guid tenantId, PreviewCustomerMergeCommand command,
        CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> MergeAsync(Guid tenantId, MergeCustomerCommand command,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MemberCardTypeDto>> ListCardTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<MemberCardTypeDto>> CreateCardTypeAsync(Guid tenantId, CreateMemberCardTypeCommand command, CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> OpenMembershipAsync(Guid tenantId, OpenMembershipCommand command, CancellationToken cancellationToken);
    Task<Result<CustomerMobileRevealDto>> RevealMobileAsync(Guid tenantId, RevealCustomerMobileCommand command,
        CancellationToken cancellationToken);
    Task<Result<CustomerExportDto>> ExportAsync(Guid tenantId, ExportCustomersCommand command,
        CancellationToken cancellationToken);
}
