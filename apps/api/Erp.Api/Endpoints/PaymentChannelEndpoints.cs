using Erp.Application.Cashier;
using Erp.Application.Identity;
using Erp.Application.Security;
using Erp.Domain.Cashier;

namespace Erp.Api.Endpoints;

public static class PaymentChannelEndpoints
{
    private static readonly string[] ConfigurationReaders = [SystemRoles.Owner, SystemRoles.StoreManager];

    public static IEndpointRouteBuilder MapPaymentChannelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/payment-channels").WithTags("Payment Channels")
            .RequireAuthorization(policy => policy.RequireRole(ConfigurationReaders));

        group.MapGet("/configurations", async (Guid storeId, IIdentityService identity,
            IPaymentChannelConfigurationService channels, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await channels.ListAsync(current.TenantId, storeId, cancellationToken));
        });

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
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);

    private sealed record ConfigureChannelRequest(Guid StoreId, string? Environment, string? DisplayName,
        string? CredentialProfile, bool IsEnabled, uint ExpectedVersion);
}
