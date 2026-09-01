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
            var rebaselineBalances = new Dictionary<Guid, LegacyStoredValue>();
            if (command.FinancialRebaseline)
            {
                var mappedCustomerIds = maps
                    .Where(item => item.Key.Entity == "customers")
                    .Select(item => item.Value.TargetId)
                    .Distinct()
                    .ToArray();
                if (mappedCustomerIds.Length != command.ExpectedMappedCustomers)
                    throw new InvalidOperationException("金额重建护栏失败：已映射顾客数量与预期不一致");
                var existingMappedCustomers = await db.Customers.AsNoTracking()
                    .CountAsync(customer => customer.TenantId == tenant.Id &&
                        mappedCustomerIds.Contains(customer.Id), cancellationToken);
                if (existingMappedCustomers != mappedCustomerIds.Length)
                    throw new InvalidOperationException("金额重建护栏失败：顾客映射存在失效目标");
                var currentAccounts = await db.MemberAccounts
                    .Where(account => account.TenantId == tenant.Id &&
                        mappedCustomerIds.Contains(account.CustomerId) &&
                        (account.AccountType == MemberAccountType.Principal ||
                         account.AccountType == MemberAccountType.Bonus))
                    .ToListAsync(cancellationToken);
                if (currentAccounts.GroupBy(account => (account.CustomerId, account.AccountType))
                    .Any(group => group.Count() != 1))
                    throw new InvalidOperationException("金额重建护栏失败：顾客存在多个同类型储值账户");
                foreach (var customerId in mappedCustomerIds)
                {
                    var principal = currentAccounts.Where(account => account.CustomerId == customerId &&
                            account.AccountType == MemberAccountType.Principal)
                        .Sum(account => account.BalanceUnits);
                    var bonus = currentAccounts.Where(account => account.CustomerId == customerId &&
                            account.AccountType == MemberAccountType.Bonus)
                        .Sum(account => account.BalanceUnits);
                    rebaselineBalances[customerId] = new LegacyStoredValue(principal, bonus,
                        principal > 0 || bonus > 0);
                }
                if (rebaselineBalances.Values.Sum(balance => balance.PrincipalMinor) !=
                        command.ExpectedCurrentPrincipalMinor ||
                    rebaselineBalances.Values.Sum(balance => balance.BonusMinor) !=
                        command.ExpectedCurrentBonusMinor)
                    throw new InvalidOperationException("金额重建护栏失败：当前本金或赠送金总额与预期不一致");
            }
            var rows = dataset.Rows.GroupBy(x => x.Entity, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderBy(row => row.SourceId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

            var storeRows = Rows(rows, "stores");
            var storeOverrides = dataset.StoreSourceToTargetCodes ?? new Dictionary<string, string>();
            var existingStoresByCode = await db.Stores.Where(x => x.TenantId == tenant.Id)
                .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var unknownOverrides = storeOverrides.Keys.Except(storeRows.Select(row => row.SourceId),
                StringComparer.OrdinalIgnoreCase).ToArray();
            if (unknownOverrides.Length > 0)
                throw new InvalidOperationException("门店映射包含来源数据中不存在的门店ID；已停止迁移");
            if (command.SyncMappedStores && storeOverrides.Count != storeRows.Length)
                throw new InvalidOperationException("同步映射门店时必须覆盖全部来源门店；已停止迁移");
            if (command.SyncMappedStores && existingStoresByCode.Keys
                    .Except(storeOverrides.Values, StringComparer.OrdinalIgnoreCase).Any())
                throw new InvalidOperationException("目标品牌存在迁移表之外的门店；已停止迁移");
            var storesBySource = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var storesByLegacyCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var storesByLegacyName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in storeRows)
            {
                if (maps.TryGetValue((row.Entity, row.SourceId), out var existingStoreMap))
                {
                    var mappedStoreId = existingStoreMap.TargetId;
                    if (!string.Equals(existingStoreMap.SourceSha256, row.SourceSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (!command.FinancialIncrementalSync && !command.FinancialRebaseline)
                            throw new InvalidOperationException($"来源记录已变化，拒绝覆盖：{row.Entity}/{row.SourceId}");
                        var mappedStore = await db.Stores.SingleAsync(x => x.TenantId == tenant.Id &&
                            x.Id == mappedStoreId, cancellationToken);
                        var latestName = CleanText(Field(row, "shop_name"), 100)
                            ?? throw new InvalidOperationException("增量门店名称无效；已停止同步");
                        var latestAddress = CleanText(Field(row, "shop_addr"), 300);
                        mappedStore.UpdateProfile(mappedStore.Code, latestName, mappedStore.TimeZoneId,
                            latestAddress ?? mappedStore.Address);
                        await AddRevisionAsync(runId, tenant.Id, row, existingStoreMap, maps,
                            cancellationToken);
                        Increment(created, "store-updates");
                    }
                    else
                    {
                        Increment(skipped, "stores");
                    }
                    storesBySource[row.SourceId] = mappedStoreId;
                    var legacyCode = Field(row, "shop_code");
                    if (!string.IsNullOrWhiteSpace(legacyCode)) storesByLegacyCode[legacyCode] = mappedStoreId;
                    var legacyName = CleanText(Field(row, "shop_name"), 100);
                    if (legacyName is not null) AddStoreAlias(storesByLegacyName, legacyName, mappedStoreId);
                    continue;
                }

                if (storeOverrides.TryGetValue(row.SourceId, out var targetCode))
                {
                    var legacyName = CleanText(Field(row, "shop_name"), 100);
                    var legacyAddress = CleanText(Field(row, "shop_addr"), 300);
                    if (!existingStoresByCode.TryGetValue(targetCode, out var targetStore))
                    {
                        if (!command.SyncMappedStores || legacyName is null)
                            throw new InvalidOperationException(
                                $"旧系统门店 {row.SourceId} 指定的目标门店编码不存在；已停止迁移");
                        targetStore = new Store(tenant.Id, targetCode, legacyName, address: legacyAddress);
                        if (LooksDisabled(Field(row, "shop_stop"))) targetStore.Disable();
                        db.Stores.Add(targetStore);
                        existingStoresByCode[targetCode] = targetStore;
                        Increment(created, "stores");
                    }
                    else if (command.SyncMappedStores)
                    {
                        if (legacyName is null)
                            throw new InvalidOperationException("映射门店名称无效；已停止迁移");
                        targetStore.UpdateProfile(targetStore.Code, legacyName, targetStore.TimeZoneId,
                            legacyAddress ?? targetStore.Address);
                    }
                    else if (targetStore.Address is null && legacyAddress is not null)
                        targetStore.UpdateProfile(targetStore.Code, targetStore.Name, targetStore.TimeZoneId,
                            legacyAddress);
                    storesBySource[row.SourceId] = targetStore.Id;
                    var overrideLegacyCode = Field(row, "shop_code");
                    if (!string.IsNullOrWhiteSpace(overrideLegacyCode))
                        storesByLegacyCode[overrideLegacyCode] = targetStore.Id;
                    var overrideLegacyName = CleanText(Field(row, "shop_name"), 100);
                    if (overrideLegacyName is not null)
                        AddStoreAlias(storesByLegacyName, overrideLegacyName, targetStore.Id);
                    await AddMapAsync(runId, tenant.Id, row, "organization_stores", targetStore.Id, maps,
                        cancellationToken);
                    Increment(created, "store-mappings");
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
                var store = new Store(tenant.Id, code, name, address: CleanText(Field(row, "shop_addr"), 300));
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
            Guid? fallbackStoredValueCardTypeId = null;
            if (customerRows.Any(row => StoredValue(row).HasEvidence))
            {
                fallbackStoredValueCardTypeId = await db.MemberCardTypes
                    .Where(x => x.TenantId == tenant.Id && x.Code == "LEGACY-STORED")
                    .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
                if (!fallbackStoredValueCardTypeId.HasValue)
                {
                    var fallbackType = new MemberCardType(tenant.Id, "LEGACY-STORED", "旧系统储值卡", null);
                    db.MemberCardTypes.Add(fallbackType);
                    fallbackStoredValueCardTypeId = fallbackType.Id;
                    Increment(created, "stored-value-card-types");
                }
            }

            var existingCustomersByMobile = new Dictionary<string, Customer>(StringComparer.Ordinal);
            if (command.ReconcileExistingCustomers)
            {
                var existingCustomers = await db.Customers
                    .Where(x => x.TenantId == tenant.Id && x.Status != CustomerStatus.Merged)
                    .ToListAsync(cancellationToken);
                foreach (var group in existingCustomers.GroupBy(
                             customer => Convert.ToHexString(customer.MobileLookupHash), StringComparer.Ordinal))
                {
                    if (group.Count() != 1)
                        throw new InvalidOperationException("目标品牌存在重复手机号顾客，无法安全合并旧系统顾客");
                    existingCustomersByMobile[group.Key] = group.Single();
                }
            }
            var consumedExistingCustomers = new HashSet<Guid>();
            var storedValuePlans = new List<LegacyStoredValuePlan>();
            var financialSnapshots = new List<(Guid CustomerId, LegacySourceRow Row)>();
            var financialRevisions = new List<LegacyFinancialRevisionPlan>();
            foreach (var row in customerRows)
            {
                maps.TryGetValue((row.Entity, row.SourceId), out var existingCustomerMap);
                if (existingCustomerMap is not null && string.Equals(existingCustomerMap.SourceSha256,
                        row.SourceSha256, StringComparison.OrdinalIgnoreCase) && !command.FinancialRebaseline)
                {
                    Increment(skipped, "customers");
                    continue;
                }
                if (existingCustomerMap is not null && !command.FinancialIncrementalSync &&
                    !command.FinancialRebaseline)
                    throw new InvalidOperationException($"来源记录已变化，拒绝覆盖：{row.Entity}/{row.SourceId}");
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
                Customer customer;
                var mobileKey = Convert.ToHexString(protectedMobile.LookupHash);
                Customer? existingCustomer = null;
                var incrementalCustomer = existingCustomerMap is not null;
                var reconcilesExistingCustomer = !incrementalCustomer && command.ReconcileExistingCustomers &&
                    existingCustomersByMobile.TryGetValue(mobileKey, out existingCustomer) &&
                    consumedExistingCustomers.Add(existingCustomer.Id);
                if (incrementalCustomer)
                {
                    customer = await db.Customers.SingleAsync(x => x.TenantId == tenant.Id &&
                        x.Id == existingCustomerMap!.TargetId, cancellationToken);
                    if (await db.Customers.AnyAsync(x => x.TenantId == tenant.Id && x.Id != customer.Id &&
                            x.Status != CustomerStatus.Merged && x.MobileLookupHash == protectedMobile.LookupHash,
                            cancellationToken))
                        throw new InvalidOperationException("增量顾客手机号与其他顾客冲突；已停止同步");
                    customer.ChangeHomeStore(homeStoreId);
                    customer.UpdateProfile(name, protectedMobile.Ciphertext, protectedMobile.LookupHash,
                        protectedMobile.LastFour, ParseGender(Field(row, "member_sex")), birthDate,
                        CleanCode(Field(row, "member_source"), 40), false, false,
                        DateOnly.FromDateTime(DateTime.UtcNow));
                    var previousStoredValue = command.FinancialRebaseline
                        ? rebaselineBalances.GetValueOrDefault(customer.Id,
                            new LegacyStoredValue(0, 0, false))
                        : await LoadPreviousStoredValueAsync(tenant.Id, row.SourceId, cancellationToken);
                    var currentStoredValue = StoredValue(row);
                    if (!string.Equals(existingCustomerMap!.SourceSha256, row.SourceSha256,
                            StringComparison.OrdinalIgnoreCase))
                        await AddRevisionAsync(runId, tenant.Id, row, existingCustomerMap, maps,
                            cancellationToken);
                    financialRevisions.Add(new LegacyFinancialRevisionPlan(customer.Id, row,
                        previousStoredValue.PrincipalMinor, previousStoredValue.BonusMinor,
                        currentStoredValue.PrincipalMinor, currentStoredValue.BonusMinor));
                    if (currentStoredValue.HasEvidence || previousStoredValue.PrincipalMinor > 0 ||
                        previousStoredValue.BonusMinor > 0)
                    {
                        var levelSource = Field(row, "member_iclevel")?.Trim() ?? string.Empty;
                        var cardTypeId = cardTypes.GetValueOrDefault(levelSource,
                            fallbackStoredValueCardTypeId ?? throw new InvalidOperationException(
                                "缺少旧系统储值卡类型"));
                        storedValuePlans.Add(new LegacyStoredValuePlan(row, customer.Id, homeStoreId, cardTypeId,
                            currentStoredValue.PrincipalMinor, currentStoredValue.BonusMinor, true,
                            previousStoredValue.PrincipalMinor, previousStoredValue.BonusMinor, true,
                            command.FinancialRebaseline));
                    }
                    Increment(created, "customer-updates");
                    continue;
                }
                if (reconcilesExistingCustomer)
                {
                    customer = existingCustomer!;
                    customer.ChangeHomeStore(homeStoreId);
                    customer.UpdateProfile(name, protectedMobile.Ciphertext, protectedMobile.LookupHash,
                        protectedMobile.LastFour, ParseGender(Field(row, "member_sex")), birthDate,
                        CleanCode(Field(row, "member_source"), 40), false, false,
                        DateOnly.FromDateTime(DateTime.UtcNow));
                    await AddMapAsync(runId, tenant.Id, row, "customers", customer.Id, maps, cancellationToken);
                    Increment(created, "customer-mappings");
                }
                else
                {
                    customer = new Customer(tenant.Id, homeStoreId, name, protectedMobile.Ciphertext,
                        protectedMobile.LookupHash, protectedMobile.LastFour, ParseGender(Field(row, "member_sex")),
                        birthDate, CleanCode(Field(row, "member_source"), 40), false, false,
                        DateOnly.FromDateTime(DateTime.UtcNow));
                    db.Customers.Add(customer);
                    await AddMapAsync(runId, tenant.Id, row, "customers", customer.Id, maps, cancellationToken);
                    Increment(created, "customers");
                }
                financialSnapshots.Add((customer.Id, row));

                var storedValue = StoredValue(row);
                if (storedValue.HasEvidence)
                {
                    var levelSource = Field(row, "member_iclevel")?.Trim() ?? string.Empty;
                    var cardTypeId = cardTypes.GetValueOrDefault(levelSource,
                        fallbackStoredValueCardTypeId ?? throw new InvalidOperationException("缺少旧系统储值卡类型"));
                    storedValuePlans.Add(new LegacyStoredValuePlan(row, customer.Id, homeStoreId, cardTypeId,
                        storedValue.PrincipalMinor, storedValue.BonusMinor, reconcilesExistingCustomer,
                        0, 0, false, false));
                }
            }

            // Snapshot rows have a real FK to customers, so flush all normalized master data first while
            // remaining inside the same serializable transaction. Dry runs still roll the entire unit back.
            await db.SaveChangesAsync(cancellationToken);

            var planCustomerIds = storedValuePlans.Select(plan => plan.CustomerId).Distinct().ToArray();
            var activeCardsByCustomer = planCustomerIds.Length == 0
                ? new Dictionary<Guid, MemberCard>()
                : (await db.MemberCards.Where(card => card.TenantId == tenant.Id &&
                        planCustomerIds.Contains(card.CustomerId) && card.Status == MemberCardStatus.Active)
                    .OrderBy(card => card.CreatedAtUtc).ToListAsync(cancellationToken))
                    .GroupBy(card => card.CustomerId).ToDictionary(group => group.Key, group => group.First());
            var cardPlans = new List<(LegacyStoredValuePlan Plan, MemberCard Card)>();
            foreach (var plan in storedValuePlans)
            {
                var cardSource = plan.Row with
                {
                    Entity = "customer-stored-value-card",
                    SourceSha256 = CombinedHash(plan.Row.SourceSha256, ["stored-value-v1"]),
                };
                MemberCard card;
                if (plan.ReconcilesExistingCustomer && activeCardsByCustomer.TryGetValue(plan.CustomerId, out var activeCard))
                {
                    card = activeCard;
                    Increment(skipped, "stored-value-cards");
                }
                else
                {
                    var occurredAt = ParseOccurredAt(Field(plan.Row, "member_time1"), null)
                        .ToOffset(TimeSpan.FromHours(8));
                    card = new MemberCard(tenant.Id, plan.CustomerId, plan.CardTypeId, plan.StoreId,
                        LegacyCardNo(plan.Row.SourceId), DateOnly.FromDateTime(occurredAt.DateTime), null,
                        "旧系统储值迁移");
                    db.MemberCards.Add(card);
                    Increment(created, "stored-value-cards");
                }
                if (maps.TryGetValue((cardSource.Entity, cardSource.SourceId), out var existingCardMap))
                {
                    if (!string.Equals(existingCardMap.SourceSha256, cardSource.SourceSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (!plan.IsIncremental && !plan.IsRebaseline)
                            throw new InvalidOperationException(
                                $"来源记录已变化，拒绝覆盖：{cardSource.Entity}/{cardSource.SourceId}");
                        await AddRevisionAsync(runId, tenant.Id, cardSource, existingCardMap, maps,
                            cancellationToken);
                    }
                }
                else
                {
                    await AddMapAsync(runId, tenant.Id, cardSource, "membership_cards", card.Id, maps,
                        cancellationToken);
                }
                cardPlans.Add((plan, card));
            }
            await db.SaveChangesAsync(cancellationToken);

            var cardIds = cardPlans.Select(item => item.Card.Id).Distinct().ToArray();
            var accountsByCardAndType = cardIds.Length == 0
                ? new Dictionary<(Guid, MemberAccountType), MemberAccount>()
                : (await db.MemberAccounts.Where(account => account.TenantId == tenant.Id &&
                        cardIds.Contains(account.CardId)).ToListAsync(cancellationToken))
                    .ToDictionary(account => (account.CardId, account.AccountType));
            foreach (var cardId in cardIds)
            foreach (var accountType in Enum.GetValues<MemberAccountType>())
            {
                if (accountsByCardAndType.ContainsKey((cardId, accountType))) continue;
                var plan = cardPlans.First(item => item.Card.Id == cardId).Plan;
                var account = new MemberAccount(tenant.Id, plan.CustomerId, cardId, accountType);
                db.MemberAccounts.Add(account);
                accountsByCardAndType[(cardId, accountType)] = account;
                Increment(created, "member-accounts");
            }
            await db.SaveChangesAsync(cancellationToken);

            foreach (var item in cardPlans)
            {
                var businessId = DeterministicGuid($"{tenant.Id:N}:legacy-stored-value:{item.Plan.Row.SourceId}");
                if (item.Plan.IsRebaseline)
                {
                    ReconcileRebaseline(accountsByCardAndType[(item.Card.Id, MemberAccountType.Principal)],
                        item.Plan.PrincipalMinor, "Principal", businessId, item.Plan.Row, created);
                    ReconcileRebaseline(accountsByCardAndType[(item.Card.Id, MemberAccountType.Bonus)],
                        item.Plan.BonusMinor, "Bonus", businessId, item.Plan.Row, created);
                }
                else if (item.Plan.IsIncremental)
                {
                    ApplyBalanceDelta(accountsByCardAndType[(item.Card.Id, MemberAccountType.Principal)],
                        item.Plan.PrincipalMinor - item.Plan.PreviousPrincipalMinor, "Principal", businessId,
                        item.Plan.Row, created);
                    ApplyBalanceDelta(accountsByCardAndType[(item.Card.Id, MemberAccountType.Bonus)],
                        item.Plan.BonusMinor - item.Plan.PreviousBonusMinor, "Bonus", businessId,
                        item.Plan.Row, created);
                }
                else
                {
                    ReconcileOpeningBalance(accountsByCardAndType[(item.Card.Id, MemberAccountType.Principal)],
                        item.Plan.PrincipalMinor, "Principal", businessId, item.Plan.Row, created);
                    ReconcileOpeningBalance(accountsByCardAndType[(item.Card.Id, MemberAccountType.Bonus)],
                        item.Plan.BonusMinor, "Bonus", businessId, item.Plan.Row, created);
                }
            }
            await db.SaveChangesAsync(cancellationToken);

            foreach (var snapshot in financialSnapshots)
                await InsertFinancialSnapshotAsync(runId, tenant.Id, snapshot.CustomerId, snapshot.Row,
                    cancellationToken);
            foreach (var revision in financialRevisions)
                await InsertFinancialRevisionAsync(runId, tenant.Id, revision, cancellationToken);

            if (!command.FinancialIncrementalSync && !command.FinancialRebaseline)
            {
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
        if (dataset.TenantCode is not "B01" &&
            !string.Equals(command.ConfirmedTargetTenantCode, dataset.TenantCode, StringComparison.Ordinal))
            throw new InvalidOperationException("非测试品牌缺少精确的目标品牌二次确认");
        if (dataset.SourceFingerprintSha256.Length != 64 || !dataset.SourceFingerprintSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("来源指纹无效");
        if (dataset.Rows.Count > 20_000 || dataset.Photos.Count > 20_000 ||
            (dataset.CarePhotos?.Count ?? 0) > 20_000)
            throw new InvalidOperationException("迁移数据超过安全上限");
        if (dataset.Rows.Any(row => row.SourceSha256.Length != 64 || !row.SourceSha256.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("来源记录摘要无效");
        if ((command.FinancialIncrementalSync || command.FinancialRebaseline) && dataset.Rows.Any(row =>
                row.Entity is not ("stores" or "customers")))
            throw new InvalidOperationException("金额同步只允许门店和顾客模块");
        if ((command.FinancialIncrementalSync || command.FinancialRebaseline) && (dataset.Photos.Count > 0 ||
                (dataset.CarePhotos?.Count ?? 0) > 0))
            throw new InvalidOperationException("金额同步不接收护理或顾客图片");
        if (command.FinancialRebaseline && (command.ExpectedCurrentPrincipalMinor is null ||
                command.ExpectedCurrentBonusMinor is null || command.ExpectedMappedCustomers is null))
            throw new InvalidOperationException("金额重建缺少当前金额或映射数量护栏");
        if (!command.FinancialRebaseline && (command.ExpectedCurrentPrincipalMinor is not null ||
                command.ExpectedCurrentBonusMinor is not null || command.ExpectedMappedCustomers is not null))
            throw new InvalidOperationException("金额护栏只能用于金额重建");
        if (command.FinancialRebaseline && (command.FinancialIncrementalSync ||
                command.SyncMappedStores || command.ReconcileExistingCustomers))
            throw new InvalidOperationException("金额重建不能与其他迁移模式同时使用");
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
            WITH all_maps AS (
                SELECT source_entity,source_id,source_sha256,target_table,target_id,created_at_utc AS captured_at_utc
                FROM legacy_migration_record_maps WHERE tenant_id=@tenant_id
                UNION ALL
                SELECT source_entity,source_id,source_sha256,target_table,target_id,captured_at_utc
                FROM legacy_migration_record_revisions WHERE tenant_id=@tenant_id
            )
            SELECT DISTINCT ON (source_entity,source_id)
                source_entity,source_id,source_sha256,target_table,target_id
            FROM all_maps
            ORDER BY source_entity,source_id,captured_at_utc DESC
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<(string, string), LegacyMap>();
        while (await reader.ReadAsync(cancellationToken))
            result[(reader.GetString(0), reader.GetString(1))] =
                new LegacyMap(reader.GetString(2), reader.GetString(3), reader.GetGuid(4));
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
        maps[(row.Entity, row.SourceId)] = new LegacyMap(row.SourceSha256, targetTable, targetId);
    }

    private async Task AddRevisionAsync(Guid runId, Guid tenantId, LegacySourceRow row, LegacyMap previous,
        IDictionary<(string Entity, string SourceId), LegacyMap> maps, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            INSERT INTO legacy_migration_record_revisions
              (id,tenant_id,run_id,source_entity,source_id,source_sha256,previous_source_sha256,
               target_table,target_id,captured_at_utc)
            VALUES (@id,@tenant_id,@run_id,@source_entity,@source_id,@source_sha256,@previous_source_sha256,
               @target_table,@target_id,now())
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("source_entity", NpgsqlDbType.Varchar, row.Entity);
        command.Parameters.AddWithValue("source_id", NpgsqlDbType.Varchar, row.SourceId);
        command.Parameters.AddWithValue("source_sha256", NpgsqlDbType.Varchar, row.SourceSha256);
        command.Parameters.AddWithValue("previous_source_sha256", NpgsqlDbType.Varchar, previous.SourceSha256);
        command.Parameters.AddWithValue("target_table", NpgsqlDbType.Varchar, previous.TargetTable);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, previous.TargetId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        maps[(row.Entity, row.SourceId)] = new LegacyMap(row.SourceSha256, previous.TargetTable,
            previous.TargetId);
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

    private async Task<LegacyStoredValue> LoadPreviousStoredValueAsync(Guid tenantId, string sourceCustomerId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            SELECT GREATEST(COALESCE(source_member_store_minor,0),0),
                   GREATEST(COALESCE(source_member_bonus_minor,0),0) +
                   GREATEST(COALESCE(source_member_sbonus_minor,0),0),
                   GREATEST(COALESCE(source_member_store_minor,0),0)
            FROM (
                SELECT source_member_money_minor,source_member_bonus_minor,source_member_sbonus_minor,
                       source_member_store_minor,captured_at_utc
                FROM legacy_customer_financial_revisions
                WHERE tenant_id=@tenant_id AND source_customer_id=@source_customer_id
                UNION ALL
                SELECT source_member_money_minor,source_member_bonus_minor,source_member_sbonus_minor,
                       source_member_store_minor,captured_at_utc
                FROM legacy_customer_financial_snapshots
                WHERE tenant_id=@tenant_id AND source_customer_id=@source_customer_id
            ) evidence
            ORDER BY captured_at_utc DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("source_customer_id", NpgsqlDbType.Varchar, sourceCustomerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("已映射顾客缺少上一版财务快照；已停止金额同步");
        var principal = reader.GetInt64(0);
        var bonus = reader.GetInt64(1);
        var historicalTopup = reader.GetInt64(2);
        return new LegacyStoredValue(principal, bonus,
            principal > 0 || bonus > 0 || historicalTopup > 0);
    }

    private async Task InsertFinancialRevisionAsync(Guid runId, Guid tenantId,
        LegacyFinancialRevisionPlan revision, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            INSERT INTO legacy_customer_financial_revisions
              (id,tenant_id,run_id,customer_id,source_customer_id,source_sha256,
               source_member_money_minor,source_member_bonus_minor,source_member_sbonus_minor,
               source_member_store_minor,source_member_credit_minor,source_member_arrear_minor,
               source_member_score,principal_delta_minor,bonus_delta_minor,captured_at_utc)
            VALUES (@id,@tenant_id,@run_id,@customer_id,@source_customer_id,@source_sha256,
               @money,@bonus,@sbonus,@store,@credit,@arrear,@score,@principal_delta,@bonus_delta,now())
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Uuid, revision.CustomerId);
        command.Parameters.AddWithValue("source_customer_id", NpgsqlDbType.Varchar, revision.Row.SourceId);
        command.Parameters.AddWithValue("source_sha256", NpgsqlDbType.Varchar, revision.Row.SourceSha256);
        AddNullable(command, "money", NpgsqlDbType.Bigint, ParseMinor(Field(revision.Row, "member_money")));
        AddNullable(command, "bonus", NpgsqlDbType.Bigint, ParseMinor(Field(revision.Row, "member_bonus")));
        AddNullable(command, "sbonus", NpgsqlDbType.Bigint, ParseMinor(Field(revision.Row, "member_sbonus")));
        AddNullable(command, "store", NpgsqlDbType.Bigint, ParseMinor(Field(revision.Row, "member_store")));
        AddNullable(command, "credit", NpgsqlDbType.Bigint, ParseMinor(Field(revision.Row, "member_credit")));
        AddNullable(command, "arrear", NpgsqlDbType.Bigint, ParseMinor(Field(revision.Row, "member_arrear")));
        AddNullable(command, "score", NpgsqlDbType.Numeric, ParseDecimal(Field(revision.Row, "member_score")));
        command.Parameters.AddWithValue("principal_delta", NpgsqlDbType.Bigint,
            revision.CurrentPrincipalMinor - revision.PreviousPrincipalMinor);
        command.Parameters.AddWithValue("bonus_delta", NpgsqlDbType.Bigint,
            revision.CurrentBonusMinor - revision.PreviousBonusMinor);
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

    private void ReconcileOpeningBalance(MemberAccount account, long targetBalance, string accountLabel,
        Guid businessId, LegacySourceRow row, Dictionary<string, int> created)
    {
        if (account.BalanceUnits == targetBalance) return;
        var occurredAt = ParseOccurredAt(Field(row, "member_time2"), Field(row, "member_time1"));
        var commandId = DeterministicGuid(
            $"{account.TenantId:N}:legacy-balance:{row.SourceId}:{accountLabel}:{targetBalance}");
        var ledger = account.BalanceUnits < targetBalance
            ? account.Credit("LegacyStoredValueOpening", businessId, targetBalance - account.BalanceUnits,
                commandId, occurredAt)
            : account.Debit("LegacyBalanceReconciliation", businessId, account.BalanceUnits - targetBalance,
                commandId, occurredAt);
        db.MemberAccountLedgers.Add(ledger);
        Increment(created, accountLabel == "Principal"
            ? "stored-value-principal-ledgers"
            : "stored-value-bonus-ledgers");
    }

    private void ApplyBalanceDelta(MemberAccount account, long delta, string accountLabel, Guid businessId,
        LegacySourceRow row, Dictionary<string, int> created)
    {
        if (delta == 0) return;
        var commandId = DeterministicGuid(
            $"{account.TenantId:N}:legacy-balance-sync:{row.SourceId}:{accountLabel}:{row.SourceSha256}");
        var ledger = delta > 0
            ? account.Credit("LegacyBalanceSync", businessId, delta, commandId, DateTimeOffset.UtcNow)
            : account.Debit("LegacyBalanceSync", businessId, checked(-delta), commandId, DateTimeOffset.UtcNow);
        db.MemberAccountLedgers.Add(ledger);
        Increment(created, accountLabel == "Principal"
            ? "stored-value-principal-sync-ledgers"
            : "stored-value-bonus-sync-ledgers");
    }

    private void ReconcileRebaseline(MemberAccount account, long targetBalance, string accountLabel,
        Guid businessId, LegacySourceRow row, Dictionary<string, int> created)
    {
        if (account.BalanceUnits == targetBalance) return;
        var delta = targetBalance - account.BalanceUnits;
        var commandId = DeterministicGuid(
            $"{account.TenantId:N}:legacy-balance-rebaseline-v1:{row.SourceId}:{accountLabel}:{targetBalance}");
        var ledger = delta > 0
            ? account.Credit("LegacyBalanceRebaseline", businessId, delta, commandId, DateTimeOffset.UtcNow)
            : account.Debit("LegacyBalanceRebaseline", businessId, checked(-delta), commandId,
                DateTimeOffset.UtcNow);
        db.MemberAccountLedgers.Add(ledger);
        Increment(created, accountLabel == "Principal"
            ? "stored-value-principal-rebaseline-ledgers"
            : "stored-value-bonus-rebaseline-ledgers");
    }

    private static LegacyStoredValue StoredValue(LegacySourceRow row)
    {
        var principal = Math.Max(ParseMinor(Field(row, "member_store")) ?? 0, 0);
        var bonus = checked(Math.Max(ParseMinor(Field(row, "member_bonus")) ?? 0, 0) +
                            Math.Max(ParseMinor(Field(row, "member_sbonus")) ?? 0, 0));
        return new LegacyStoredValue(principal, bonus, principal > 0 || bonus > 0);
    }

    private static string LegacyCardNo(string sourceId)
    {
        var normalized = new string(sourceId.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length is > 0 and <= 31) return $"LEGACY-{normalized}";
        return $"LG-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)))[..24]}";
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

    private sealed record LegacyMap(string SourceSha256, string TargetTable, Guid TargetId);
    private sealed record LegacyStoredValue(long PrincipalMinor, long BonusMinor, bool HasEvidence);
    private sealed record LegacyStoredValuePlan(LegacySourceRow Row, Guid CustomerId, Guid StoreId,
        Guid CardTypeId, long PrincipalMinor, long BonusMinor, bool ReconcilesExistingCustomer,
        long PreviousPrincipalMinor, long PreviousBonusMinor, bool IsIncremental, bool IsRebaseline);
    private sealed record LegacyFinancialRevisionPlan(Guid CustomerId, LegacySourceRow Row,
        long PreviousPrincipalMinor, long PreviousBonusMinor, long CurrentPrincipalMinor, long CurrentBonusMinor);
}
