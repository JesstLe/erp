using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record CustomerSummaryDto(Guid Id, string DisplayName, string MaskedMobile, string Status,
    Guid HomeStoreId, int ActiveCardCount, DateTimeOffset CreatedAtUtc);

public sealed record MemberCardTypeDto(Guid Id, string Code, string Name, int? ValidityDays, string Status);
public sealed record MemberAccountDto(Guid Id, string AccountType, long BalanceUnits, string Status);
public sealed record MemberCardDto(Guid Id, string CardTypeName, string MaskedCardNo, string Status,
    DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<MemberAccountDto> Accounts);
public sealed record CustomerDetailDto(Guid Id, string DisplayName, string MaskedMobile, string Gender,
    string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent,
    string Status, Guid HomeStoreId, uint Version, IReadOnlyList<MemberCardDto> Cards);

public sealed record CreateCustomerCommand(Guid StoreId, string Name, string Mobile, string? Gender,
    DateOnly? BirthDate, string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent,
    Guid CommandId, Guid OperatorId);

public sealed record CreateMemberCardTypeCommand(string Code, string Name, int? ValidityDays,
    Guid CommandId, Guid OperatorId);

public sealed record OpenMembershipCommand(Guid StoreId, Guid CustomerId, Guid CardTypeId,
    string? CardNo, string? Note, Guid CommandId, Guid OperatorId);

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerSummaryDto>> SearchAsync(Guid tenantId, Guid storeId, string? query, CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> GetAsync(Guid tenantId, Guid storeId, Guid customerId, CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> CreateAsync(Guid tenantId, CreateCustomerCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemberCardTypeDto>> ListCardTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<MemberCardTypeDto>> CreateCardTypeAsync(Guid tenantId, CreateMemberCardTypeCommand command, CancellationToken cancellationToken);
    Task<Result<CustomerDetailDto>> OpenMembershipAsync(Guid tenantId, OpenMembershipCommand command, CancellationToken cancellationToken);
}
