using System.Net;
using System.Net.Http.Json;
using Erp.Application.Cashier;
using Erp.Application.Catalog;
using Erp.Application.Identity;
using Erp.Application.Reports;

namespace Erp.Api.IntegrationTests;

// Separate disposable database and serialized execution keep the existing core-flow seed unchanged.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PositionCommissionApiTestGroup : ICollectionFixture<RealApiPostgreSqlFixture>
{
    public const string Name = "position-commission-api";
}

[Collection(PositionCommissionApiTestGroup.Name)]
public sealed class PositionCommissionApiTests(RealApiPostgreSqlFixture fixture)
{
    [Fact]
    public async Task PositionRulesRunThroughConfigurationDraftSettlementAndReport()
    {
        using var client = fixture.CreateIsolatedClient();
        var user = await Post<CurrentUserDto>(client, "/api/v1/auth/login", new
        { account = "owner01", password = RealApiPostgreSqlFixture.InitialPassword });
        user = await Post<CurrentUserDto>(client, "/api/v1/auth/change-password", new
        { currentPassword = RealApiPostgreSqlFixture.InitialPassword, newPassword = RealApiPostgreSqlFixture.ChangedPassword });
        var storeId = user.Stores.Single().Id;
        var position = await Post<EmployeePositionDto>(client, "/api/v1/employees/positions", new
        { name = "自定义初级岗位", sortOrder = 10 }, HttpStatusCode.Created);
        var employee = await Post<EmployeeDto>(client, "/api/v1/employees", new
        { displayName = "岗位提成员工", positionCode = position.Code, storeIds = new[] { storeId }, createLoginAccount = false });
        var service = await Post<ServiceItemDto>(client, "/api/v1/catalog/service-items", new
        { name = "岗位提成服务", standardDurationMinutes = 30, commissionMode = "FIXED_AMOUNT", commissionFixedMinor = 2500L }, HttpStatusCode.Created);
        var book = await Post<PriceBookDto>(client, "/api/v1/catalog/price-books", new
        { name = "岗位提成价格", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            lines = new[] { new { serviceItemId = service.Id, unitPriceMinor = 10000L } } }, HttpStatusCode.Created);
        await Post<PriceBookDto>(client, $"/api/v1/catalog/price-books/{book.Id}/publish", new { });
        var path = $"/api/v1/employees/positions/{position.Id}/commissions";
        var config = (await client.GetFromJsonAsync<PositionCommissionsDto>(path))!;
        Assert.Null(config.Position.DefaultCommissionRateBasisPoints);

        async Task<PositionCommissionsDto> Configure(int? defaultRate, int? overrideRate) =>
            await Put<PositionCommissionsDto>(client, path, new
            {
                defaultRateBasisPoints = defaultRate, expectedVersion = config.Position.Version,
                services = overrideRate.HasValue ? new[] { new { serviceItemId = service.Id, rateBasisPoints = overrideRate.Value } } : [],
            });
        object[] Lines() => [new { lineType = "SERVICE", serviceItemId = service.Id,
            serviceEmployeeId = employee.Id, quantity = 1, actualSeconds = 600,
            enteredPriceMinor = 8000L, priceOverrideReason = "现场优惠" }];

        config = await Configure(1000, null);
        var order = await Post<ServiceOrderDto>(client, "/api/v1/cashier/orders", new
        { storeId, lines = Lines(), commandId = Guid.NewGuid() });
        Assert.Equal(800, (await fixture.GetCommissionSnapshotAsync(order.Lines.Single().Id)).AmountMinor);
        async Task<ServiceOrderDto> SaveDraft() => await Put<ServiceOrderDto>(client,
            $"/api/v1/cashier/orders/{order.Id}/draft", new
            { storeId, lines = Lines(), expectedVersion = order.Version, commandId = Guid.NewGuid() });

        config = await Configure(2000, 1550);
        order = await SaveDraft();
        Assert.Equal((1240L, (int?)1550), await fixture.GetCommissionSnapshotAsync(order.Lines.Single().Id));
        config = await Configure(2000, 0);
        order = await SaveDraft();
        Assert.Equal(0, (await fixture.GetCommissionSnapshotAsync(order.Lines.Single().Id)).AmountMinor);
        config = await Configure(2000, null);
        order = await SaveDraft();
        Assert.Equal(1600, (await fixture.GetCommissionSnapshotAsync(order.Lines.Single().Id)).AmountMinor);
        config = await Configure(null, null);
        order = await SaveDraft();
        Assert.Equal(2500, (await fixture.GetCommissionSnapshotAsync(order.Lines.Single().Id)).AmountMinor);
        config = await Configure(2000, 1550);
        order = await SaveDraft();

        using (var invalid = await Send(client, HttpMethod.Put, path, new
        { defaultRateBasisPoints = -1, services = Array.Empty<object>(), expectedVersion = config.Position.Version }))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var otherBrand = await fixture.SeedOtherBrandCommissionResourcesAsync();
        using (var otherPosition = await client.GetAsync($"/api/v1/employees/positions/{otherBrand.PositionId}/commissions"))
            Assert.Equal(HttpStatusCode.NotFound, otherPosition.StatusCode);
        using (var invalidProject = await Send(client, HttpMethod.Put, path, new
        { defaultRateBasisPoints = 1000, services = new[] { new { serviceItemId = otherBrand.ServiceId, rateBasisPoints = 1000 } }, expectedVersion = config.Position.Version }))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidProject.StatusCode);
        using (var duplicate = await Send(client, HttpMethod.Put, path, new
        { services = new[] { new { serviceItemId = service.Id, rateBasisPoints = 1000 }, new { serviceItemId = service.Id, rateBasisPoints = 2000 } }, expectedVersion = config.Position.Version }))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        using (var stale = await Send(client, HttpMethod.Put, path, new
        { defaultRateBasisPoints = 1000, services = Array.Empty<object>(), expectedVersion = position.Version }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        order = await Post<ServiceOrderDto>(client, $"/api/v1/cashier/orders/{order.Id}/confirm", new
        { storeId, expectedVersion = order.Version, commandId = Guid.NewGuid() });
        config = await Configure(9000, null);
        var methods = (await client.GetFromJsonAsync<List<PaymentMethodDto>>($"/api/v1/payments/methods?storeId={storeId}"))!;
        var cash = methods.Single(x => x.Code == "CASH");
        await Post<CashierShiftDto>(client, "/api/v1/payments/shifts/open", new
        { storeId, openingCashMinor = 0L, commandId = Guid.NewGuid() });
        var payment = await Post<PaymentDto>(client, $"/api/v1/payments/orders/{order.Id}/settle", new
        { storeId, expectedVersion = order.Version, allocations = new[] { new { methodId = cash.Id, amountMinor = 8000L } },
            cashTenderedMinor = 8000L, commandId = Guid.NewGuid() });
        Assert.Equal("Paid", payment.Status);
        var report = (await client.GetFromJsonAsync<OperationsReportDto>($"/api/v1/reports/operations?storeId={storeId}"))!;
        Assert.Equal(1240, report.EmployeeCommissions.Single(x => x.EmployeeId == employee.Id).NetCommissionMinor);

        var detailPath = "/api/v1/reports/employee-commissions";
        var detail = (await client.GetFromJsonAsync<EmployeeCommissionReportDto>(detailPath))!;
        Assert.Equal(1240, detail.Totals.NetCommissionMinor);
        Assert.Equal("自定义初级岗位", detail.Items.Single().PositionName);
        Assert.Equal("PositionService", detail.Items.Single().RuleSource);
        Assert.Equal(1550, detail.Items.Single().RateBasisPoints);
        var refund = await Post<RefundDto>(client, "/api/v1/refunds", new
        { storeId, paymentId = payment.Id, expectedPaymentVersion = payment.Version, reason = "提成退款冲减验证",
            lines = new[] { new { originalAllocationId = payment.Allocations.Single().Id, amountMinor = 2000L } }, commandId = Guid.NewGuid() });
        await Post<RefundDto>(client, $"/api/v1/refunds/{refund.Id}/approve", new
        { storeId, expectedVersion = refund.Version, commandId = Guid.NewGuid() });
        var secondOrder = await Post<ServiceOrderDto>(client, "/api/v1/cashier/orders", new
        { storeId, lines = Lines(), commandId = Guid.NewGuid() });
        secondOrder = await Post<ServiceOrderDto>(client, $"/api/v1/cashier/orders/{secondOrder.Id}/confirm", new
        { storeId, expectedVersion = secondOrder.Version, commandId = Guid.NewGuid() });
        await Post<PaymentDto>(client, $"/api/v1/payments/orders/{secondOrder.Id}/settle", new
        { storeId, expectedVersion = secondOrder.Version, allocations = new[] { new { methodId = cash.Id, amountMinor = 8000L } },
            cashTenderedMinor = 8000L, commandId = Guid.NewGuid() });
        detail = (await client.GetFromJsonAsync<EmployeeCommissionReportDto>($"{detailPath}?page=1&pageSize=1"))!;
        Assert.Equal(2, detail.Total);
        Assert.Single(detail.Items);
        Assert.Equal(8440, detail.Totals.GrossCommissionMinor);
        Assert.Equal(310, detail.Totals.RefundDeductionMinor);
        Assert.Equal(8130, detail.Totals.NetCommissionMinor);
        Assert.Equal(8130, detail.Employees.Single().NetCommissionMinor);
        detail = (await client.GetFromJsonAsync<EmployeeCommissionReportDto>($"{detailPath}?query={order.OrderNo}"))!;
        Assert.Equal(930, detail.Items.Single().NetCommissionMinor);
        Assert.Equal(1240, detail.Items.Single().GrossCommissionMinor);
        var operations = (await client.GetFromJsonAsync<OperationsReportDto>($"/api/v1/reports/operations?storeId={storeId}"))!;
        Assert.Equal(8130, operations.EmployeeCommissions.Single(x => x.EmployeeId == employee.Id).NetCommissionMinor);
        using (var noStoreAccess = await client.GetAsync($"{detailPath}?storeId={Guid.NewGuid()}"))
            Assert.Equal(HttpStatusCode.Forbidden, noStoreAccess.StatusCode);
        using (var invalidDates = await client.GetAsync($"{detailPath}?fromDate=2026-10-02&toDate=2026-10-01"))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidDates.StatusCode);

        using var anonymous = fixture.CreateIsolatedClient();
        using var unauthorized = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await Post<EmployeeDto>(client, "/api/v1/employees", new
        { displayName = "受限收银员工", positionCode = position.Code, storeIds = new[] { storeId },
            createLoginAccount = true, account = "position.cashier", initialPassword = "CashierTest123",
            roles = new List<string> { "CASHIER" } });
        using var cashier = fixture.CreateIsolatedClient();
        await Post<CurrentUserDto>(cashier, "/api/v1/auth/login", new { account = "position.cashier", password = "CashierTest123" });
        await Post<CurrentUserDto>(cashier, "/api/v1/auth/change-password", new { currentPassword = "CashierTest123", newPassword = "CashierChanged123" });
        using var forbidden = await cashier.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var forbiddenWrite = await Send(cashier, HttpMethod.Put, path, new
        { defaultRateBasisPoints = 1000, expectedVersion = config.Position.Version });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);
        using var forbiddenReport = await cashier.GetAsync(detailPath);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReport.StatusCode);
    }

    private static async Task<T> Post<T>(HttpClient client, string path, object body,
        HttpStatusCode status = HttpStatusCode.OK) => await Request<T>(client, HttpMethod.Post, path, body, status);
    private static async Task<T> Put<T>(HttpClient client, string path, object body) =>
        await Request<T>(client, HttpMethod.Put, path, body, HttpStatusCode.OK);
    private static async Task<T> Request<T>(HttpClient client, HttpMethod method, string path, object body, HttpStatusCode status)
    {
        using var response = await Send(client, method, path, body);
        Assert.True(response.StatusCode == status, $"{method} {path}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/security/csrf"))!;
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }
    private sealed record CsrfResponse(string Token);
}
