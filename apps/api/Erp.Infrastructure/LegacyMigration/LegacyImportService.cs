using System.Data;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Application.Common;
using Erp.Application.LegacyMigration;
using Erp.Domain.Catalog;
using Erp.Domain.Customers;
using Erp.Domain.Organization;
using Erp.Infrastructure.Customers;
using Erp.Infrastructure.Files;
using Erp.Infrastructure.Organization;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Erp.Infrastructure.LegacyMigration;

internal sealed partial class LegacyImportService(
    ErpDbContext db,
    CustomerPrivacyService customerPrivacy,
    SecureFileStorage fileStorage,
    BusinessCodeGenerator codeGenerator,
    IDataProtectionProvider dataProtectionProvider) : ILegacyImportService
{
    private readonly IDataProtector snapshotProtector =
        dataProtectionProvider.CreateProtector("Erp.LegacyMigration.Snapshot.v1");

    public async Task<LegacyImportResult> ImportAsync(
        LegacyImportCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        var dataset = command.Dataset;
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Code == dataset.TenantCode, cancellationToken)
            ?? throw new InvalidOperationException("目标品牌不存在");
        var operatorId = await db.Users.AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && x.IsEnabled)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (operatorId == Guid.Empty) throw new InvalidOperationException("目标品牌没有可用操作账号");

        var created = new Dictionary<string, int>(StringComparer.Ordinal);
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        var exceptions = new Dictionary<string, int>(StringComparer.Ordinal);
        var storedFiles = new List<StoredFileRecord>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var existingRun = await FindCompletedRunAsync(
                tenant.Id, dataset.SourceSystem, dataset.SourceFingerprintSha256, command.DryRun, cancellationToken);
            if (existingRun.HasValue)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new LegacyImportResult(existingRun.Value, command.DryRun, true, created, skipped, exceptions);
            }

            var runId = Guid.CreateVersion7();
            await InsertRunAsync(runId, tenant.Id, dataset, command.DryRun, cancellationToken);
            var maps = await LoadMapsAsync(tenant.Id, cancellationToken);
            var rows = dataset.Rows.GroupBy(x => x.Entity, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderBy(row => row.SourceId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

            var storeRows = Rows(rows, "stores");
            var storesBySource = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var storesByLegacyCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var storesByLegacyName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in storeRows)
            {
                if (TryMapped(maps, row, out var mappedStoreId))
                {
                    storesBySource[row.SourceId] = mappedStoreId;
                    var legacyCode = Field(row, "shop_code");
                    if (!string.IsNullOrWhiteSpace(legacyCode)) storesByLegacyCode[legacyCode] = mappedStoreId;
                    var legacyName = CleanText(Field(row, "shop_name"), 100);
                    if (legacyName is not null) AddStoreAlias(storesByLegacyName, legacyName, mappedStoreId);
                    Increment(skipped, "stores");
                    continue;
                }

                var name = CleanText(Field(row, "shop_name"), 100);
                if (name is null)
                {
                    await AddExceptionAsync(runId, tenant.Id, row, "shop_name", "INVALID_STORE", "Error",
                        "门店名称为空或无效，已跳过", cancellationToken);
                    Increment(exceptions, "stores");
                    continue;
                }

                var code = await codeGenerator.NextStoreCodeAsync(tenant.Id, cancellationToken);
                var store = new Store(tenant.Id, code, name);
                if (LooksDisabled(Field(row, "shop_stop"))) store.Disable();
                db.Stores.Add(store);
                storesBySource[row.SourceId] = store.Id;
                var sourceCode = Field(row, "shop_code");
                if (!string.IsNullOrWhiteSpace(sourceCode)) storesByLegacyCode[sourceCode] = store.Id;
                AddStoreAlias(storesByLegacyName, name, store.Id);
                await AddMapAsync(runId, tenant.Id, row, "organization_stores", store.Id, maps, cancellationToken);
                Increment(created, "stores");
            }

            // Customer.HomeStoreId is enforced by PostgreSQL but intentionally has no EF navigation. Persist
            // migrated stores first so later customer inserts cannot be ordered ahead of their store FK.
            await db.SaveChangesAsync(cancellationToken);

            var defaultStoreId = storesBySource.Values.FirstOrDefault();
            if (defaultStoreId == Guid.Empty)
                defaultStoreId = await db.Stores.Where(x => x.TenantId == tenant.Id).OrderBy(x => x.Code)
                    .Select(x => x.Id).FirstAsync(cancellationToken);

            var unitNames = Rows(rows, "units").ToDictionary(
                row => row.SourceId,
                row => CleanText(Field(row, "unit_name"), 20) ?? "件",
                StringComparer.OrdinalIgnoreCase);
            var tradeRows = Rows(rows, "employee-trades");
            var tradeCodes = tradeRows.ToDictionary(row => row.SourceId,
                row => CleanCode(Field(row, "ework_code"), 40) ?? $"LEGACY-{row.SourceId}",
                StringComparer.OrdinalIgnoreCase);
            var existingPositionCodes = await db.EmployeePositions.Where(x => x.TenantId == tenant.Id)
                .Select(x => x.Code).ToListAsync(cancellationToken);
            foreach (var row in tradeRows)
            {
                var code = tradeCodes[row.SourceId];
                if (existingPositionCodes.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
                var name = CleanText(Field(row, "ework_name"), 60) ?? code;
                if (name.Length < 2) name = $"岗位{name}";
                db.EmployeePositions.Add(new EmployeePosition(tenant.Id, code, name, 100));
                existingPositionCodes.Add(code);
            }

            foreach (var row in Rows(rows, "employees"))
            {
                if (TryMapped(maps, row, out _)) { Increment(skipped, "employees"); continue; }
                var name = CleanText(Field(row, "emplee_name"), 100);
                if (name is null || name.Length < 2)
                {
                    await AddExceptionAsync(runId, tenant.Id, row, "emplee_name", "INVALID_EMPLOYEE", "Error",
                        "员工姓名少于2个字符，已跳过", cancellationToken);
                    Increment(exceptions, "employees");
                    continue;
                }
                var positionSource = Field(row, "emplee_ework") ?? string.Empty;
                var position = tradeCodes.GetValueOrDefault(positionSource) ??
                    CleanCode(positionSource, 40) ?? "LEGACY-STAFF";
                if (position.Length < 2) position = $"P{position}";
                if (!existingPositionCodes.Contains(position, StringComparer.OrdinalIgnoreCase))
                {
                    db.EmployeePositions.Add(new EmployeePosition(tenant.Id, position, position, 100));
                    existingPositionCodes.Add(position);
                }
                var employee = new Employee(tenant.Id, $"LEGACY-{row.SourceId}", name, position, null);
                if (LooksDisabled(Field(row, "emplee_end"))) employee.Deactivate();
                db.Employees.Add(employee);
                var storeId = ResolveStore(row, "emplee_shop", storesBySource, storesByLegacyCode,
                    storesByLegacyName, defaultStoreId);
                db.EmployeeStores.Add(new EmployeeStore(tenant.Id, employee.Id, storeId, true));
                await AddMapAsync(runId, tenant.Id, row, "organization_employees", employee.Id, maps, cancellationToken);
                Increment(created, "employees");
            }

            foreach (var row in Rows(rows, "services"))
            {
                if (TryMapped(maps, row, out _)) { Increment(skipped, "services"); continue; }
                var name = CleanText(Field(row, "goods_name"), 120);
                if (name is null) { Increment(exceptions, "services"); continue; }
                var item = new ServiceItem(tenant.Id, $"LEGACY-SVC-{row.SourceId}", name, 0);
                if (LooksDisabled(Field(row, "goods_status"))) item.Disable();
                db.ServiceItems.Add(item);
                await AddMapAsync(runId, tenant.Id, row, "catalog_service_items", item.Id, maps, cancellationToken);
                Increment(created, "services");
            }

            foreach (var row in Rows(rows, "products"))
            {
                if (TryMapped(maps, row, out _)) { Increment(skipped, "products"); continue; }
                var name = CleanText(Field(row, "goods_name"), 120);
                if (name is null) { Increment(exceptions, "products"); continue; }
                var unitSource = Field(row, "goods_unit1") ?? string.Empty;
                var unit = unitNames.GetValueOrDefault(unitSource) ?? CleanText(unitSource, 20) ?? "件";
                var item = new ProductItem(tenant.Id, $"LEGACY-PRD-{row.SourceId}", name, unit, true);
                if (LooksDisabled(Field(row, "goods_status"))) item.Disable();
                db.ProductItems.Add(item);
                await AddMapAsync(runId, tenant.Id, row, "catalog_product_items", item.Id, maps, cancellationToken);
                Increment(created, "products");
            }

            var cardTypes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows(rows, "member-levels"))
            {
                if (TryMapped(maps, row, out var mappedCardTypeId))
                {
                    cardTypes[row.SourceId] = mappedCardTypeId;
                    Increment(skipped, "member-levels");
                    continue;
                }
                var name = CleanText(Field(row, "iclevel_name"), 80);
                if (name is null) { Increment(exceptions, "member-levels"); continue; }
                var cardType = new MemberCardType(tenant.Id, $"LEGACY-CARD-{row.SourceId}", name, null);
                db.MemberCardTypes.Add(cardType);
                cardTypes[row.SourceId] = cardType.Id;
                await AddMapAsync(runId, tenant.Id, row, "membership_card_types", cardType.Id, maps, cancellationToken);
                Increment(created, "member-levels");
            }

            var customerRows = Rows(rows, "customers");
            var financialSnapshots = new List<(Guid CustomerId, LegacySourceRow Row)>();
            foreach (var row in customerRows)
            {
                if (TryMapped(maps, row, out _)) { Increment(skipped, "customers"); continue; }
                var name = CleanText(Field(row, "member_name"), 100);
                var mobile = Field(row, "member_hand");
                if (name is null || string.IsNullOrWhiteSpace(mobile))
                {
                    await AddExceptionAsync(runId, tenant.Id, row, null, "INVALID_CUSTOMER", "Error",
                        "顾客姓名或手机号为空，已跳过", cancellationToken);
                    Increment(exceptions, "customers");
                    continue;
                }

                ProtectedMobile protectedMobile;
                try { protectedMobile = customerPrivacy.Protect(mobile); }
                catch (ArgumentException)
                {
                    await AddExceptionAsync(runId, tenant.Id, row, "member_hand", "INVALID_MOBILE", "Error",
                        "手机号格式无效，已跳过", cancellationToken);
                    Increment(exceptions, "customers");
                    continue;
                }

                var homeStoreId = ResolveStore(row, "member_shop", storesBySource, storesByLegacyCode,
                    storesByLegacyName, defaultStoreId);
                var birthDate = ParseBirthDate(Field(row, "member_birthday"));
                if (birthDate is null && !string.IsNullOrWhiteSpace(Field(row, "member_birthday")))
                {
                    await AddExceptionAsync(runId, tenant.Id, row, "member_birthday", "INVALID_BIRTH_DATE", "Warning",
                        "生日格式无法证明，目标字段保持为空", cancellationToken);
                    Increment(exceptions, "birth-date");
                }
                var customer = new Customer(tenant.Id, homeStoreId, name, protectedMobile.Ciphertext,
                    protectedMobile.LookupHash, protectedMobile.LastFour, ParseGender(Field(row, "member_sex")),
                    birthDate, CleanCode(Field(row, "member_source"), 40), false, false,
                    DateOnly.FromDateTime(DateTime.UtcNow));
                db.Customers.Add(customer);
                await AddMapAsync(runId, tenant.Id, row, "customers", customer.Id, maps, cancellationToken);
                financialSnapshots.Add((customer.Id, row));
                Increment(created, "customers");
            }

            // Snapshot rows have a real FK to customers, so flush all normalized master data first while
            // remaining inside the same serializable transaction. Dry runs still roll the entire unit back.
            await db.SaveChangesAsync(cancellationToken);
            foreach (var snapshot in financialSnapshots)
                await InsertFinancialSnapshotAsync(runId, tenant.Id, snapshot.CustomerId, snapshot.Row,
                    cancellationToken);

            var photosByCustomer = dataset.Photos.GroupBy(x => x.SourceCustomerId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderBy(photo => photo.Slot).ToArray(), StringComparer.Ordinal);
            foreach (var row in customerRows)
            {
                if (!maps.TryGetValue(("customers", row.SourceId), out var customerMap)) continue;
                var memo = CleanText(Field(row, "member_memo"), 4_000);
                photosByCustomer.TryGetValue(row.SourceId, out var photos);
                photos ??= [];
                if (memo is null && photos.Length == 0) continue;
                var noteSource = row with
                {
                    Entity = "customer-service-record",
                    SourceSha256 = CombinedHash(row.SourceSha256, photos.Select(x => x.PlainSha256))
                };
                if (TryMapped(maps, noteSource, out _)) { Increment(skipped, "service-records"); continue; }
                var storeId = ResolveStore(row, "member_shop", storesBySource, storesByLegacyCode,
                    storesByLegacyName, defaultStoreId);
                var record = new ServiceRecord(tenant.Id, storeId, customerMap.TargetId, null,
                    ParseOccurredAt(Field(row, "member_time2"), Field(row, "member_time1")),
                    "旧系统顾客档案备注（迁移）", memo, null,
                    DeterministicGuid($"{tenant.Id:N}:legacy-service-record:{row.SourceId}"), operatorId,
                    DateTimeOffset.UtcNow);
                foreach (var photo in photos)
                {
                    if (photo.Content.LongLength > SecureFileStorage.MaximumImageBytes)
                    {
                        await AddExceptionAsync(runId, tenant.Id, row, $"member_image{photo.Slot}",
                            "PHOTO_TOO_LARGE", "Warning", "历史照片超过5MB，未导入附件", cancellationToken);
                        Increment(exceptions, "photos");
                        continue;
                    }
                    await using var content = new MemoryStream(photo.Content, writable: false);
                    var stored = await fileStorage.StoreImageAsync(tenant.Id, storeId,
                        StoredFilePurposes.ServiceRecordImage, operatorId,
                        new FileUploadInput($"legacy-{row.SourceId}-slot-{photo.Slot}.jpg", photo.ContentType,
                            photo.Content.LongLength, content), cancellationToken);
                    storedFiles.Add(stored);
                    db.StoredFiles.Add(stored);
                    record.AttachImage(stored.Id);
                    Increment(created, "photos");
                }
                db.ServiceRecords.Add(record);
                await AddMapAsync(runId, tenant.Id, noteSource, "customer_service_records", record.Id, maps,
                    cancellationToken);
                Increment(created, "service-records");
            }

            var carePhotosByRecord = (dataset.CarePhotos ?? [])
                .GroupBy(x => x.SourceCareRecordId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderBy(photo => photo.Slot).ToArray(), StringComparer.Ordinal);
            foreach (var row in Rows(rows, "care-records"))
            {
                carePhotosByRecord.TryGetValue(row.SourceId, out var carePhotos);
                carePhotos ??= [];
                var noteSource = row with
                {
                    Entity = "care-record-service-record",
                    SourceSha256 = CombinedHash(row.SourceSha256, carePhotos.Select(x => x.PlainSha256))
                };
                if (TryMapped(maps, noteSource, out _))
                {
                    Increment(skipped, "care-records");
                    continue;
                }

                var sourceCustomerId = Field(row, "bill_member")?.Trim();
                if (string.IsNullOrWhiteSpace(sourceCustomerId) ||
                    !maps.TryGetValue(("customers", sourceCustomerId), out var customerMap))
                {
                    await AddExceptionAsync(runId, tenant.Id, row, "bill_member", "CUSTOMER_NOT_MAPPED", "Error",
                        "护理记录对应顾客未能安全映射，已跳过", cancellationToken);
                    Increment(exceptions, "care-records");
                    continue;
                }

                var storeId = ResolveStore(row, "bill_shop", storesBySource, storesByLegacyCode,
                    storesByLegacyName, defaultStoreId);
                var followUpParts = new[]
                {
                    CleanText(Field(row, "bill_next"), 100) is { } next ? $"下次护理：{next}" : null,
                    CleanText(Field(row, "bill_emplee"), 100) is { } employee ? $"护理人员：{employee}" : null,
                    CleanText(Field(row, "bill_memo"), 1_700)
                }.Where(value => value is not null);
                var followUp = CleanText(string.Join("；", followUpParts), 2_000);
                var record = new ServiceRecord(
                    tenant.Id,
                    storeId,
                    customerMap.TargetId,
                    null,
                    ParseOccurredAt(Field(row, "bill_time1"), Field(row, "bill_date")),
                    CleanText(Field(row, "bill_intro"), 2_000),
                    CleanText(Field(row, "bill_plan"), 4_000),
                    followUp,
                    DeterministicGuid($"{tenant.Id:N}:legacy-care-record:{row.SourceId}"),
                    operatorId,
                    DateTimeOffset.UtcNow);
                foreach (var photo in carePhotos)
                {
                    if (photo.Content.LongLength > SecureFileStorage.MaximumImageBytes)
                    {
                        await AddExceptionAsync(runId, tenant.Id, row, $"care_image{photo.Slot}",
                            "PHOTO_TOO_LARGE", "Warning", "历史护理照片超过5MB，未导入附件", cancellationToken);
                        Increment(exceptions, "care-photos");
                        continue;
                    }
                    await using var content = new MemoryStream(photo.Content, writable: false);
                    var stored = await fileStorage.StoreImageAsync(tenant.Id, storeId,
                        StoredFilePurposes.ServiceRecordImage, operatorId,
                        new FileUploadInput($"legacy-care-{row.SourceId}-slot-{photo.Slot}.jpg", photo.ContentType,
                            photo.Content.LongLength, content), cancellationToken);
                    storedFiles.Add(stored);
                    db.StoredFiles.Add(stored);
                    record.AttachImage(stored.Id);
                    Increment(created, "care-photos");
                }
                db.ServiceRecords.Add(record);
                await AddMapAsync(runId, tenant.Id, noteSource, "customer_service_records", record.Id, maps,
                    cancellationToken);
                Increment(created, "care-records");
                Increment(created, "service-records");
            }

            await db.SaveChangesAsync(cancellationToken);
            var result = new LegacyImportResult(runId, command.DryRun, false, created, skipped, exceptions);
            await CompleteRunAsync(runId, result, cancellationToken);
            if (command.DryRun)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                finally
                {
                    await CleanupFilesAsync(storedFiles);
                }
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException) when (rollbackException is InvalidOperationException or ObjectDisposedException)
            {
                // A failed SaveChanges can already leave the provider transaction aborted. Preserve the
                // original import error, but always continue to remove files written before that failure.
            }
            await CleanupFilesAsync(storedFiles);
            throw;
        }
    }

    private static void Validate(LegacyImportCommand command)
    {
        var dataset = command.Dataset;
        if (dataset.TenantCode is not "B01")
            throw new InvalidOperationException("本迁移切片只允许写入测试品牌 B01");
        if (dataset.SourceFingerprintSha256.Length != 64 || !dataset.SourceFingerprintSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("来源指纹无效");
        if (dataset.Rows.Count > 20_000 || dataset.Photos.Count > 20_000 ||
            (dataset.CarePhotos?.Count ?? 0) > 20_000)
            throw new InvalidOperationException("迁移数据超过安全上限");
        if (dataset.Rows.Any(row => row.SourceSha256.Length != 64 || !row.SourceSha256.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("来源记录摘要无效");
    }

    private static LegacySourceRow[] Rows(
        Dictionary<string, LegacySourceRow[]> rows, string entity) =>
        rows.TryGetValue(entity, out var value) ? value : [];

    private static string? Field(LegacySourceRow row, string field) =>
        row.Fields.TryGetValue(field, out var value) ? value : null;

    private static string? CleanText(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var decoded = WebUtility.HtmlDecode(TagPattern().Replace(value, string.Empty));
        var normalized = SpacePattern().Replace(decoded, " ").Trim();
        if (normalized.Length == 0) return null;
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private static string? CleanCode(string? value, int maximum)
    {
        var text = CleanText(value, maximum);
        if (text is null) return null;
        var normalized = new string(text.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return normalized.Length == 0 ? null : normalized[..Math.Min(normalized.Length, maximum)].ToUpperInvariant();
    }

    private static bool LooksDisabled(string? value) => value?.Trim() is "1" or "true" or "停用" or "禁用";

    private static CustomerGender ParseGender(string? value) => value?.Trim() switch
    {
        "男" or "1" => CustomerGender.Male,
        "女" or "2" => CustomerGender.Female,
        _ => CustomerGender.Unknown,
    };

    private static DateOnly? ParseBirthDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "yyyy-MM-dd", "yyyy/M/d", "yyyyMMdd", "MM-dd", "M-d" };
        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed)) continue;
            if (format is "MM-dd" or "M-d") parsed = new DateTime(1900, parsed.Month, parsed.Day);
            var result = DateOnly.FromDateTime(parsed);
            if (result <= DateOnly.FromDateTime(DateTime.UtcNow)) return result;
        }
        return null;
    }

    private static DateTimeOffset ParseOccurredAt(string? preferred, string? fallback)
    {
        foreach (var value in new[] { preferred, fallback })
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
                continue;
            var result = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(8))
                .ToUniversalTime();
            if (result <= DateTimeOffset.UtcNow.AddMinutes(5)) return result;
        }
        return DateTimeOffset.UtcNow;
    }

    private static Guid ResolveStore(LegacySourceRow row, string field,
        Dictionary<string, Guid> bySource, Dictionary<string, Guid> byCode,
        Dictionary<string, Guid> byName, Guid fallback)
    {
        var value = Field(row, field)?.Trim() ?? string.Empty;
        if (value.Length == 0) return fallback;
        if (bySource.TryGetValue(value, out var byId)) return byId;
        if (byCode.TryGetValue(value, out var byLegacyCode)) return byLegacyCode;
        if (byName.TryGetValue(value, out var byLegacyName)) return byLegacyName;
        throw new InvalidOperationException(
            $"旧系统{row.Entity}记录 {row.SourceId} 的门店值“{value}”无法映射；已拒绝回退到默认门店");
    }

    private static void AddStoreAlias(Dictionary<string, Guid> aliases, string alias, Guid storeId)
    {
        var normalized = alias.Trim();
        if (normalized.Length == 0) return;
        if (aliases.TryGetValue(normalized, out var existing) && existing != storeId)
            throw new InvalidOperationException($"旧系统门店名称“{normalized}”不唯一，无法安全迁移");
        aliases[normalized] = storeId;
    }

    private static string CombinedHash(string rowHash, IEnumerable<string> photoHashes)
    {
        var text = string.Join(':', new[] { rowHash }.Concat(photoHashes.Order(StringComparer.Ordinal)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var guidBytes = bytes[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0f) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return new Guid(guidBytes);
    }

    private static void Increment(Dictionary<string, int> values, string key) =>
        values[key] = values.TryGetValue(key, out var current) ? current + 1 : 1;

    private static bool TryMapped(
        IReadOnlyDictionary<(string Entity, string SourceId), LegacyMap> maps,
        LegacySourceRow row,
        out Guid targetId)
    {
        if (!maps.TryGetValue((row.Entity, row.SourceId), out var map))
        {
            targetId = Guid.Empty;
            return false;
        }
        if (!string.Equals(map.SourceSha256, row.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"来源记录已变化，拒绝覆盖：{row.Entity}/{row.SourceId}");
        targetId = map.TargetId;
        return true;
    }

    private async Task<Guid?> FindCompletedRunAsync(Guid tenantId, string sourceSystem, string fingerprint,
        bool dryRun, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            SELECT id FROM legacy_migration_runs
            WHERE tenant_id=@tenant_id AND source_system=@source_system
              AND source_fingerprint_sha256=@fingerprint AND is_dry_run=@is_dry_run AND status='Completed'
            LIMIT 1
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("source_system", NpgsqlDbType.Varchar, sourceSystem);
        command.Parameters.AddWithValue("fingerprint", NpgsqlDbType.Varchar, fingerprint);
        command.Parameters.AddWithValue("is_dry_run", NpgsqlDbType.Boolean, dryRun);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private async Task InsertRunAsync(Guid runId, Guid tenantId, LegacyImportDataset dataset, bool dryRun,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            INSERT INTO legacy_migration_runs
              (id,tenant_id,source_system,source_fingerprint_sha256,import_version,status,is_dry_run,started_at_utc,counts)
            VALUES (@id,@tenant_id,@source_system,@fingerprint,@import_version,'Running',@is_dry_run,now(),'{}'::jsonb)
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("source_system", NpgsqlDbType.Varchar, dataset.SourceSystem);
        command.Parameters.AddWithValue("fingerprint", NpgsqlDbType.Varchar, dataset.SourceFingerprintSha256);
        command.Parameters.AddWithValue("import_version", NpgsqlDbType.Varchar, dataset.ImportVersion);
        command.Parameters.AddWithValue("is_dry_run", NpgsqlDbType.Boolean, dryRun);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Dictionary<(string Entity, string SourceId), LegacyMap>> LoadMapsAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            SELECT source_entity,source_id,source_sha256,target_id
            FROM legacy_migration_record_maps WHERE tenant_id=@tenant_id
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<(string, string), LegacyMap>();
        while (await reader.ReadAsync(cancellationToken))
            result[(reader.GetString(0), reader.GetString(1))] =
                new LegacyMap(reader.GetString(2), reader.GetGuid(3));
        return result;
    }

    private async Task AddMapAsync(Guid runId, Guid tenantId, LegacySourceRow row, string targetTable,
        Guid targetId, IDictionary<(string Entity, string SourceId), LegacyMap> maps,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            INSERT INTO legacy_migration_record_maps
              (id,tenant_id,run_id,source_entity,source_id,source_sha256,target_table,target_id,created_at_utc)
            VALUES (@id,@tenant_id,@run_id,@source_entity,@source_id,@source_sha256,@target_table,@target_id,now())
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("source_entity", NpgsqlDbType.Varchar, row.Entity);
        command.Parameters.AddWithValue("source_id", NpgsqlDbType.Varchar, row.SourceId);
        command.Parameters.AddWithValue("source_sha256", NpgsqlDbType.Varchar, row.SourceSha256);
        command.Parameters.AddWithValue("target_table", NpgsqlDbType.Varchar, targetTable);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, targetId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        maps[(row.Entity, row.SourceId)] = new LegacyMap(row.SourceSha256, targetId);
    }

    private async Task AddExceptionAsync(Guid runId, Guid tenantId, LegacySourceRow row, string? field,
        string code, string severity, string detail, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            INSERT INTO legacy_migration_exceptions
              (id,tenant_id,run_id,source_entity,source_id,field_name,code,severity,detail,created_at_utc)
            VALUES (@id,@tenant_id,@run_id,@source_entity,@source_id,@field_name,@code,@severity,@detail,now())
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("source_entity", NpgsqlDbType.Varchar, row.Entity);
        command.Parameters.AddWithValue("source_id", NpgsqlDbType.Varchar, row.SourceId);
        command.Parameters.AddWithValue("field_name", NpgsqlDbType.Varchar, (object?)field ?? DBNull.Value);
        command.Parameters.AddWithValue("code", NpgsqlDbType.Varchar, code);
        command.Parameters.AddWithValue("severity", NpgsqlDbType.Varchar, severity);
        command.Parameters.AddWithValue("detail", NpgsqlDbType.Varchar, detail);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertFinancialSnapshotAsync(Guid runId, Guid tenantId, Guid customerId,
        LegacySourceRow row, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            INSERT INTO legacy_customer_financial_snapshots
              (id,tenant_id,run_id,customer_id,source_customer_id,source_card_reference_ciphertext,
               source_member_money_minor,source_member_bonus_minor,source_member_sbonus_minor,
               source_member_store_minor,source_member_credit_minor,source_member_arrear_minor,
               source_member_score,is_spendable,captured_at_utc)
            VALUES (@id,@tenant_id,@run_id,@customer_id,@source_customer_id,@card,
               @money,@bonus,@sbonus,@store,@credit,@arrear,@score,false,now())
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Uuid, customerId);
        command.Parameters.AddWithValue("source_customer_id", NpgsqlDbType.Varchar, row.SourceId);
        var card = CleanText(Field(row, "member_code"), 200);
        command.Parameters.AddWithValue("card", NpgsqlDbType.Text,
            card is null ? DBNull.Value : snapshotProtector.Protect(card));
        AddNullable(command, "money", NpgsqlDbType.Bigint, ParseMinor(Field(row, "member_money")));
        AddNullable(command, "bonus", NpgsqlDbType.Bigint, ParseMinor(Field(row, "member_bonus")));
        AddNullable(command, "sbonus", NpgsqlDbType.Bigint, ParseMinor(Field(row, "member_sbonus")));
        AddNullable(command, "store", NpgsqlDbType.Bigint, ParseMinor(Field(row, "member_store")));
        AddNullable(command, "credit", NpgsqlDbType.Bigint, ParseMinor(Field(row, "member_credit")));
        AddNullable(command, "arrear", NpgsqlDbType.Bigint, ParseMinor(Field(row, "member_arrear")));
        AddNullable(command, "score", NpgsqlDbType.Numeric, ParseDecimal(Field(row, "member_score")));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CompleteRunAsync(Guid runId, LegacyImportResult result, CancellationToken cancellationToken)
    {
        var counts = JsonSerializer.Serialize(new { result.Created, result.Skipped, result.Exceptions });
        await using var command = CreateCommand("""
            UPDATE legacy_migration_runs SET status='Completed',completed_at_utc=now(),counts=@counts::jsonb WHERE id=@id
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("counts", NpgsqlDbType.Text, counts);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value?.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture,
            out var parsed) ? parsed : null;

    private static long? ParseMinor(string? value)
    {
        var amount = ParseDecimal(value);
        if (!amount.HasValue) return null;
        try { return checked((long)decimal.Round(amount.Value * 100m, 0, MidpointRounding.AwayFromZero)); }
        catch (OverflowException) { return null; }
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.AddWithValue(name, type, value ?? DBNull.Value);

    private async Task CleanupFilesAsync(IEnumerable<StoredFileRecord> files)
    {
        foreach (var file in files) await fileStorage.TryDeleteUncommittedAsync(file);
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TagPattern();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SpacePattern();

    private sealed record LegacyMap(string SourceSha256, Guid TargetId);
}
