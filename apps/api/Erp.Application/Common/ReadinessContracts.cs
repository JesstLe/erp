namespace Erp.Application.Common;

public sealed record DatabaseReadinessDto(bool IsReady, string RequiredSchemaVersion);

public interface IDatabaseReadinessService
{
    Task<DatabaseReadinessDto> CheckAsync(CancellationToken cancellationToken);
}
