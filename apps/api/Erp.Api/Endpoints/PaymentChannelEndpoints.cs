using System.Text;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;
using Erp.Domain.Cashier;
using Microsoft.AspNetCore.WebUtilities;

namespace Erp.Api.Endpoints;

public static class PaymentChannelEndpoints
{
    public static IEndpointRouteBuilder MapPaymentChannelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/payment-channels").WithTags("Payment Channels")
            .RequireAuthorization();

        group.MapGet("/configurations", async (Guid storeId, IIdentityService identity,
            IPaymentChannelConfigurationService channels, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await channels.ListAsync(current.TenantId, storeId, cancellationToken));
        }).RequireAuthorization(SystemPermissions.PaymentChannelRead);

        group.MapPut("/configurations/{provider}", async (string provider, ConfigureChannelRequest request,
            IIdentityService identity, IPaymentChannelConfigurationService channels,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            if (!Enum.TryParse<PaymentChannelProvider>(provider, true, out var parsedProvider) ||
                !Enum.IsDefined(parsedProvider))
                return Results.BadRequest(new { error = new { code = "VALIDATION_FAILED", message = "支付渠道无效" } });
            if (!Enum.TryParse<PaymentChannelEnvironment>(request.Environment, true, out var environment) ||
                !Enum.IsDefined(environment))
                return Results.BadRequest(new { error = new { code = "VALIDATION_FAILED", message = "渠道环境无效" } });
            return EndpointResults.From(await channels.ConfigureAsync(current.TenantId,
                new ConfigurePaymentChannelCommand(request.StoreId, parsedProvider, environment,
                    request.DisplayName ?? string.Empty, request.CredentialProfile ?? string.Empty,
                    request.IsEnabled, request.ExpectedVersion, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.PaymentChannelManage);

        group.MapPost("/orders/{orderId:guid}/initiate", async (Guid orderId, InitiateChannelRequest request,
            IIdentityService identity, IPaymentChannelPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await payments.InitiateAsync(current.TenantId,
                new InitiatePaymentChannelCommand(request.StoreId, orderId, request.ExpectedOrderVersion,
                    request.MethodId, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierCheckout);

        group.MapGet("/orders/by-service-order/{orderId:guid}", async (Guid orderId, Guid storeId,
            IIdentityService identity, IPaymentChannelPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return EndpointResults.From(await payments.GetByServiceOrderAsync(current.TenantId, storeId,
                orderId, cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierCheckout);

        group.MapPost("/orders/{channelOrderId:guid}/query", async (Guid channelOrderId,
            OperateChannelRequest request, IIdentityService identity, IPaymentChannelPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await payments.QueryAsync(current.TenantId,
                new OperatePaymentChannelCommand(request.StoreId, channelOrderId, current.Id),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierCheckout);

        group.MapPost("/orders/{channelOrderId:guid}/close", async (Guid channelOrderId,
            OperateChannelRequest request, IIdentityService identity, IPaymentChannelPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await payments.CloseAsync(current.TenantId,
                new OperatePaymentChannelCommand(request.StoreId, channelOrderId, current.Id),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierCheckout);

        group.MapGet("/reconciliations", async (Guid storeId, DateOnly? fromDate, DateOnly? toDate,
            int? page, int? pageSize,
            IIdentityService identity, IPaymentChannelReconciliationService reconciliations,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await reconciliations.ListAsync(current.TenantId, storeId, fromDate, toDate,
                normalizedPage, normalizedPageSize, cancellationToken));
        }).RequireAuthorization(SystemPermissions.PaymentChannelRead);

        group.MapPost("/reconciliations/run", async (StartReconciliationRequest request,
            IIdentityService identity, IPaymentChannelReconciliationService reconciliations,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            if (!Enum.TryParse<PaymentChannelProvider>(request.Provider, true, out var provider) ||
                !Enum.IsDefined(provider))
                return Results.UnprocessableEntity(new
                    { error = new { code = "VALIDATION_FAILED", message = "支付渠道无效" } });
            return EndpointResults.From(await reconciliations.StartAsync(current.TenantId,
                new StartPaymentChannelReconciliationCommand(request.StoreId, provider,
                    request.BusinessDate, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.PaymentChannelManage);

        group.MapPost("/reconciliations/items/{itemId:guid}/resolve", async (Guid itemId,
            ResolveReconciliationRequest request, IIdentityService identity,
            IPaymentChannelReconciliationService reconciliations, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await reconciliations.ResolveAsync(current.TenantId,
                new ResolvePaymentChannelReconciliationItemCommand(request.StoreId, itemId,
                    request.ExpectedVersion, request.Reason ?? string.Empty, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.PaymentChannelManage);

        endpoints.MapPost("/api/integrations/payment-notifications/{provider}/{configurationId:guid}",
            ProcessNotification).AllowAnonymous().RequireRateLimiting("payment-notification")
            .WithTags("Payment Channel Notifications");

        return endpoints;
    }

    private static async Task<IResult> ProcessNotification(string provider, Guid configurationId,
        HttpRequest request, IPaymentChannelPaymentService payments, CancellationToken cancellationToken)
    {
        if (!TryProvider(provider, out var parsedProvider)) return Results.NotFound();
        const int maximumBodyBytes = 256 * 1024;
        if (request.ContentLength is > maximumBodyBytes)
            return NotificationResponse(parsedProvider, false, StatusCodes.Status413PayloadTooLarge);
        using var reader = new StreamReader(request.Body, leaveOpen: false);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(body) > maximumBodyBytes)
            return NotificationResponse(parsedProvider, false, StatusCodes.Status413PayloadTooLarge);

        IReadOnlyDictionary<string, string>? form = null;
        if (parsedProvider == PaymentChannelProvider.Alipay)
        {
            if (!request.HasFormContentType)
                return NotificationResponse(parsedProvider, false, StatusCodes.Status400BadRequest);
            var parsed = QueryHelpers.ParseQuery(body);
            if (parsed.Any(x => x.Value.Count != 1))
                return NotificationResponse(parsedProvider, false, StatusCodes.Status400BadRequest);
            form = parsed.ToDictionary(x => x.Key, x => x.Value[0] ?? string.Empty,
                StringComparer.Ordinal);
        }
        else if (!request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return NotificationResponse(parsedProvider, false, StatusCodes.Status400BadRequest);
        }

        var headers = request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        var result = await payments.ProcessNotificationAsync(new PaymentChannelNotificationCommand(
            parsedProvider, configurationId, headers, body, form), cancellationToken);
        return NotificationResponse(parsedProvider, result.Acknowledge,
            result.Acknowledge ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError);
    }

    private static IResult NotificationResponse(PaymentChannelProvider provider, bool success, int statusCode) =>
        provider == PaymentChannelProvider.Alipay
            ? Results.Text(success ? "success" : "failure", "text/plain", Encoding.UTF8, statusCode)
            : Results.Json(success
                ? new { code = "SUCCESS", message = "成功" }
                : new { code = "FAIL", message = "处理失败" }, statusCode: statusCode);

    private static bool TryProvider(string value, out PaymentChannelProvider provider)
    {
        if (value.Equals("wechat", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("wechatpay", StringComparison.OrdinalIgnoreCase))
        {
            provider = PaymentChannelProvider.WeChatPay;
            return true;
        }
        if (value.Equals("alipay", StringComparison.OrdinalIgnoreCase))
        {
            provider = PaymentChannelProvider.Alipay;
            return true;
        }
        provider = default;
        return false;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);

    private sealed record ConfigureChannelRequest(Guid StoreId, string? Environment, string? DisplayName,
        string? CredentialProfile, bool IsEnabled, uint ExpectedVersion);
    private sealed record InitiateChannelRequest(Guid StoreId, uint ExpectedOrderVersion, Guid MethodId,
        Guid CommandId);
    private sealed record OperateChannelRequest(Guid StoreId);
    private sealed record StartReconciliationRequest(Guid StoreId, string Provider, DateOnly BusinessDate);
    private sealed record ResolveReconciliationRequest(Guid StoreId, uint ExpectedVersion, string? Reason);
}
