using System.Data;
using Erp.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

internal sealed class DatabaseReadinessService(ErpDbContext db) : IDatabaseReadinessService
{
    public const string RequiredSchemaVersion = "202608180020";

    public async Task<DatabaseReadinessDto> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return new DatabaseReadinessDto(false, RequiredSchemaVersion);
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT to_regclass('public.price_override_policies') IS NOT NULL
                   AND to_regclass('public.price_override_approvals') IS NOT NULL
                   AND to_regclass('public.customer_service_records') IS NOT NULL
                   AND to_regclass('public.inventory_movements') IS NOT NULL
                """;
            command.CommandType = CommandType.Text;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return new DatabaseReadinessDto(result is true, RequiredSchemaVersion);
        }
        catch
        {
            return new DatabaseReadinessDto(false, RequiredSchemaVersion);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
