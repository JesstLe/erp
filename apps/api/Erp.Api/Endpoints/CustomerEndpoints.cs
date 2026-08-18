using Erp.Application.Customers;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class CustomerEndpoints
{
    private static readonly string[] CustomerOperators =
        [SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.FrontDesk, SystemRoles.Cashier];
    private static readonly string[] MembershipOperators = [SystemRoles.Owner, SystemRoles.StoreManager];
    private static readonly string[] ServiceRecordOperators = [SystemRoles.Owner, SystemRoles.StoreManager];

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers").WithTags("Customers").RequireAuthorization();

        group.MapGet("", async (Guid storeId, string? query, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await customers.SearchAsync(current.TenantId, storeId, query, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators)).RequireRateLimiting("customer-search");

        group.MapGet("/{customerId:guid}", async (Guid customerId, Guid storeId, IIdentityService identity,
            ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return EndpointResults.From(await customers.GetAsync(current.TenantId, storeId, customerId, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators));

        group.MapPost("", async (CreateCustomerRequest request, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.CreateAsync(current.TenantId,
                new CreateCustomerCommand(request.StoreId, request.Name ?? string.Empty, request.Mobile ?? string.Empty,
                    request.Gender, request.BirthDate, request.SourceCode, request.ServiceNotificationConsent,
                    request.MarketingConsent, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators));

        group.MapGet("/membership/card-types", async (IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await customers.ListCardTypesAsync(current.TenantId, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators));

        group.MapPost("/membership/card-types", async (CreateCardTypeRequest request, IIdentityService identity,
            ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await customers.CreateCardTypeAsync(current.TenantId,
                new CreateMemberCardTypeCommand(request.Code ?? string.Empty, request.Name ?? string.Empty,
                    request.ValidityDays, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapPost("/{customerId:guid}/membership", async (Guid customerId, OpenMembershipRequest request,
            IIdentityService identity, ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.OpenMembershipAsync(current.TenantId,
                new OpenMembershipCommand(request.StoreId, customerId, request.CardTypeId, request.CardNo,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(MembershipOperators));

        group.MapGet("/{customerId:guid}/service-records", async (Guid customerId, Guid storeId,
            IIdentityService identity, IServiceRecordService records, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await records.ListAsync(current.TenantId, storeId, customerId, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(ServiceRecordOperators));

        group.MapGet("/{customerId:guid}/service-record-order-options", async (Guid customerId, Guid storeId,
            IIdentityService identity, IServiceRecordService records, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await records.ListOrderOptionsAsync(current.TenantId, storeId, customerId,
                cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(ServiceRecordOperators));

        group.MapPost("/{customerId:guid}/service-records", async (Guid customerId, HttpRequest request,
            IIdentityService identity, IServiceRecordService records, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!request.HasFormContentType)
                return Results.Json(new { error = new { code = "VALIDATION_FAILED", message = "请使用表单提交服务记录" } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            var form = await request.ReadFormAsync(cancellationToken);
            if (!Guid.TryParse(form["storeId"], out var storeId) || !HasStore(current, storeId))
                return Results.Forbid();
            if (!Guid.TryParse(form["commandId"], out var commandId) ||
                !DateTimeOffset.TryParse(form["serviceOccurredAtUtc"], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var occurredAtUtc))
                return Results.Json(new { error = new { code = "VALIDATION_FAILED", message = "服务时间或请求号无效" } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            Guid? serviceOrderId = Guid.TryParse(form["serviceOrderId"], out var parsedOrderId) ? parsedOrderId : null;
            if (form.Files.Count > 6)
                return Results.Json(new { error = new { code = "VALIDATION_FAILED", message = "每条服务记录最多上传6张图片" } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            var streams = new List<Stream>();
            try
            {
                var images = form.Files.Select(file =>
                {
                    var stream = file.OpenReadStream();
                    streams.Add(stream);
                    return new FileUploadInput(file.FileName, file.ContentType, file.Length, stream);
                }).ToList();
                return EndpointResults.From(await records.CreateAsync(current.TenantId,
                    new CreateServiceRecordCommand(storeId, customerId, serviceOrderId, occurredAtUtc,
                        form["conditionNotes"], form["serviceContent"], form["followUpNotes"], commandId,
                        current.Id, images), cancellationToken),
                    value => Results.Created($"/api/v1/customers/{customerId}/service-records/{value.Id}", value));
            }
            finally
            {
                foreach (var stream in streams) await stream.DisposeAsync();
            }
        }).RequireAuthorization(policy => policy.RequireRole(ServiceRecordOperators))
            .RequireRateLimiting("file-upload")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(32 * 1024 * 1024));

        group.MapGet("/{customerId:guid}/service-record-files/{fileId:guid}", async (Guid customerId, Guid fileId,
            Guid storeId, HttpResponse response, IIdentityService identity, IServiceRecordService records,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            var result = await records.ReadImageAsync(current.TenantId, storeId, customerId, fileId,
                cancellationToken);
            if (!result.IsSuccess || result.Value is null) return EndpointResults.From(result);
            response.Headers.CacheControl = "private, no-store";
            return Results.File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: false);
        }).RequireAuthorization(policy => policy.RequireRole(ServiceRecordOperators));

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);

    private sealed record CreateCustomerRequest(Guid StoreId, string? Name, string? Mobile, string? Gender,
        DateOnly? BirthDate, string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent, Guid CommandId);
    private sealed record CreateCardTypeRequest(string? Code, string? Name, int? ValidityDays, Guid CommandId);
    private sealed record OpenMembershipRequest(Guid StoreId, Guid CardTypeId, string? CardNo, string? Note, Guid CommandId);
}
