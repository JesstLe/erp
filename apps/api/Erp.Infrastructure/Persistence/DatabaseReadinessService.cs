using System.Data;
using Erp.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

internal sealed class DatabaseReadinessService(ErpDbContext db) : IDatabaseReadinessService
{
    public const string RequiredSchemaVersion = "202608190030";

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
                   AND to_regclass('public.customer_service_record_corrections') IS NOT NULL
                   AND to_regclass('public.inventory_movements') IS NOT NULL
                   AND to_regclass('public.appointments') IS NOT NULL
                   AND to_regclass('public.employee_shifts') IS NOT NULL
                   AND to_regclass('public.member_service_passes') IS NOT NULL
                   AND to_regclass('public.member_service_pass_ledgers') IS NOT NULL
                   AND to_regclass('public.member_point_grants') IS NOT NULL
                   AND to_regclass('public.member_point_use_allocations') IS NOT NULL
                   AND to_regclass('public.suppliers') IS NOT NULL
                   AND to_regclass('public.purchase_receipts') IS NOT NULL
                   AND to_regclass('public.inventory_lots') IS NOT NULL
                   AND to_regclass('public.inventory_lot_allocations') IS NOT NULL
                   AND to_regclass('public.stocktakes') IS NOT NULL
                   AND to_regclass('public.inventory_transfers') IS NOT NULL
                   AND to_regclass('public.platform_admin_users') IS NOT NULL
                   AND to_regclass('public.merchant_registration_applications') IS NOT NULL
                   AND to_regclass('public.login_security_events') IS NOT NULL
                   AND to_regclass('public.platform_audit_events') IS NOT NULL
                   AND to_regclass('public.ix_customers_name_trgm') IS NOT NULL
                   AND to_regclass('public.ix_organization_employees_name_trgm') IS NOT NULL
                   AND EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'payments'
                                 AND column_name = 'cash_tendered_minor')
                   AND EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'payments'
                                 AND column_name = 'cash_change_minor')
                   AND EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'member_topup_orders'
                                 AND column_name = 'refunded_principal_minor')
                   AND EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'member_topup_orders'
                                 AND column_name = 'revoked_bonus_minor')
                   AND EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm')
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
