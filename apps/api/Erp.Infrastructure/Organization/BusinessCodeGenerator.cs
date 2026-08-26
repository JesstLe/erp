using System.Data;
using System.Globalization;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Erp.Infrastructure.Organization;

public sealed class BusinessCodeGenerator(ErpDbContext db, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo BusinessTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    public async Task<string> NextBrandCodeAsync(CancellationToken cancellationToken)
    {
        var localDate = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), BusinessTimeZone);
        var datePart = localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var value = await NextValueAsync("BRAND", datePart, 1, cancellationToken);
        if (value > 9_999)
            throw new DomainRuleException("CODE_SEQUENCE_EXHAUSTED", "当日品牌编码数量已达到上限");
        return $"B{datePart}{value:0000}";
    }

    public async Task<string> NextStoreCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var initialValue = await NextStoreInitialValueAsync(tenantId, cancellationToken);
        var value = await NextValueAsync("STORE", tenantId.ToString("N"), initialValue, cancellationToken);
        if (value > 999)
            throw new DomainRuleException("CODE_SEQUENCE_EXHAUSTED", "当前品牌的门店编码数量已达到上限");
        return $"S{value:000}";
    }

    public Task<string> NextServiceItemCodeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextTenantCodeAsync("SERVICE_ITEM", tenantId, "SV", 6,
            db.ServiceItems.Where(x => x.TenantId == tenantId).Select(x => x.Code), "服务项目", cancellationToken);

    public Task<string> NextProductItemCodeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextTenantCodeAsync("PRODUCT_ITEM", tenantId, "PD", 6,
            db.ProductItems.Where(x => x.TenantId == tenantId).Select(x => x.Code), "产品", cancellationToken);

    public Task<string> NextEmployeeCodeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextTenantCodeAsync("EMPLOYEE", tenantId, "EMP", 6,
            db.Employees.Where(x => x.TenantId == tenantId).Select(x => x.EmployeeNo), "员工", cancellationToken);

    public Task<string> NextEmployeePositionCodeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextTenantCodeAsync("EMPLOYEE_POSITION", tenantId, "POS", 6,
            db.EmployeePositions.Where(x => x.TenantId == tenantId).Select(x => x.Code), "员工岗位", cancellationToken);

    public Task<string> NextServiceRecordCategoryCodeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextTenantCodeAsync("SERVICE_RECORD_CATEGORY", tenantId, "CARE", 6,
            db.ServiceRecordCategories.Where(x => x.TenantId == tenantId).Select(x => x.Code),
            "服务记录分类", cancellationToken);

    public Task<string> NextSupplierCodeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextTenantCodeAsync("SUPPLIER", tenantId, "SUP", 6,
            db.Suppliers.Where(x => x.TenantId == tenantId).Select(x => x.Code), "供应商", cancellationToken);

    public Task<string> NextMemberCardTypeCodeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        NextTenantCodeAsync("MEMBER_CARD_TYPE", tenantId, "CT", 6,
            db.MemberCardTypes.Where(x => x.TenantId == tenantId).Select(x => x.Code), "会员卡类", cancellationToken);

    public async Task<string> NextFacilityCodeAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var codes = db.Facilities.Where(x => x.StoreId == storeId).Select(x => x.Code);
        var initialValue = await NextInitialValueAsync(codes, "F", cancellationToken);
        var value = await NextValueAsync("FACILITY", storeId.ToString("N"), initialValue, cancellationToken);
        return FormatCode("F", value, 4, "服务位");
    }

    private async Task<string> NextTenantCodeAsync(string sequenceName, Guid tenantId, string prefix, int digits,
        IQueryable<string> codes, string displayName, CancellationToken cancellationToken)
    {
        var initialValue = await NextInitialValueAsync(codes, prefix, cancellationToken);
        var value = await NextValueAsync(sequenceName, tenantId.ToString("N"), initialValue, cancellationToken);
        return FormatCode(prefix, value, digits, displayName);
    }

    private static async Task<long> NextInitialValueAsync(IQueryable<string> codes, string prefix,
        CancellationToken cancellationToken)
    {
        var existing = await codes.Where(code => code.StartsWith(prefix)).ToListAsync(cancellationToken);
        var maximum = 0L;
        foreach (var code in existing)
        {
            if (code.Length <= prefix.Length ||
                !long.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var value))
                continue;
            maximum = Math.Max(maximum, value);
        }
        return maximum + 1;
    }

    private static string FormatCode(string prefix, long value, int digits, string displayName)
    {
        var maximum = (long)Math.Pow(10, digits) - 1;
        if (value > maximum)
            throw new DomainRuleException("CODE_SEQUENCE_EXHAUSTED", $"{displayName}编码数量已达到上限");
        return $"{prefix}{value.ToString(new string('0', digits), CultureInfo.InvariantCulture)}";
    }

    private async Task<long> NextStoreInitialValueAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var currentTransaction = db.Database.CurrentTransaction ??
            throw new InvalidOperationException("业务编码必须在数据库事务中生成");
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction)currentTransaction.GetDbTransaction();
        command.CommandText = """
            SELECT COALESCE(MAX(substring(code FROM '^S([0-9]+)$')::bigint), 0) + 1
            FROM organization_stores
            WHERE tenant_id = @tenant_id AND code ~ '^S[0-9]+$';
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<long> NextValueAsync(string sequenceName, string scopeKey, long initialValue,
        CancellationToken cancellationToken)
    {
        var currentTransaction = db.Database.CurrentTransaction ??
            throw new InvalidOperationException("业务编码必须在数据库事务中生成");
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction)currentTransaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO platform_code_sequences
                (sequence_name, scope_key, current_value, updated_at_utc)
            VALUES (@sequence_name, @scope_key, @initial_value, @updated_at_utc)
            ON CONFLICT (sequence_name, scope_key)
            DO UPDATE SET
                current_value = GREATEST(platform_code_sequences.current_value + 1, EXCLUDED.current_value),
                updated_at_utc = EXCLUDED.updated_at_utc
            RETURNING current_value;
            """;
        command.Parameters.AddWithValue("sequence_name", NpgsqlDbType.Varchar, sequenceName);
        command.Parameters.AddWithValue("scope_key", NpgsqlDbType.Varchar, scopeKey);
        command.Parameters.AddWithValue("initial_value", NpgsqlDbType.Bigint, initialValue);
        command.Parameters.AddWithValue("updated_at_utc", NpgsqlDbType.TimestampTz, timeProvider.GetUtcNow());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }
}
