using Erp.Application.Common;

namespace Erp.Application.Identity;

public sealed record LoginCommand(string Account, string Password, bool RememberMe);

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

public sealed record CurrentUserDto(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Account,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<AuthorizedStoreDto> Stores);

public sealed record AuthorizedStoreDto(Guid Id, string Code, string Name, bool IsDefault);

public interface IIdentityService
{
    Task<Result<CurrentUserDto>> LoginAsync(LoginCommand command, CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    Task<CurrentUserDto?> GetCurrentAsync(CancellationToken cancellationToken);

    Task<Result<CurrentUserDto>> ChangePasswordAsync(ChangePasswordCommand command, CancellationToken cancellationToken);
}
