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
                current_value = platform_code_sequences.current_value + 1,
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
