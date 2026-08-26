using System.Net;
using System.Net.Http.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Catalog;
using Erp.Application.Customers;
using Erp.Application.Facilities;
using Erp.Application.Identity;
using Erp.Application.Inventory;
using Erp.Application.Organization;
using Erp.Application.Scheduling;
using Erp.Application.Platform;
using Erp.Application.Reports;
using Erp.Application.Security;
using Erp.Application.LegacyMigration;
using Erp.Infrastructure.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Erp.Api.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class RealApiPostgreSqlTestGroup : ICollectionFixture<RealApiPostgreSqlFixture>
{
    public const string Name = "real-api-postgresql";
}

[Collection(RealApiPostgreSqlTestGroup.Name)]
public sealed class RealApiPostgreSqlFlowTests(RealApiPostgreSqlFixture fixture)
{
    [Fact]
    public async Task LegacyImportDryRunTransformsB01DataAndRollsBack()
    {
        static LegacySourceRow Row(string entity, string id, params (string Key, string? Value)[] fields) =>
            new(entity, id, new string('a', 64),
                fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        var rows = new[]
        {
            Row("stores", "901", ("shop_code", "901"), ("shop_name", "旧系统演练店"), ("shop_stop", "0")),
            Row("employee-trades", "1", ("ework_code", "TECH"), ("ework_name", "技师")),
            Row("employees", "902", ("emplee_name", "旧系统员工"), ("emplee_ework", "1"),
                ("emplee_shop", "旧系统演练店")),
            Row("services", "903", ("goods_name", "<b>旧系统服务</b>"), ("goods_status", "0")),
            Row("units", "1", ("unit_name", "盒")),
            Row("products", "904", ("goods_name", "旧系统产品"), ("goods_unit1", "1"),
                ("goods_status", "0")),
            Row("member-levels", "905", ("iclevel_name", "旧系统卡类")),
            Row("customers", "906", ("member_name", "旧系统顾客"), ("member_hand", "13900001111"),
                ("member_shop", "旧系统演练店"), ("member_sex", "女"), ("member_memo", "旧系统服务备注"),
                ("member_time2", "2026-08-20 12:00:00"), ("member_money", "123.45"),
                ("member_bonus", "10"), ("member_score", "5")),
            Row("care-records", "907", ("bill_member", "906"), ("bill_shop", "旧系统演练店"),
                ("bill_time1", "2026-08-20 13:00:00"), ("bill_date", "2026-08-20"),
                ("bill_intro", "旧系统症状"), ("bill_plan", "旧系统处理方案"),
                ("bill_next", "2026-08-27"), ("bill_emplee", "旧系统员工"), ("bill_memo", "复诊建议")),
        };
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var dataset = new LegacyImportDataset("B01", "integration-legacy", new string('b', 64),
            "integration-v1", rows,
            [new LegacySourcePhoto("906", 1, "image/png", new string('c', 64), png)],
            [new LegacySourceCarePhoto("907", 1, "image/png", new string('d', 64), png)]);

        var result = await fixture.RunLegacyImportAsync(new LegacyImportCommand(dataset, DryRun: true));

        Assert.True(result.DryRun);
        Assert.False(result.AlreadyCompleted);
        Assert.Equal(1, result.Created["stores"]);
        Assert.Equal(1, result.Created["customers"]);
        Assert.Equal(2, result.Created["service-records"]);
        Assert.Equal(1, result.Created["photos"]);
        Assert.Equal(1, result.Created["care-records"]);
        Assert.Equal(1, result.Created["care-photos"]);
        Assert.Equal(0, await fixture.CountLegacyRunsAsync("integration-legacy"));
        Assert.Equal(0, fixture.CountStoredFileBlobs());
    }

    [Fact]
    public async Task LegacyImportRejectsNonEmptyUnmappedStoreInsteadOfUsingDefault()
    {
        static LegacySourceRow Row(string entity, string id, params (string Key, string? Value)[] fields) =>
            new(entity, id, new string('d', 64),
                fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        var dataset = new LegacyImportDataset("B01", "integration-legacy-unmapped-store", new string('e', 64),
            "integration-v1",
            [
                Row("stores", "901", ("shop_code", "901"), ("shop_name", "旧系统演练店")),
                Row("customers", "906", ("member_name", "旧系统顾客"), ("member_hand", "13900001112"),
                    ("member_shop", "不存在的门店")),
            ], []);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RunLegacyImportAsync(new LegacyImportCommand(dataset, DryRun: true)));

        Assert.Contains("拒绝回退到默认门店", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await fixture.CountLegacyRunsAsync("integration-legacy-unmapped-store"));
    }

    [Fact]
    public async Task CoreStoreFlowRunsThroughRealHttpApiAndPostgreSql()
    {
        var client = fixture.Client;
        var ready = await client.GetFromJsonAsync<ReadinessResponse>("/health/ready");
        Assert.Equal("ready", ready?.Status);
        Assert.Equal("202608270035", ready?.SchemaVersion);

        var login = await PostAsync<CurrentUserDto>(client, "/api/v1/auth/login", new
        {
            account = "owner01", password = RealApiPostgreSqlFixture.InitialPassword, rememberMe = false,
        });
        Assert.True(login.MustChangePassword);

        var blockedBeforePasswordChange = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", new
        {
            storeId = login.Stores.Single().Id, name = "测试顾客", mobile = "13800138000",
            serviceNotificationConsent = false, marketingConsent = false, commandId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.Forbidden, blockedBeforePasswordChange.StatusCode);

        var changed = await PostAsync<CurrentUserDto>(client, "/api/v1/auth/change-password", new
        {
            currentPassword = RealApiPostgreSqlFixture.InitialPassword,
            newPassword = RealApiPostgreSqlFixture.ChangedPassword,
        });
        Assert.False(changed.MustChangePassword);
        var storeId = changed.Stores.Single().Id;

        var organization = await client.GetFromJsonAsync<OrganizationSettingsDto>(
            "/api/v1/organization/settings");
        Assert.Single(organization!.Stores);
        var brand = await PutAsync<BrandProfileDto>(client, "/api/v1/organization/brand", new
        {
            code = "B01", name = "集成测试品牌已更新", expectedVersion = organization.Brand.Version,
        });
        Assert.Equal("集成测试品牌已更新", brand.Name);
        var secondStore = await PostAsync<StoreProfileDto>(client, "/api/v1/organization/stores", new
        {
            name = "集成测试二店", timeZoneId = "Asia/Shanghai",
        });
        Assert.Equal("Enabled", secondStore.Status);
        Assert.Equal("S002", secondStore.Code);
        var ownerWithAllStores = await client.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me");
        Assert.Contains(ownerWithAllStores!.Stores, store => store.Id == secondStore.Id);
        using (var immutableStoreCode = await SendAsync(client, HttpMethod.Put,
                   $"/api/v1/organization/stores/{secondStore.Id}", new
                   {
                       code = "S999", name = "集成测试二店", timeZoneId = "Asia/Shanghai",
                       expectedVersion = secondStore.Version,
                   }))
        {
            Assert.Equal(HttpStatusCode.Conflict, immutableStoreCode.StatusCode);
            Assert.Contains("STORE_CODE_IMMUTABLE", await immutableStoreCode.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
        secondStore = await PutAsync<StoreProfileDto>(client,
            $"/api/v1/organization/stores/{secondStore.Id}", new
            {
                code = secondStore.Code, name = "集成测试二店已更新", timeZoneId = "Asia/Shanghai",
                expectedVersion = secondStore.Version,
            });
        Assert.Equal("S002", secondStore.Code);
        secondStore = await PostAsync<StoreProfileDto>(client,
            $"/api/v1/organization/stores/{secondStore.Id}/status", new
            {
                enable = false, reason = "自动回归门店停用验证", expectedVersion = secondStore.Version,
            });
        Assert.Equal("Disabled", secondStore.Status);
        var ownerAfterDisable = await client.GetFromJsonAsync<CurrentUserDto>("/api/v1/auth/me");
        Assert.DoesNotContain(ownerAfterDisable!.Stores, store => store.Id == secondStore.Id);
        using (var lastStoreResponse = await SendAsync(client, HttpMethod.Post,
                   $"/api/v1/organization/stores/{storeId}/status", new
                   {
                       enable = false, reason = "自动回归最后门店保护", expectedVersion = organization.Stores[0].Version,
                   }))
            Assert.Equal(HttpStatusCode.Conflict, lastStoreResponse.StatusCode);
        secondStore = await PostAsync<StoreProfileDto>(client,
            $"/api/v1/organization/stores/{secondStore.Id}/status", new
            {
                enable = true, reason = "自动回归门店恢复验证", expectedVersion = secondStore.Version,
            });
        Assert.Equal("Enabled", secondStore.Status);

        var employee = await PostAsync<EmployeeDto>(client, "/api/v1/employees", new
        {
            employeeNo = "E0002", displayName = "自动回归服务员工", positionCode = "TECHNICIAN",
            storeIds = new List<Guid> { storeId }, createLoginAccount = true, account = "technician02",
            initialPassword = "Technician_Test!123", roles = new List<string> { "TECHNICIAN" },
        });
        Assert.Matches("^EMP[0-9]{6}$", employee.EmployeeNo);
        Assert.True(employee.AccountEnabled);
        Assert.True(employee.MustChangePassword);
        employee = await PutAsync<EmployeeDto>(client, $"/api/v1/employees/{employee.Id}", new
        {
            displayName = "自动回归前台员工", positionCode = "FRONT_DESK",
            storeIds = new List<Guid> { storeId }, roles = new List<string> { "FRONT_DESK" },
            expectedVersion = employee.Version,
        });
        Assert.Equal("自动回归前台员工", employee.DisplayName);
        Assert.Equal("FRONT_DESK", employee.PositionCode);
        Assert.Equal("FRONT_DESK", Assert.Single(employee.Roles));
        employee = await PostAsync<EmployeeDto>(client, $"/api/v1/employees/{employee.Id}/reset-password", new
        {
            newInitialPassword = "Reset_Test!7890", reason = "自动回归密码重置验证",
        });
        Assert.True(employee.MustChangePassword);
        employee = await PostAsync<EmployeeDto>(client, $"/api/v1/employees/{employee.Id}/employment-status", new
        {
            reactivate = false, reason = "自动回归离职验证", expectedVersion = employee.Version,
        });
        Assert.Equal("Inactive", employee.Status);
        Assert.False(employee.AccountEnabled);
        employee = await PostAsync<EmployeeDto>(client, $"/api/v1/employees/{employee.Id}/employment-status", new
        {
            reactivate = true, reason = "自动回归复职验证", expectedVersion = employee.Version,
        });
        Assert.Equal("Active", employee.Status);
        Assert.False(employee.AccountEnabled);
        employee = await PostAsync<EmployeeDto>(client, $"/api/v1/employees/{employee.Id}/account-status", new
        {
            isEnabled = true,
        });
        Assert.True(employee.AccountEnabled);

        using (var frontDeskClient = fixture.CreateIsolatedClient())
        {
            var frontDeskLogin = await PostAsync<CurrentUserDto>(frontDeskClient, "/api/v1/auth/login", new
            {
                account = "technician02", password = "Reset_Test!7890", rememberMe = false,
            });
            Assert.True(frontDeskLogin.MustChangePassword);
            var frontDesk = await PostAsync<CurrentUserDto>(frontDeskClient, "/api/v1/auth/change-password", new
            {
                currentPassword = "Reset_Test!7890", newPassword = "FrontDesk_Test!456",
            });
            Assert.Contains(SystemPermissions.FacilityOperate, frontDesk.Permissions);
            Assert.Contains(SystemPermissions.CustomerWrite, frontDesk.Permissions);
            Assert.DoesNotContain(SystemPermissions.CashierCheckout, frontDesk.Permissions);
            Assert.DoesNotContain(SystemPermissions.InventoryRead, frontDesk.Permissions);
            using var catalogResponse = await frontDeskClient.GetAsync("/api/v1/catalog/service-items");
            using var reportResponse = await frontDeskClient.GetAsync(
                $"/api/v1/reports/operations?storeId={storeId}");
            using var cashierResponse = await frontDeskClient.GetAsync(
                $"/api/v1/cashier/orders?storeId={storeId}");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, reportResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, cashierResponse.StatusCode);
        }

        var customer = await PostAsync<CustomerDetailDto>(client, "/api/v1/customers", new
        {
            storeId, name = "测试顾客", mobile = "13800138000", gender = (string?)null,
            serviceNotificationConsent = false, marketingConsent = false, commandId = Guid.NewGuid(),
        });
        Assert.Equal("138****8000", customer.MaskedMobile);
        customer = await PutAsync<CustomerDetailDto>(client, $"/api/v1/customers/{customer.Id}", new
        {
            storeId, name = "测试顾客已更新", mobile = "13900139000", gender = "Female",
            birthDate = new DateOnly(1990, 1, 2), sourceCode = "AUTOMATION",
            serviceNotificationConsent = true, marketingConsent = false,
            expectedVersion = customer.Version, commandId = Guid.NewGuid(),
        });
        Assert.Equal("测试顾客已更新", customer.DisplayName);
        Assert.Equal("139****9000", customer.MaskedMobile);
        customer = await PostAsync<CustomerDetailDto>(client, $"/api/v1/customers/{customer.Id}/status", new
        {
            storeId, restore = false, reason = "自动回归停用验证", expectedVersion = customer.Version,
            commandId = Guid.NewGuid(),
        });
        Assert.Equal("Disabled", customer.Status);
        customer = await PostAsync<CustomerDetailDto>(client, $"/api/v1/customers/{customer.Id}/status", new
        {
            storeId, restore = true, reason = "自动回归恢复验证", expectedVersion = customer.Version,
            commandId = Guid.NewGuid(),
        });
        Assert.Equal("Active", customer.Status);
        var duplicate = await PostAsync<CustomerDetailDto>(client, "/api/v1/customers", new
        {
            storeId, name = "测试顾客旧档", mobile = "13700137000", gender = "Unknown",
            serviceNotificationConsent = false, marketingConsent = false, commandId = Guid.NewGuid(),
        });
        var mergeCardType = await PostAsync<MemberCardTypeDto>(client,
            "/api/v1/customers/membership/card-types", new
            {
                code = "MERGE_TEST", name = "合并回归卡", validityDays = (int?)null, commandId = Guid.NewGuid(),
            });
        Assert.Matches("^CT[0-9]{6}$", mergeCardType.Code);
        duplicate = await PostAsync<CustomerDetailDto>(client,
            $"/api/v1/customers/{duplicate.Id}/membership", new
            {
                storeId, cardTypeId = mergeCardType.Id, cardNo = "MERGETEST001", note = "合并回归",
                commandId = Guid.NewGuid(),
            });
        var preview = await PostAsync<CustomerMergePreviewDto>(client,
            $"/api/v1/customers/{duplicate.Id}/merge-preview", new
            {
                storeId, targetCustomerId = customer.Id,
            });
        Assert.True(preview.CanMerge);
        Assert.Equal(1, preview.SourceCardCount);
        customer = await PostAsync<CustomerDetailDto>(client,
            $"/api/v1/customers/{duplicate.Id}/merge", new
            {
                storeId, targetCustomerId = customer.Id, expectedSourceVersion = preview.SourceVersion,
                expectedTargetVersion = preview.TargetVersion, reason = "自动回归确认属于同一顾客",
                commandId = Guid.NewGuid(),
            });
        Assert.Contains(customer.MergedAliases, alias => alias.Id == duplicate.Id);
        Assert.Contains(customer.Cards, card => card.MaskedCardNo.EndsWith("T001", StringComparison.Ordinal));
        var aliasSearch = await PostAsync<PageResponse<CustomerSummaryDto>>(client,
            "/api/v1/customers/search", new { storeId, query = "13700137000", page = 1, pageSize = 20 });
        Assert.Single(aliasSearch.Items);
        Assert.Equal(customer.Id, aliasSearch.Items.Single().Id);
        var customerPage = await PostAsync<PageResponse<CustomerSummaryDto>>(client,
            "/api/v1/customers/search", new { storeId, query = "13900139000", page = 1, pageSize = 1 });
        Assert.Equal(1, customerPage.Total);
        Assert.Single(customerPage.Items);
        Assert.Equal(1, customerPage.Page);
        Assert.Equal(1, customerPage.PageSize);
        var crossStoreCustomerPage = await PostAsync<PageResponse<CustomerSummaryDto>>(client,
            "/api/v1/customers/search", new
            {
                storeId = secondStore.Id, query = "13900139000", page = 1, pageSize = 20,
            });
        Assert.Equal(customer.Id, Assert.Single(crossStoreCustomerPage.Items).Id);
        var crossStoreCustomerDetail = await client.GetFromJsonAsync<CustomerDetailDto>(
            $"/api/v1/customers/{customer.Id}?storeId={secondStore.Id}");
        Assert.Equal(storeId, crossStoreCustomerDetail!.HomeStoreId);
        Assert.Contains(crossStoreCustomerDetail.Cards, card => card.Id == customer.Cards.Single().Id);
        var invalidCustomerPage = await SendAsync(client, HttpMethod.Post, "/api/v1/customers/search",
            new { storeId, query = "", page = 1, pageSize = 101 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidCustomerPage.StatusCode);

        var serviceRecord = await CreateServiceRecordAsync(client, storeId, customer.Id);
        Assert.Empty(serviceRecord.Corrections);
        serviceRecord = await PostAsync<ServiceRecordDto>(client,
            $"/api/v1/customers/{customer.Id}/service-records/{serviceRecord.Id}/corrections", new
            {
                storeId, reason = "自动回归更正服务描述", conditionNotes = "更正后的顾客需求",
                serviceContent = "更正后的服务内容", followUpNotes = (string?)null,
                commandId = Guid.NewGuid(),
            });
        var correction = Assert.Single(serviceRecord.Corrections);
        Assert.Equal("自动回归更正服务描述", correction.Reason);
        Assert.Equal("更正后的服务内容", correction.ServiceContent);
        var crossStoreRecords = await client.GetFromJsonAsync<PageResponse<ServiceRecordDto>>(
            $"/api/v1/customers/{customer.Id}/service-records?storeId={secondStore.Id}&page=1&pageSize=20");
        Assert.Contains(crossStoreRecords!.Items, item => item.Id == serviceRecord.Id && item.StoreId == storeId);

        var service = await PostAsync<ServiceItemDto>(client, "/api/v1/catalog/service-items", new
        {
            code = "SVC01", name = "测试服务", standardDurationMinutes = 30, commissionMode = "NONE",
        }, HttpStatusCode.Created);
        Assert.Matches("^SV[0-9]{6}$", service.Code);
        var memberCard = Assert.Single(customer.Cards);
        var issuedPass = await PostAsync<ServicePassDto>(client,
            "/api/v1/membership-benefits/service-passes", new
            {
                storeId, customerId = customer.Id, cardId = memberCard.Id, serviceItemId = service.Id,
                passName = "自动回归三次卡", purchasedUses = 2, bonusUses = 1,
                validFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                validTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                reason = "自动回归发放", commandId = Guid.NewGuid(),
            });
        Assert.Equal(3, issuedPass.RemainingUses);
        var product = await PostAsync<ProductItemDto>(client, "/api/v1/catalog/products", new
        {
            code = "PRD01", name = "测试产品", unitName = "件", trackInventory = true,
        }, HttpStatusCode.Created);
        Assert.Matches("^PD[0-9]{6}$", product.Code);

        var opening = await PostAsync<InventoryDocumentDto>(client, "/api/v1/inventory/documents", new
        {
            storeId, documentType = "OPENING", reason = "自动回归期初",
            lines = new[] { new { productItemId = product.Id, quantity = 3 } }, commandId = Guid.NewGuid(),
        });
        Assert.Equal("Opening", opening.DocumentType);

        var priceBook = await PostAsync<PriceBookDto>(client, "/api/v1/catalog/price-books", new
        {
            name = "自动回归价格", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            lines = new[] { new { serviceItemId = service.Id, unitPriceMinor = 10_000L } },
            productLines = new[] { new { productItemId = product.Id, unitPriceMinor = 5_000L } },
        }, HttpStatusCode.Created);
        priceBook = await PutAsync<PriceBookDto>(client, $"/api/v1/catalog/price-books/{priceBook.Id}", new
        {
            name = "自动回归价格已编辑", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            lines = new[] { new { serviceItemId = service.Id, unitPriceMinor = 10_000L } },
            productLines = new[] { new { productItemId = product.Id, unitPriceMinor = 5_000L } },
            expectedVersion = priceBook.Version,
        });
        Assert.Equal("自动回归价格已编辑", priceBook.Name);
        var published = await PostAsync<PriceBookDto>(client,
            $"/api/v1/catalog/price-books/{priceBook.Id}/publish", new { });
        Assert.Equal("PUBLISHED", published.Status);
        var copiedPriceBook = await PostAsync<PriceBookDto>(client,
            $"/api/v1/catalog/price-books/{published.Id}/copies", new
            {
                name = "自动回归价格复制版", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            }, HttpStatusCode.Created);
        Assert.Equal("DRAFT", copiedPriceBook.Status);
        Assert.Equal(published.Lines.Count, copiedPriceBook.Lines.Count);
        Assert.Equal(published.ProductLines.Count, copiedPriceBook.ProductLines.Count);
        copiedPriceBook = await PostAsync<PriceBookDto>(client,
            $"/api/v1/catalog/price-books/{copiedPriceBook.Id}/publish", new { });
        var detailPriceBook = await client.GetFromJsonAsync<PriceBookDto>(
            $"/api/v1/catalog/price-books/{copiedPriceBook.Id}");
        Assert.Equal(copiedPriceBook.Id, detailPriceBook!.Id);
        var filteredPriceBooks = await client.GetFromJsonAsync<IReadOnlyList<PriceBookDto>>(
            "/api/v1/catalog/price-books?query=%E5%A4%8D%E5%88%B6%E7%89%88&status=PUBLISHED");
        Assert.Contains(filteredPriceBooks!, x => x.Id == copiedPriceBook.Id);
        using (var deletePublished = await SendAsync(client, HttpMethod.Delete,
                   $"/api/v1/catalog/price-books/{published.Id}", new
                   {
                       expectedVersion = published.Version, reason = "自动回归验证已发布版本删除",
                   }))
            Assert.Equal(HttpStatusCode.NoContent, deletePublished.StatusCode);
        using (var deletedPublished = await client.GetAsync($"/api/v1/catalog/price-books/{published.Id}"))
            Assert.Equal(HttpStatusCode.NotFound, deletedPublished.StatusCode);
        var cancelledDraft = await PostAsync<PriceBookDto>(client, "/api/v1/catalog/price-books", new
        {
            name = "自动回归待取消价格", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            lines = new[] { new { serviceItemId = service.Id, unitPriceMinor = 10_100L } },
            productLines = Array.Empty<object>(),
        }, HttpStatusCode.Created);
        cancelledDraft = await PostAsync<PriceBookDto>(client,
            $"/api/v1/catalog/price-books/{cancelledDraft.Id}/cancel", new
            {
                expectedVersion = cancelledDraft.Version,
            });
        Assert.Equal("RETIRED", cancelledDraft.Status);

        var group = await PostAsync<FacilityGroupDto>(client, "/api/v1/facilities/groups", new
        {
            storeId, displayName = "自动回归服务区", sortOrder = 10,
        });
        var type = await PostAsync<FacilityTypeDto>(client, "/api/v1/facilities/types", new
        {
            displayName = "自动回归服务位",
        });
        var facility = await PostAsync<FacilityBoardItemDto>(client, "/api/v1/facilities", new
        {
            storeId, groupId = group.Id, facilityTypeId = type.Id, code = "F01", displayName = "服务位01",
            serviceName = "测试服务", equipmentName = "测试设备", referencePriceMinor = (long?)null,
            sortOrder = 10, defaultCleaningMinutes = 0, allowReservation = true,
        });
        Assert.Matches("^F[0-9]{4}$", facility.Code);

        var schedulingEmployees = await client.GetFromJsonAsync<IReadOnlyList<SchedulingResourceDto>>(
            $"/api/v1/scheduling/employees?storeId={storeId}");
        Assert.Contains(schedulingEmployees!, item => item.Id == employee.Id);
        var schedulingFacilities = await client.GetFromJsonAsync<IReadOnlyList<SchedulingResourceDto>>(
            $"/api/v1/scheduling/facilities?storeId={storeId}");
        Assert.Contains(schedulingFacilities!, item => item.Id == facility.Id);
        var scheduleStart = DateTimeOffset.UtcNow.AddDays(1).AddMinutes(5);
        var shift = await PostAsync<EmployeeShiftDto>(client, "/api/v1/scheduling/shifts", new
        {
            storeId, employeeId = employee.Id, startsAtUtc = scheduleStart,
            endsAtUtc = scheduleStart.AddHours(8), note = "自动回归班次", commandId = Guid.NewGuid(),
        });
        Assert.Equal("Scheduled", shift.Status);
        using (var overlappingShift = await SendAsync(client, HttpMethod.Post, "/api/v1/scheduling/shifts", new
               {
                   storeId, employeeId = employee.Id, startsAtUtc = scheduleStart.AddHours(1),
                   endsAtUtc = scheduleStart.AddHours(2), note = (string?)null, commandId = Guid.NewGuid(),
               }))
            Assert.Equal(HttpStatusCode.Conflict, overlappingShift.StatusCode);
        shift = await PutAsync<EmployeeShiftDto>(client, $"/api/v1/scheduling/shifts/{shift.Id}", new
        {
            storeId, startsAtUtc = scheduleStart.AddMinutes(30), endsAtUtc = scheduleStart.AddHours(8),
            note = "自动回归班次已调整", expectedVersion = shift.Version,
        });
        Assert.Equal("自动回归班次已调整", shift.Note);

        var appointment = await PostAsync<AppointmentDto>(client, "/api/v1/scheduling/appointments", new
        {
            storeId, customerId = customer.Id, serviceItemId = service.Id, employeeId = employee.Id,
            facilityId = (Guid?)null, startsAtUtc = scheduleStart.AddHours(2),
            endsAtUtc = scheduleStart.AddHours(3), note = "自动回归预约", commandId = Guid.NewGuid(),
        });
        Assert.Equal("Scheduled", appointment.Status);
        using (var overlappingAppointment = await SendAsync(client, HttpMethod.Post,
                   "/api/v1/scheduling/appointments", new
                   {
                       storeId, customerId = customer.Id, serviceItemId = service.Id,
                       employeeId = employee.Id, facilityId = (Guid?)null,
                       startsAtUtc = scheduleStart.AddHours(2).AddMinutes(15),
                       endsAtUtc = scheduleStart.AddHours(2).AddMinutes(45), note = (string?)null,
                       commandId = Guid.NewGuid(),
                   }))
            Assert.Equal(HttpStatusCode.Conflict, overlappingAppointment.StatusCode);
        var appointmentPage = await client.GetFromJsonAsync<PageResponse<AppointmentDto>>(
            $"/api/v1/scheduling/appointments?storeId={storeId}&fromUtc={Uri.EscapeDataString(scheduleStart.ToString("O"))}" +
            $"&toUtc={Uri.EscapeDataString(scheduleStart.AddDays(1).ToString("O"))}&query=13900139000&page=1&pageSize=1");
        Assert.Equal(1, appointmentPage!.Total);
        Assert.Equal("139****9000", appointmentPage.Items.Single().MaskedMobile);
        appointment = await PutAsync<AppointmentDto>(client,
            $"/api/v1/scheduling/appointments/{appointment.Id}", new
            {
                storeId, serviceItemId = service.Id, employeeId = employee.Id, facilityId = (Guid?)null,
                startsAtUtc = scheduleStart.AddHours(3), endsAtUtc = scheduleStart.AddHours(4),
                note = "自动回归预约已调整", expectedVersion = appointment.Version,
            });
        appointment = await PostAsync<AppointmentDto>(client,
            $"/api/v1/scheduling/appointments/{appointment.Id}/arrive", new
            {
                storeId, reason = (string?)null, expectedVersion = appointment.Version, commandId = Guid.NewGuid(),
            });
        Assert.Equal("Arrived", appointment.Status);
        Assert.NotNull(appointment.VisitId);

        var facilityAppointment = await PostAsync<AppointmentDto>(client,
            "/api/v1/scheduling/appointments", new
            {
                storeId, customerId = customer.Id, serviceItemId = service.Id, employeeId = (Guid?)null,
                facilityId = facility.Id, startsAtUtc = scheduleStart.AddHours(5),
                endsAtUtc = scheduleStart.AddHours(6), note = "设施预约", commandId = Guid.NewGuid(),
            });
        using (var facilityConflict = await SendAsync(client, HttpMethod.Post,
                   "/api/v1/scheduling/appointments", new
                   {
                       storeId, customerId = customer.Id, serviceItemId = service.Id, employeeId = (Guid?)null,
                       facilityId = facility.Id, startsAtUtc = scheduleStart.AddHours(5).AddMinutes(10),
                       endsAtUtc = scheduleStart.AddHours(5).AddMinutes(40), note = (string?)null,
                       commandId = Guid.NewGuid(),
                   }))
            Assert.Equal(HttpStatusCode.Conflict, facilityConflict.StatusCode);
        facilityAppointment = await PostAsync<AppointmentDto>(client,
            $"/api/v1/scheduling/appointments/{facilityAppointment.Id}/cancel", new
            {
                storeId, reason = "顾客取消自动回归预约", expectedVersion = facilityAppointment.Version,
                commandId = Guid.NewGuid(),
            });
        Assert.Equal("Cancelled", facilityAppointment.Status);

        var noShow = await PostAsync<AppointmentDto>(client, "/api/v1/scheduling/appointments", new
        {
            storeId, customerId = customer.Id, serviceItemId = service.Id, employeeId = (Guid?)null,
            facilityId = (Guid?)null, startsAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            endsAtUtc = DateTimeOffset.UtcNow.AddMinutes(29), note = "爽约自动回归", commandId = Guid.NewGuid(),
        });
        noShow = await PostAsync<AppointmentDto>(client,
            $"/api/v1/scheduling/appointments/{noShow.Id}/no-show", new
            {
                storeId, reason = "顾客未按时到店", expectedVersion = noShow.Version, commandId = Guid.NewGuid(),
            });
        Assert.Equal("NoShow", noShow.Status);
        shift = await PostAsync<EmployeeShiftDto>(client, $"/api/v1/scheduling/shifts/{shift.Id}/cancel", new
        {
            storeId, reason = "自动回归班次取消", expectedVersion = shift.Version, commandId = Guid.NewGuid(),
        });
        Assert.Equal("Cancelled", shift.Status);

        var started = await PostAsync<FacilityBoardItemDto>(client, "/api/v1/facilities/sessions/start", new
        {
            storeId, facilityId = facility.Id, customerId = customer.Id, plannedServiceItemId = service.Id,
            expectedDurationMinutes = 30, note = "自动回归接待", commandId = Guid.NewGuid(),
        });
        Assert.NotNull(started.SessionId);
        Assert.NotNull(started.VisitId);
        var mergeBlockerTarget = await PostAsync<CustomerDetailDto>(client, "/api/v1/customers", new
        {
            storeId, name = "合并阻断目标", mobile = "13600136000", gender = "Unknown",
            serviceNotificationConsent = false, marketingConsent = false, commandId = Guid.NewGuid(),
        });
        var blockedMerge = await PostAsync<CustomerMergePreviewDto>(client,
            $"/api/v1/customers/{customer.Id}/merge-preview", new
            {
                storeId, targetCustomerId = mergeBlockerTarget.Id,
            });
        Assert.False(blockedMerge.CanMerge);
        Assert.Contains(blockedMerge.Blockers, blocker => blocker.Contains("接待", StringComparison.Ordinal));
        _ = await PostAsync<FacilityBoardItemDto>(client,
            $"/api/v1/facilities/sessions/{started.SessionId}/end", new
            {
                storeId, commandId = Guid.NewGuid(),
            });

        var order = await PostAsync<ServiceOrderDto>(client, "/api/v1/cashier/orders", new
        {
            storeId, visitId = started.VisitId, customerId = customer.Id, note = "自动回归消费单",
            lines = new object[]
            {
                new { lineType = "SERVICE", serviceItemId = service.Id, productItemId = (Guid?)null,
                    serviceEmployeeId = (Guid?)null, quantity = 1, actualSeconds = 90,
                    enteredPriceMinor = 10_000L, priceOverrideReason = (string?)null },
                new { lineType = "PRODUCT", serviceItemId = (Guid?)null, productItemId = product.Id,
                    serviceEmployeeId = (Guid?)null, quantity = 1, actualSeconds = (int?)null,
                    enteredPriceMinor = 5_000L, priceOverrideReason = (string?)null },
            },
            commandId = Guid.NewGuid(),
        });
        var confirmed = await PostAsync<ServiceOrderDto>(client,
            $"/api/v1/cashier/orders/{order.Id}/confirm", new
            {
                storeId, expectedVersion = order.Version, commandId = Guid.NewGuid(),
            });
        Assert.Equal("PendingPayment", confirmed.Status);

        var primaryShift = await PostAsync<CashierShiftDto>(client, "/api/v1/payments/shifts/open", new
        {
            storeId, openingCashMinor = 5_000L, commandId = Guid.NewGuid(),
        });
        var methods = (await client.GetFromJsonAsync<IReadOnlyList<PaymentMethodDto>>(
            $"/api/v1/payments/methods?storeId={storeId}"))!;
        var cash = Assert.Single(methods, x => x.Code == "CASH");
        var wechatManual = Assert.Single(methods, x => x.Code == "WECHAT_MANUAL");
        Assert.Equal("ManualExternal", wechatManual.Category);
        Assert.DoesNotContain(methods, x => x.Category == "ChannelExternal");

        var topup = await PostAsync<MemberTopupDto>(client, "/api/v1/member-topups", new
        {
            storeId, customerId = customer.Id, cardId = memberCard.Id,
            principalMinor = 50_000L, bonusMinor = 10_000L, note = "自动回归储值",
            allocations = new[] { new { methodId = cash.Id, amountMinor = 50_000L,
                externalReference = (string?)null } },
            commandId = Guid.NewGuid(),
        });
        var topupRefund = await PostAsync<RefundDto>(client, "/api/v1/refunds", new
        {
            storeId, paymentId = topup.PaymentId, expectedPaymentVersion = topup.PaymentVersion,
            reason = "自动回归部分退储值本金",
            lines = new[] { new { originalAllocationId = topup.Allocations.Single().Id, amountMinor = 20_000L } },
            commandId = Guid.NewGuid(),
        });
        _ = await PostAsync<RefundDto>(client, $"/api/v1/refunds/{topupRefund.Id}/approve", new
        {
            storeId, expectedVersion = topupRefund.Version, commandId = Guid.NewGuid(),
        });
        var topupPage = await client.GetFromJsonAsync<PageResponse<MemberTopupDto>>(
            $"/api/v1/member-topups?storeId={storeId}&customerId={customer.Id}&page=1&pageSize=20");
        var partiallyRefundedTopup = Assert.Single(topupPage!.Items);
        Assert.Equal("PartiallyRefunded", partiallyRefundedTopup.Status);
        Assert.Equal(20_000L, partiallyRefundedTopup.RefundedPrincipalMinor);
        Assert.Equal(4_000L, partiallyRefundedTopup.RevokedBonusMinor);
        Assert.Equal(30_000L, partiallyRefundedTopup.RemainingPrincipalMinor);
        var customerAfterTopupRefund = await client.GetFromJsonAsync<CustomerDetailDto>(
            $"/api/v1/customers/{customer.Id}?storeId={storeId}");
        var accountsAfterTopupRefund = Assert.Single(customerAfterTopupRefund!.Cards).Accounts;
        Assert.Equal(30_000L, Assert.Single(accountsAfterTopupRefund,
            account => account.AccountType == "Principal").BalanceUnits);
        Assert.Equal(6_000L, Assert.Single(accountsAfterTopupRefund,
            account => account.AccountType == "Bonus").BalanceUnits);

        var secondStoreShift = await PostAsync<CashierShiftDto>(client, "/api/v1/payments/shifts/open", new
        {
            storeId = secondStore.Id, openingCashMinor = 0L, commandId = Guid.NewGuid(),
        });
        var secondStoreMethods = (await client.GetFromJsonAsync<IReadOnlyList<PaymentMethodDto>>(
            $"/api/v1/payments/methods?storeId={secondStore.Id}"))!;
        var secondStoreCash = Assert.Single(secondStoreMethods, x => x.Code == "CASH");
        _ = await PostAsync<MemberTopupDto>(client, "/api/v1/member-topups", new
        {
            storeId = secondStore.Id, customerId = customer.Id, cardId = memberCard.Id,
            principalMinor = 10_000L, bonusMinor = 0L, note = "二店跨店储值回归",
            allocations = new[] { new { methodId = secondStoreCash.Id, amountMinor = 10_000L,
                externalReference = (string?)null } },
            commandId = Guid.NewGuid(),
        });
        var crossStoreOrder = await PostAsync<ServiceOrderDto>(client, "/api/v1/cashier/orders", new
        {
            storeId = secondStore.Id, visitId = (Guid?)null, customerId = customer.Id,
            note = "二店使用一店会员余额", lines = new object[]
            {
                new { lineType = "SERVICE", serviceItemId = service.Id, productItemId = (Guid?)null,
                    serviceEmployeeId = (Guid?)null, quantity = 1, actualSeconds = 60,
                    enteredPriceMinor = 10_000L, priceOverrideReason = (string?)null },
            },
            commandId = Guid.NewGuid(),
        });
        crossStoreOrder = await PostAsync<ServiceOrderDto>(client,
            $"/api/v1/cashier/orders/{crossStoreOrder.Id}/confirm", new
            {
                storeId = secondStore.Id, expectedVersion = crossStoreOrder.Version, commandId = Guid.NewGuid(),
            });
        var principalMethod = Assert.Single(secondStoreMethods, x => x.Code == "MEMBER_PRINCIPAL");
        var principalAccount = Assert.Single(accountsAfterTopupRefund, x => x.AccountType == "Principal");
        var crossStorePayment = await PostAsync<PaymentDto>(client,
            $"/api/v1/payments/orders/{crossStoreOrder.Id}/settle", new
            {
                storeId = secondStore.Id, expectedVersion = crossStoreOrder.Version,
                allocations = new[] { new { methodId = principalMethod.Id, amountMinor = 10_000L,
                    externalReference = (string?)null, memberAccountId = (Guid?)principalAccount.Id } },
                cashTenderedMinor = (long?)null, verifiedMobile = "13900139000",
                verificationChallengeId = (Guid?)null, commandId = Guid.NewGuid(),
            });
        Assert.Equal("Paid", crossStorePayment.Status);
        var crossStoreBalance = await client.GetFromJsonAsync<CustomerDetailDto>(
            $"/api/v1/customers/{customer.Id}?storeId={secondStore.Id}");
        Assert.Equal(30_000L, Assert.Single(Assert.Single(crossStoreBalance!.Cards).Accounts,
            account => account.AccountType == "Principal").BalanceUnits);

        var points = await PostAsync<MemberPointSummaryDto>(client,
            "/api/v1/membership-benefits/points/adjust", new
            {
                storeId, customerId = customer.Id, cardId = memberCard.Id, units = 100L, credit = true,
                expiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                reason = "自动回归增加积分", commandId = Guid.NewGuid(),
            });
        Assert.Equal(100L, points.BalanceUnits);
        points = await PostAsync<MemberPointSummaryDto>(client,
            "/api/v1/membership-benefits/points/adjust", new
            {
                storeId, customerId = customer.Id, cardId = memberCard.Id, units = 30L, credit = false,
                expiresOn = (DateOnly?)null, reason = "自动回归使用积分", commandId = Guid.NewGuid(),
            });
        Assert.Equal(70L, points.BalanceUnits);
        var pointDebit = Assert.Single(points.Ledgers, line => line.BusinessType == "PointManualDebit");
        points = await PostAsync<MemberPointSummaryDto>(client,
            "/api/v1/membership-benefits/points/reverse", new
            {
                storeId, cardId = memberCard.Id, ledgerId = pointDebit.Id,
                reason = "自动回归撤销积分扣减", commandId = Guid.NewGuid(),
            });
        Assert.Equal(100L, points.BalanceUnits);

        var payment = await PostAsync<PaymentDto>(client, $"/api/v1/payments/orders/{order.Id}/settle", new
        {
            storeId, expectedVersion = confirmed.Version,
            allocations = new[]
            {
                new { methodId = cash.Id, amountMinor = 4_000L,
                    externalReference = (string?)null, memberAccountId = (Guid?)null },
                new { methodId = wechatManual.Id, amountMinor = 11_000L,
                    externalReference = (string?)"WX-MANUAL-REGRESSION", memberAccountId = (Guid?)null },
            },
            cashTenderedMinor = 5_000L, verifiedMobile = (string?)null,
            verificationChallengeId = (Guid?)null, commandId = Guid.NewGuid(),
        });
        Assert.Equal("Paid", payment.Status);
        Assert.Equal(5_000L, payment.CashTenderedMinor);
        Assert.Equal(1_000L, payment.CashChangeMinor);
        var manualAllocation = Assert.Single(payment.Allocations,
            allocation => allocation.MethodCode == "WECHAT_MANUAL");
        Assert.Equal("ManualPendingReconciliation", manualAllocation.ConfirmationStatus);
        Assert.Equal("Pending", manualAllocation.ReconciliationStatus);
        var firstReceipt = await PostAsync<PaymentReceiptDto>(client,
            $"/api/v1/payments/{payment.Id}/receipt", new { storeId, commandId = Guid.NewGuid() });
        var reprintedReceipt = await PostAsync<PaymentReceiptDto>(client,
            $"/api/v1/payments/{payment.Id}/receipt", new { storeId, commandId = Guid.NewGuid() });
        Assert.Equal(1, firstReceipt.PrintSequence);
        Assert.Equal("顾客联", firstReceipt.PrintLabel);
        Assert.Equal(2, reprintedReceipt.PrintSequence);
        Assert.StartsWith("补打联", reprintedReceipt.PrintLabel);
        Assert.Equal(1_000L, reprintedReceipt.CashChangeMinor);

        var afterSale = await client.GetFromJsonAsync<IReadOnlyList<InventoryBalanceDto>>(
            $"/api/v1/inventory/balances?storeId={storeId}");
        Assert.Equal(2, Assert.Single(afterSale!, x => x.ProductItemId == product.Id).OnHandQuantity);

        var refund = await PostAsync<RefundDto>(client, "/api/v1/refunds", new
        {
            storeId, paymentId = payment.Id, expectedPaymentVersion = payment.Version,
            reason = "自动回归部分退款",
            lines = new[] { new { originalAllocationId = payment.Allocations.Single(x =>
                x.MethodCode == "CASH").Id, amountMinor = 1_000L } },
            commandId = Guid.NewGuid(),
        });
        var completedRefund = await PostAsync<RefundDto>(client, $"/api/v1/refunds/{refund.Id}/approve", new
        {
            storeId, expectedVersion = refund.Version, commandId = Guid.NewGuid(),
        });
        Assert.Equal("Completed", completedRefund.Status);

        var reportDate = CurrentShanghaiDate();
        var storeReport = await client.GetFromJsonAsync<OperationsReportDto>(
            $"/api/v1/reports/operations?storeId={storeId}&fromDate={reportDate:yyyy-MM-dd}" +
            $"&toDate={reportDate:yyyy-MM-dd}");
        Assert.Equal(14_000L, storeReport!.Summary.NetRevenueMinor);
        Assert.Equal(11_000L, storeReport.Summary.PendingReconciliationMinor);
        Assert.Equal(30_000L, storeReport.Summary.StoredValuePrincipalMinor);
        Assert.Equal(6_000L, storeReport.Summary.StoredValueBonusMinor);
        Assert.Equal(36_000L, storeReport.Summary.StoredValueNetMinor);

        var storeOverview = await client.GetFromJsonAsync<BrandStoreFinancialOverviewDto>(
            $"/api/v1/reports/store-overview?fromDate={reportDate:yyyy-MM-dd}" +
            $"&toDate={reportDate:yyyy-MM-dd}");
        Assert.Equal(2, storeOverview!.Stores.Count);
        Assert.Equal(24_000L, storeOverview.TodayRevenueMinor);
        Assert.Equal(46_000L, storeOverview.StoredValueNetMinor);
        var primaryStoreOverview = Assert.Single(storeOverview.Stores, x => x.StoreId == storeId);
        Assert.Equal(14_000L, primaryStoreOverview.PeriodNetRevenueMinor);
        Assert.Equal(36_000L, primaryStoreOverview.StoredValueNetMinor);
        var secondStoreOverview = Assert.Single(storeOverview.Stores, x => x.StoreId == secondStore.Id);
        Assert.Equal(10_000L, secondStoreOverview.PeriodNetRevenueMinor);
        Assert.Equal(10_000L, secondStoreOverview.StoredValueNetMinor);

        var autoClosedSecondStoreShift = await PostAsync<CashierShiftDto>(client,
            $"/api/v1/payments/shifts/{secondStoreShift.Id}/submit", new
            {
                storeId = secondStore.Id, expectedVersion = secondStoreShift.Version,
                submittedCashMinor = 10_000L, note = "无差额自动关班回归", commandId = Guid.NewGuid(),
            });
        Assert.Equal("Closed", autoClosedSecondStoreShift.Status);
        Assert.Equal(10_000L, autoClosedSecondStoreShift.ExpectedCashMinor);
        Assert.Equal(0L, autoClosedSecondStoreShift.CashDifferenceMinor);
        Assert.Equal(0L, autoClosedSecondStoreShift.PendingReconciliationMinor);
        Assert.NotNull(autoClosedSecondStoreShift.ClosedAtUtc);
        Assert.Null(autoClosedSecondStoreShift.ReviewedBy);

        var submittedPrimaryShift = await PostAsync<CashierShiftDto>(client,
            $"/api/v1/payments/shifts/{primaryShift.Id}/submit", new
            {
                storeId, expectedVersion = primaryShift.Version, submittedCashMinor = 38_000L,
                note = "人工收款交班回归", commandId = Guid.NewGuid(),
            });
        Assert.Equal("ReviewPending", submittedPrimaryShift.Status);
        Assert.Equal(38_000L, submittedPrimaryShift.ExpectedCashMinor);
        Assert.Equal(0L, submittedPrimaryShift.CashDifferenceMinor);
        Assert.Equal(11_000L, submittedPrimaryShift.PendingReconciliationMinor);

        var ownerSelfReviewedShift = await PostAsync<CashierShiftDto>(client,
            $"/api/v1/payments/shifts/{submittedPrimaryShift.Id}/review", new
            {
                storeId, expectedVersion = submittedPrimaryShift.Version,
                reason = "最高权限确认外部待核对款项并完成关班", commandId = Guid.NewGuid(),
            });
        Assert.Equal("Closed", ownerSelfReviewedShift.Status);
        Assert.Equal(login.Id, ownerSelfReviewedShift.ReviewedBy);
        Assert.Equal("最高权限确认外部待核对款项并完成关班", ownerSelfReviewedShift.ReviewReason);
        Assert.Equal(11_000L, ownerSelfReviewedShift.PendingReconciliationMinor);

        var redeemedPass = await PostAsync<ServicePassDto>(client,
            $"/api/v1/membership-benefits/service-passes/{issuedPass.Id}/redeem", new
            {
                storeId, uses = 2, serviceOrderId = order.Id, reason = "自动回归次卡核销",
                expectedVersion = issuedPass.Version, commandId = Guid.NewGuid(),
            });
        Assert.Equal(1, redeemedPass.RemainingUses);
        var redemption = Assert.Single(redeemedPass.Ledgers, line => line.Action == "Redeem");
        var reversedPass = await PostAsync<ServicePassDto>(client,
            $"/api/v1/membership-benefits/service-passes/{issuedPass.Id}/reverse", new
            {
                storeId, ledgerId = redemption.Id, reason = "自动回归撤销次卡核销",
                expectedVersion = redeemedPass.Version, commandId = Guid.NewGuid(),
            });
        Assert.Equal(3, reversedPass.RemainingUses);
        var benefits = await client.GetFromJsonAsync<MembershipBenefitsDto>(
            $"/api/v1/membership-benefits?storeId={storeId}&customerId={customer.Id}");
        Assert.Equal(3, Assert.Single(benefits!.ServicePasses).RemainingUses);
        Assert.Equal(100L, Assert.Single(benefits.PointAccounts).BalanceUnits);

        var orderAfterRefund = await client.GetFromJsonAsync<ServiceOrderDto>(
            $"/api/v1/cashier/orders/{order.Id}?storeId={storeId}");
        var productLine = Assert.Single(orderAfterRefund!.Lines, x => x.ProductItemId == product.Id);
        _ = await PostAsync<ProductReturnDto>(client, "/api/v1/inventory/product-returns", new
        {
            storeId, orderId = order.Id, orderLineId = productLine.Id, quantity = 1,
            reason = "自动回归退货", expectedOrderVersion = orderAfterRefund.Version, commandId = Guid.NewGuid(),
        });
        var afterReturn = await client.GetFromJsonAsync<IReadOnlyList<InventoryBalanceDto>>(
            $"/api/v1/inventory/balances?storeId={storeId}");
        Assert.Equal(3, Assert.Single(afterReturn!, x => x.ProductItemId == product.Id).OnHandQuantity);

        var orderPage = await client.GetFromJsonAsync<PageResponse<ServiceOrderDto>>(
            $"/api/v1/cashier/orders?storeId={storeId}&page=1&pageSize=1");
        Assert.Equal(1, orderPage!.Total);
        Assert.Single(orderPage.Items);
        var filteredOrders = await client.GetFromJsonAsync<PageResponse<ServiceOrderDto>>(
            $"/api/v1/cashier/orders?storeId={storeId}&query=%E6%B5%8B%E8%AF%95%E6%9C%8D%E5%8A%A1" +
            $"&customerId={customer.Id}&catalogItemId={service.Id}&status=PartiallyRefunded" +
            $"&fromDate={CurrentShanghaiDate():yyyy-MM-dd}" +
            $"&toDate={CurrentShanghaiDate():yyyy-MM-dd}&page=1&pageSize=20");
        Assert.Single(filteredOrders!.Items);
        Assert.Equal(order.Id, filteredOrders.Items.Single().Id);
        var invalidOrderFilter = await client.GetAsync(
            $"/api/v1/cashier/orders?storeId={storeId}&status=Unknown&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidOrderFilter.StatusCode);
        var paymentPage = await client.GetFromJsonAsync<PageResponse<PaymentDto>>(
            $"/api/v1/payments?storeId={storeId}&page=1&pageSize=1");
        Assert.Equal(2, paymentPage!.Total);
        var refundPage = await client.GetFromJsonAsync<PageResponse<RefundDto>>(
            $"/api/v1/refunds?storeId={storeId}&page=1&pageSize=1");
        Assert.Equal(2, refundPage!.Total);
        var movementPage = await client.GetFromJsonAsync<PageResponse<InventoryMovementDto>>(
            $"/api/v1/inventory/movements?storeId={storeId}&page=1&pageSize=1");
        Assert.True(movementPage!.Total >= 3);
        Assert.Single(movementPage.Items);
        var documentPage = await client.GetFromJsonAsync<PageResponse<InventoryDocumentDto>>(
            $"/api/v1/inventory/documents?storeId={storeId}&page=1&pageSize=1");
        Assert.Equal(1, documentPage!.Total);

        var supplier = await PostAsync<SupplierDto>(client, "/api/v1/supply-chain/suppliers", new
        {
            code = "SUP-01", name = "集成测试供应商", contactName = "供应商联系人",
            mobile = "13800001111", settlementTerms = "月结",
        }, HttpStatusCode.Created);
        Assert.Matches("^SUP[0-9]{6}$", supplier.Code);
        Assert.Equal("Active", supplier.Status);
        var purchase = await PostAsync<PurchaseReceiptDto>(client,
            "/api/v1/supply-chain/purchase-receipts", new
            {
                storeId, supplierId = supplier.Id, externalNo = "EXT-001", note = "集成测试采购入库",
                commandId = Guid.NewGuid(), lines = new[]
                {
                    new { productItemId = product.Id, quantity = 5, unitCostMinor = 1234L,
                        batchNo = "BATCH-20260818", expiresOn = new DateOnly(2027, 8, 18) },
                },
            }, HttpStatusCode.Created);
        Assert.Equal(6170, purchase.TotalCostMinor);
        var lots = await client.GetFromJsonAsync<PageResponse<InventoryLotDto>>(
            $"/api/v1/supply-chain/lots?storeId={storeId}&productItemId={product.Id}&page=1&pageSize=100");
        Assert.Contains(lots!.Items, lot => lot.BatchNo == "BATCH-20260818" &&
            lot.UnitCostMinor == 1234 && lot.RemainingQuantity == 5);

        var balanceBeforeStocktake = (await client.GetFromJsonAsync<IReadOnlyList<InventoryBalanceDto>>(
            $"/api/v1/inventory/balances?storeId={storeId}"))!.Single(x => x.ProductItemId == product.Id);
        var stocktake = await PostAsync<StocktakeDto>(client, "/api/v1/supply-chain/stocktakes", new
        {
            storeId, reason = "集成测试盘点", commandId = Guid.NewGuid(),
            lines = new[] { new { productItemId = product.Id,
                countedQuantity = balanceBeforeStocktake.OnHandQuantity } },
        }, HttpStatusCode.Created);
        Assert.Equal("PendingApproval", stocktake.Status);
        stocktake = await PostAsync<StocktakeDto>(client,
            $"/api/v1/supply-chain/stocktakes/{stocktake.Id}/cancel", new
            {
                storeId, reason = "集成测试取消盘点", expectedVersion = stocktake.Version,
                commandId = Guid.NewGuid(),
            });
        Assert.Equal("Cancelled", stocktake.Status);

        var transfer = await PostAsync<InventoryTransferDto>(client, "/api/v1/supply-chain/transfers", new
        {
            sourceStoreId = storeId, destinationStoreId = secondStore.Id, reason = "集成测试跨店调拨",
            commandId = Guid.NewGuid(),
            lines = new[] { new { productItemId = product.Id, quantity = 2 } },
        }, HttpStatusCode.Created);
        transfer = await PostAsync<InventoryTransferDto>(client,
            $"/api/v1/supply-chain/transfers/{transfer.Id}/ship", new
            {
                reason = "集成测试确认出库", expectedVersion = transfer.Version,
                commandId = Guid.NewGuid(),
            });
        Assert.Equal("InTransit", transfer.Status);
        transfer = await PostAsync<InventoryTransferDto>(client,
            $"/api/v1/supply-chain/transfers/{transfer.Id}/receive", new
            {
                reason = "集成测试确认收货", expectedVersion = transfer.Version,
                commandId = Guid.NewGuid(),
            });
        Assert.Equal("Received", transfer.Status);
        var destinationBalance = (await client.GetFromJsonAsync<IReadOnlyList<InventoryBalanceDto>>(
            $"/api/v1/inventory/balances?storeId={secondStore.Id}"))!.Single(x =>
                x.ProductItemId == product.Id);
        Assert.Equal(2, destinationBalance.OnHandQuantity);
        using (var deleteReferencedPublished = await SendAsync(client, HttpMethod.Delete,
                   $"/api/v1/catalog/price-books/{copiedPriceBook.Id}", new
                   {
                       expectedVersion = copiedPriceBook.Version, reason = "自动回归验证已被订单引用的版本删除",
                   }))
            Assert.Equal(HttpStatusCode.NoContent, deleteReferencedPublished.StatusCode);
        var orderAfterPriceBookDeletion = await client.GetFromJsonAsync<ServiceOrderDto>(
            $"/api/v1/cashier/orders/{order.Id}?storeId={storeId}");
        Assert.Null(orderAfterPriceBookDeletion!.PriceBookId);
        Assert.NotEmpty(orderAfterPriceBookDeletion.Lines);
        Assert.True(orderAfterPriceBookDeletion.ReceivableMinor > 0);
        var invalidOrderPage = await client.GetAsync(
            $"/api/v1/cashier/orders?storeId={storeId}&page=1&pageSize=101");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidOrderPage.StatusCode);
    }

    [Fact]
    public async Task PlatformRegistrationSecurityEventsAndTenantSuspensionAreIsolated()
    {
        using var client = fixture.CreateIsolatedClient();
        using (var unauthorized = await client.GetAsync("/api/v1/platform/merchants?page=1&pageSize=20"))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var receipt = await PostAsync<MerchantRegistrationReceiptDto>(client,
            "/api/v1/public/merchant-registration-applications", new
            {
                merchantName = "平台回归商户", storeName = "平台回归首店", contactName = "回归负责人",
                contactMobile = "13600136000", contactEmail = "owner@example.test",
                desiredOwnerAccount = "platform-merchant-owner", note = "平台端到端自动回归",
                acceptedTerms = true,
            }, HttpStatusCode.Created);
        Assert.Equal("PendingReview", receipt.Status);

        using (var failedLogin = await SendAsync(client, HttpMethod.Post, "/api/v1/platform/auth/login", new
        {
            account = "unknown.platform", password = "Wrong_Password!123", rememberMe = false,
        }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
            Assert.Contains("INVALID_CREDENTIALS", await failedLogin.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        var platform = await PostAsync<PlatformCurrentUserDto>(client, "/api/v1/platform/auth/login", new
        {
            account = "platform.admin", password = RealApiPostgreSqlFixture.PlatformInitialPassword,
            rememberMe = false,
        });
        Assert.True(platform.MustChangePassword);
        using (var blocked = await client.GetAsync("/api/v1/platform/registration-applications?page=1&pageSize=20"))
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        platform = await PostAsync<PlatformCurrentUserDto>(client, "/api/v1/platform/auth/change-password", new
        {
            currentPassword = RealApiPostgreSqlFixture.PlatformInitialPassword,
            newPassword = RealApiPostgreSqlFixture.PlatformChangedPassword,
        });
        Assert.False(platform.MustChangePassword);

        var applications = await client.GetFromJsonAsync<PageResponse<MerchantRegistrationApplicationDto>>(
            "/api/v1/platform/registration-applications?status=PendingReview&page=1&pageSize=100");
        var application = Assert.Single(applications!.Items, x => x.Id == receipt.Id);
        application = await PostAsync<MerchantRegistrationApplicationDto>(client,
            $"/api/v1/platform/registration-applications/{application.Id}/approval", new
            {
                initialPassword = RealApiPostgreSqlFixture.MerchantInitialPassword,
                reason = "平台端到端审核通过", expectedVersion = application.Version,
            });
        Assert.Equal("Approved", application.Status);
        Assert.NotNull(application.TenantId);

        var merchants = await client.GetFromJsonAsync<PageResponse<PlatformMerchantDto>>(
            "/api/v1/platform/merchants?page=1&pageSize=100");
        var merchant = Assert.Single(merchants!.Items, x => x.Id == application.TenantId);
        Assert.Matches("^B[0-9]{12}$", merchant.Code);
        Assert.Equal("Enabled", merchant.Status);
        Assert.Equal(1, merchant.StoreCount);
        Assert.Equal(1, merchant.LoginAccountCount);

        var securityEvents = await client.GetFromJsonAsync<PageResponse<LoginSecurityEventDto>>(
            "/api/v1/platform/security-events?scope=Platform&account=platform.admin&page=1&pageSize=100");
        Assert.Contains(securityEvents!.Items, x => x.EventType == "LoginSucceeded" && x.ResultCode == "SUCCESS");
        Assert.Contains(securityEvents.Items, x => x.EventType == "PasswordChanged");
        await fixture.AssertLoginSecurityEventIsImmutableAsync(securityEvents.Items[0].Id);
        var unknownEvents = await client.GetFromJsonAsync<PageResponse<LoginSecurityEventDto>>(
            "/api/v1/platform/security-events?scope=Platform&account=unknown.platform&page=1&pageSize=100");
        var unknownEvent = Assert.Single(unknownEvents!.Items, x => x.EventType == "LoginFailed");
        Assert.NotEqual("unknown.platform", unknownEvent.Account);

        var merchantLogin = await PostAsync<CurrentUserDto>(client, "/api/v1/auth/login", new
        {
            account = "platform-merchant-owner", password = RealApiPostgreSqlFixture.MerchantInitialPassword,
            rememberMe = false,
        });
        Assert.True(merchantLogin.MustChangePassword);
        Assert.Equal("S001", Assert.Single(merchantLogin.Stores).Code);
        merchantLogin = await PostAsync<CurrentUserDto>(client, "/api/v1/auth/change-password", new
        {
            currentPassword = RealApiPostgreSqlFixture.MerchantInitialPassword,
            newPassword = "Merchant_Changed!456",
        });
        var merchantStoreId = Assert.Single(merchantLogin.Stores).Id;
        var merchantCustomer = await PostAsync<CustomerDetailDto>(client, "/api/v1/customers", new
        {
            storeId = merchantStoreId, name = "跨品牌隔离顾客", mobile = "13500135000",
            serviceNotificationConsent = false, marketingConsent = false, commandId = Guid.NewGuid(),
        });

        var otherBrandReceipt = await PostAsync<MerchantRegistrationReceiptDto>(client,
            "/api/v1/public/merchant-registration-applications", new
            {
                merchantName = "隔离回归品牌", storeName = "隔离回归首店", contactName = "隔离负责人",
                contactMobile = "13700137001", contactEmail = "isolated@example.test",
                desiredOwnerAccount = "isolated-brand-owner", note = "跨品牌隔离自动回归",
                acceptedTerms = true,
            }, HttpStatusCode.Created);
        applications = await client.GetFromJsonAsync<PageResponse<MerchantRegistrationApplicationDto>>(
            "/api/v1/platform/registration-applications?status=PendingReview&page=1&pageSize=100");
        var otherBrandApplication = Assert.Single(applications!.Items, x => x.Id == otherBrandReceipt.Id);
        otherBrandApplication = await PostAsync<MerchantRegistrationApplicationDto>(client,
            $"/api/v1/platform/registration-applications/{otherBrandApplication.Id}/approval", new
            {
                initialPassword = RealApiPostgreSqlFixture.MerchantInitialPassword,
                reason = "跨品牌隔离自动回归审核", expectedVersion = otherBrandApplication.Version,
            });
        using var otherBrandClient = fixture.CreateIsolatedClient();
        var otherBrandOwner = await PostAsync<CurrentUserDto>(otherBrandClient, "/api/v1/auth/login", new
        {
            account = "isolated-brand-owner", password = RealApiPostgreSqlFixture.MerchantInitialPassword,
            rememberMe = false,
        });
        otherBrandOwner = await PostAsync<CurrentUserDto>(otherBrandClient, "/api/v1/auth/change-password", new
        {
            currentPassword = RealApiPostgreSqlFixture.MerchantInitialPassword,
            newPassword = "Isolated_Changed!789",
        });
        var otherBrandStoreId = Assert.Single(otherBrandOwner.Stores).Id;
        var otherBrandCustomer = await PostAsync<CustomerDetailDto>(otherBrandClient, "/api/v1/customers", new
        {
            storeId = otherBrandStoreId, name = "另一品牌同手机号顾客", mobile = "13500135000",
            serviceNotificationConsent = false, marketingConsent = false, commandId = Guid.NewGuid(),
        });
        Assert.NotEqual(merchantCustomer.Id, otherBrandCustomer.Id);
        using (var crossTenantRead = await otherBrandClient.GetAsync(
                   $"/api/v1/customers/{merchantCustomer.Id}?storeId={otherBrandStoreId}"))
            Assert.Equal(HttpStatusCode.NotFound, crossTenantRead.StatusCode);

        merchant = await PostAsync<PlatformMerchantDto>(client,
            $"/api/v1/platform/merchants/{merchant.Id}/status-change", new
            {
                enable = false, reason = "验证商户停用即时失效", expectedVersion = merchant.Version,
            });
        Assert.Equal("Disabled", merchant.Status);
        using (var disabledSession = await client.GetAsync("/api/v1/auth/me"))
            Assert.Equal(HttpStatusCode.Unauthorized, disabledSession.StatusCode);
        merchant = await PostAsync<PlatformMerchantDto>(client,
            $"/api/v1/platform/merchants/{merchant.Id}/status-change", new
            {
                enable = true, reason = "完成停用验证后恢复", expectedVersion = merchant.Version,
            });
        Assert.Equal("Enabled", merchant.Status);

        var merchantEvents = await client.GetFromJsonAsync<PageResponse<LoginSecurityEventDto>>(
            "/api/v1/platform/security-events?scope=Merchant&account=platform-merchant-owner&page=1&pageSize=100");
        Assert.Contains(merchantEvents!.Items, x => x.EventType == "LoginSucceeded" && x.TenantId == merchant.Id);
        using var logout = await SendAsync(client, HttpMethod.Post, "/api/v1/platform/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    private static DateOnly CurrentShanghaiDate()
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body,
        HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        using var response = await SendAsync(client, HttpMethod.Post, path, body);
        if (response.StatusCode != expectedStatus)
        {
            var error = await response.Content.ReadAsStringAsync();
            Assert.Fail($"POST {path} expected {(int)expectedStatus}, got {(int)response.StatusCode}: {error}");
        }
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> PutAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await SendAsync(client, HttpMethod.Put, path, body);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync();
            Assert.Fail($"PUT {path} expected 200, got {(int)response.StatusCode}: {error}");
        }
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<ServiceRecordDto> CreateServiceRecordAsync(HttpClient client, Guid storeId,
        Guid customerId)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/security/csrf");
        using var content = new MultipartFormDataContent
        {
            { new StringContent(storeId.ToString()), "storeId" },
            { new StringContent(Guid.NewGuid().ToString()), "commandId" },
            { new StringContent(DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")), "serviceOccurredAtUtc" },
            { new StringContent("原始顾客需求"), "conditionNotes" },
            { new StringContent("原始服务内容"), "serviceContent" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/customers/{customerId}/service-records") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        using var response = await client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.Created)
            Assert.Fail($"Create service record expected 201, got {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        object body)
    {
        var csrfPath = path.StartsWith("/api/v1/platform/", StringComparison.Ordinal) &&
            !path.Equals("/api/v1/platform/auth/login", StringComparison.Ordinal)
                ? "/api/v1/platform/auth/csrf"
                : "/api/v1/security/csrf";
        var csrf = await client.GetFromJsonAsync<CsrfResponse>(csrfPath);
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }

    private sealed record CsrfResponse(string Token);
    private sealed record ReadinessResponse(string Status, string SchemaVersion);
    private sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
}

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "xUnit 2 fixtures release the HTTP factory and container through IAsyncLifetime.DisposeAsync.")]
public sealed class RealApiPostgreSqlFixture : IAsyncLifetime
{
    internal const string InitialPassword = "Initial_Test!123";
    internal const string ChangedPassword = "Changed_Test!456";
    internal const string PlatformInitialPassword = "Platform_Initial!123";
    internal const string PlatformChangedPassword = "Platform_Changed!456";
    internal const string MerchantInitialPassword = "Merchant_Initial!123";

    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18.4-alpine")
        .WithDatabase("erp_integration")
        .WithUsername("erp_test")
        .WithPassword("Integration_Test!42")
        .Build();
    private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "erp-real-api-tests",
        Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> previousEnvironment = new(StringComparer.Ordinal);
    private ErpTestApplicationFactory? factory;

    public HttpClient Client { get; private set; } = null!;

    public HttpClient CreateIsolatedClient() => factory?.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    }) ?? throw new InvalidOperationException("测试应用尚未初始化");

    public async Task<LegacyImportResult> RunLegacyImportAsync(LegacyImportCommand command)
    {
        if (factory is null) throw new InvalidOperationException("测试应用尚未初始化");
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILegacyImportService>()
            .ImportAsync(command, CancellationToken.None);
    }

    public async Task<int> CountLegacyRunsAsync(string sourceSystem)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM legacy_migration_runs WHERE source_system=@source_system", connection);
        command.Parameters.AddWithValue("source_system", sourceSystem);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public int CountStoredFileBlobs()
    {
        var root = Path.Combine(temporaryRoot, "files");
        return Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.blob", SearchOption.AllDirectories).Count() : 0;
    }

    public async Task AssertLoginSecurityEventIsImmutableAsync(Guid eventId)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE login_security_events SET result_code = result_code WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", eventId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("55000", exception.SqlState);
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(temporaryRoot);
        await database.StartAsync();
        await ApplyMigrationsAsync(database.GetConnectionString());
        SetProcessConfiguration(database.GetConnectionString());

        factory = new ErpTestApplicationFactory(database.GetConnectionString(), temporaryRoot);
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProductionBootstrapper>()
            .BootstrapAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<PlatformAdminBootstrapper>()
            .BootstrapAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        try
        {
            Client?.Dispose();
            if (factory is not null) await factory.DisposeAsync();
        }
        finally
        {
            try
            {
                await database.DisposeAsync();
                if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
            }
            finally
            {
                foreach (var (key, value) in previousEnvironment)
                    Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
            }
        }
    }

    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrations = Directory.GetFiles(Path.Combine(repositoryRoot, "db", "migrations"), "V*.sql")
            .Order(StringComparer.Ordinal).ToList();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var migration in migrations)
        {
            await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(migration), connection)
            {
                CommandTimeout = 120,
            };
            await command.ExecuteNonQueryAsync();
        }
    }

    private void SetProcessConfiguration(string connectionString)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings__ErpDatabase"] = connectionString,
            ["AllowedHosts"] = "localhost",
            ["CustomerPrivacy__LookupPepper"] = "integration-customer-pepper-1234567890",
            ["MemberVerification__CodePepper"] = "integration-member-pepper-123456789012",
            ["SecurityEvents__AccountHashPepper"] = "integration-login-security-pepper-1234567890",
            ["PlatformRegistration__ContactHashPepper"] = "integration-registration-pepper-123456789012",
            ["FileStorage__RootPath"] = Path.Combine(temporaryRoot, "files"),
            ["DataProtection__KeyRingPath"] = Path.Combine(temporaryRoot, "keys"),
        };
        foreach (var (key, value) in values)
        {
            previousEnvironment[key] = Environment.GetEnvironmentVariable(key,
                EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("无法定位 ERP 仓库根目录");
    }
}

internal sealed class ErpTestApplicationFactory(string connectionString, string temporaryRoot)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:ErpDatabase"] = connectionString,
                ["AllowedHosts"] = "localhost",
                ["CustomerPrivacy:LookupPepper"] = "integration-customer-pepper-1234567890",
                ["MemberVerification:CodePepper"] = "integration-member-pepper-123456789012",
                ["SecurityEvents:AccountHashPepper"] = "integration-login-security-pepper-1234567890",
                ["PlatformRegistration:ContactHashPepper"] = "integration-registration-pepper-123456789012",
                ["FileStorage:RootPath"] = Path.Combine(temporaryRoot, "files"),
                ["DataProtection:KeyRingPath"] = Path.Combine(temporaryRoot, "keys"),
                ["ERP_BOOTSTRAP_CONFIRM"] = ProductionBootstrapper.RequiredConfirmation,
                ["ERP_BOOTSTRAP_TENANT_CODE"] = "B01",
                ["ERP_BOOTSTRAP_TENANT_NAME"] = "集成测试品牌",
                ["ERP_BOOTSTRAP_STORE_CODE"] = "S01",
                ["ERP_BOOTSTRAP_STORE_NAME"] = "集成测试门店",
                ["ERP_BOOTSTRAP_OWNER_ACCOUNT"] = "owner01",
                ["ERP_BOOTSTRAP_OWNER_DISPLAY_NAME"] = "集成测试负责人",
                ["ERP_BOOTSTRAP_OWNER_EMPLOYEE_NO"] = "E0001",
                ["ERP_BOOTSTRAP_OWNER_PASSWORD"] = RealApiPostgreSqlFixture.InitialPassword,
                ["ERP_PLATFORM_BOOTSTRAP_CONFIRM"] = PlatformAdminBootstrapper.RequiredConfirmation,
                ["ERP_PLATFORM_ADMIN_ACCOUNT"] = "platform.admin",
                ["ERP_PLATFORM_ADMIN_DISPLAY_NAME"] = "平台集成管理员",
                ["ERP_PLATFORM_ADMIN_PASSWORD"] = RealApiPostgreSqlFixture.PlatformInitialPassword,
            }));
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
            logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        });
    }
}
