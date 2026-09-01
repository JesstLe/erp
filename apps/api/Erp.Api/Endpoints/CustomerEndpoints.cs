using Erp.Application.Customers;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers").WithTags("Customers")
            .RequireAuthorization(SystemPermissions.CustomerRead);

        group.MapPost("/search", async (CustomerSearchRequest request, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            if (!Pagination.TryNormalize(request.Page, request.PageSize, out var page, out var pageSize))
                return InvalidPagination();
            return Results.Ok(await customers.SearchAsync(current.TenantId, request.StoreId, request.Query, page,
                pageSize,
                cancellationToken));
        }).RequireRateLimiting("customer-search");

        group.MapGet("/{customerId:guid}", async (Guid customerId, Guid storeId, IIdentityService identity,
            ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            var includeFinancialDetails = current.Permissions.Contains(SystemPermissions.MembershipManage) ||
                                          current.Permissions.Contains(SystemPermissions.CashierCheckout);
            return EndpointResults.From(await customers.GetAsync(current.TenantId, storeId, customerId,
                includeFinancialDetails, cancellationToken));
        });

        group.MapPost("/{customerId:guid}/mobile/reveal", async (Guid customerId,
            RevealCustomerMobileRequest request, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.RevealMobileAsync(current.TenantId,
                new RevealCustomerMobileCommand(request.StoreId, customerId, request.Purpose ?? string.Empty,
                    request.CommandId, current.Id), cancellationToken));
        })
            .RequireRateLimiting("customer-search");

        group.MapPost("/export", async (ExportCustomersRequest request, HttpResponse response,
            IIdentityService identity, ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            var canExportFullMobile = current.Permissions.Contains(SystemPermissions.CustomerExportFullMobile);
            var canExportAllStores = current.Permissions.Contains(SystemPermissions.OrganizationManage);
            var result = await customers.ExportAsync(current.TenantId,
                new ExportCustomersCommand(request.StoreId, request.Query, request.IncludeFullMobile,
                    canExportFullMobile, canExportAllStores, request.Purpose ?? string.Empty, request.CommandId,
                    current.Id),
                cancellationToken);
            response.Headers.CacheControl = "private, no-store";
            return EndpointResults.From(result, value => Results.File(value.Content,
                "text/csv; charset=utf-8", value.FileName, enableRangeProcessing: false));
        }).RequireAuthorization(SystemPermissions.CustomerExport)
            .RequireRateLimiting("customer-search");

        group.MapPost("", async (CreateCustomerRequest request, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.CreateAsync(current.TenantId,
                new CreateCustomerCommand(request.StoreId, request.Name ?? string.Empty, request.Mobile ?? string.Empty,
                    request.Gender, request.BirthDate, request.Residence, request.SourceCode, request.ServiceNotificationConsent,
                    request.MarketingConsent, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.CustomerWrite);

        group.MapPut("/{customerId:guid}", async (Guid customerId, UpdateCustomerRequest request,
            IIdentityService identity, ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.UpdateAsync(current.TenantId,
                new UpdateCustomerCommand(request.StoreId, customerId, request.Name ?? string.Empty,
                    request.Mobile ?? string.Empty, request.Gender, request.BirthDate, request.Residence, request.SourceCode,
                    request.ServiceNotificationConsent, request.MarketingConsent, request.ExpectedVersion,
                    request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.CustomerManage);

        group.MapPost("/{customerId:guid}/status", async (Guid customerId, ChangeCustomerStatusRequest request,
            IIdentityService identity, ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.ChangeStatusAsync(current.TenantId,
                new ChangeCustomerStatusCommand(request.StoreId, customerId, request.Restore,
                    request.Reason ?? string.Empty, request.ExpectedVersion, request.CommandId, current.Id),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CustomerManage);

        group.MapPost("/{sourceCustomerId:guid}/merge-preview", async (Guid sourceCustomerId,
            PreviewCustomerMergeRequest request, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.PreviewMergeAsync(current.TenantId,
                new PreviewCustomerMergeCommand(request.StoreId, sourceCustomerId, request.TargetCustomerId),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CustomerMerge)
            .RequireRateLimiting("customer-search");

        group.MapPost("/{sourceCustomerId:guid}/merge", async (Guid sourceCustomerId,
            MergeCustomerRequest request, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.MergeAsync(current.TenantId,
                new MergeCustomerCommand(request.StoreId, sourceCustomerId, request.TargetCustomerId,
                    request.ExpectedSourceVersion, request.ExpectedTargetVersion, request.Reason ?? string.Empty,
                    request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.CustomerMerge);

        group.MapGet("/membership/card-types", async (IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await customers.ListCardTypesAsync(current.TenantId, cancellationToken));
        });

        group.MapPost("/membership/card-types", async (CreateCardTypeRequest request, IIdentityService identity,
            ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await customers.CreateCardTypeAsync(current.TenantId,
                new CreateMemberCardTypeCommand(request.Name ?? string.Empty,
                    request.ValidityDays, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipCardTypeManage);

        group.MapPost("/{customerId:guid}/membership", async (Guid customerId, OpenMembershipRequest request,
            IIdentityService identity, ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.OpenMembershipAsync(current.TenantId,
                new OpenMembershipCommand(request.StoreId, customerId, request.CardTypeId, request.CardNo,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipOpen);

        group.MapGet("/service-record-categories", async (IIdentityService identity, IServiceRecordService records,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() :
                Results.Ok(await records.ListCategoriesAsync(current.TenantId, cancellationToken));
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

        group.MapPost("/service-record-categories", async (CreateServiceRecordCategoryRequest request,
            IIdentityService identity, IServiceRecordService records, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(
                await records.CreateCategoryAsync(current.TenantId, request.Name ?? string.Empty,
                    request.SortOrder, current.Id, cancellationToken),
                value => Results.Created($"/api/v1/customers/service-record-categories/{value.Id}", value));
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

        group.MapPut("/service-record-categories/{categoryId:guid}", async (Guid categoryId,
            UpdateServiceRecordCategoryRequest request, IIdentityService identity, IServiceRecordService records,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(
                await records.UpdateCategoryAsync(current.TenantId, categoryId, request.Name ?? string.Empty,
                    request.SortOrder, request.IsEnabled, request.ExpectedVersion, current.Id, cancellationToken));
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

        group.MapDelete("/service-record-categories/{categoryId:guid}", async (Guid categoryId,
            uint expectedVersion, IIdentityService identity, IServiceRecordService records,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(
                await records.DeleteCategoryAsync(current.TenantId, categoryId, expectedVersion, current.Id,
                    cancellationToken), _ => Results.NoContent());
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

        group.MapGet("/service-record-overview", async (Guid storeId, Guid? categoryId, string? query,
            int? page, int? pageSize, IIdentityService identity, IServiceRecordService records,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return InvalidPagination();
            return Results.Ok(await records.ListOverviewAsync(current.TenantId, storeId, categoryId, query,
                normalizedPage, normalizedPageSize, cancellationToken));
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

        group.MapGet("/{customerId:guid}/service-records", async (Guid customerId, Guid storeId,
            int? page, int? pageSize,
            IIdentityService identity, IServiceRecordService records, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return InvalidPagination();
            return Results.Ok(await records.ListAsync(current.TenantId, storeId, customerId, normalizedPage,
                normalizedPageSize, cancellationToken));
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

        group.MapGet("/{customerId:guid}/service-record-order-options", async (Guid customerId, Guid storeId,
            IIdentityService identity, IServiceRecordService records, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await records.ListOrderOptionsAsync(current.TenantId, storeId, customerId,
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

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
            Guid? categoryId = Guid.TryParse(form["categoryId"], out var parsedCategoryId) ? parsedCategoryId : null;
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
                    new CreateServiceRecordCommand(storeId, customerId, serviceOrderId, categoryId, occurredAtUtc,
                        form["conditionNotes"], form["serviceContent"], form["followUpNotes"], commandId,
                        current.Id, images), cancellationToken),
                    value => Results.Created($"/api/v1/customers/{customerId}/service-records/{value.Id}", value));
            }
            finally
            {
                foreach (var stream in streams) await stream.DisposeAsync();
            }
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage)
            .RequireRateLimiting("file-upload")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(32 * 1024 * 1024));

        group.MapPost("/{customerId:guid}/service-records/{recordId:guid}/corrections", async (
            Guid customerId, Guid recordId, CorrectServiceRecordRequest request, IIdentityService identity,
            IServiceRecordService records, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await records.CorrectAsync(current.TenantId,
                new CorrectServiceRecordCommand(request.StoreId, customerId, recordId,
                    request.Reason ?? string.Empty, request.ConditionNotes, request.ServiceContent,
                    request.FollowUpNotes, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

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
        }).RequireAuthorization(SystemPermissions.ServiceRecordManage);

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);

    private static IResult InvalidPagination() => Results.UnprocessableEntity(new
    {
        error = new { code = "INVALID_PAGINATION", message = "页码必须大于0，每页数量必须为1到100" },
    });

    private sealed record CustomerSearchRequest(Guid StoreId, string? Query, int? Page, int? PageSize);
    private sealed record CorrectServiceRecordRequest(Guid StoreId, string? Reason, string? ConditionNotes,
        string? ServiceContent, string? FollowUpNotes, Guid CommandId);
    private sealed record CreateServiceRecordCategoryRequest(string? Name, int SortOrder);
    private sealed record UpdateServiceRecordCategoryRequest(string? Name, int SortOrder, bool IsEnabled,
        uint ExpectedVersion);
    private sealed record CreateCustomerRequest(Guid StoreId, string? Name, string? Mobile, string? Gender,
        DateOnly? BirthDate, string? Residence, string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent, Guid CommandId);
    private sealed record UpdateCustomerRequest(Guid StoreId, string? Name, string? Mobile, string? Gender,
        DateOnly? BirthDate, string? Residence, string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent,
        uint ExpectedVersion, Guid CommandId);
    private sealed record ChangeCustomerStatusRequest(Guid StoreId, bool Restore, string? Reason,
        uint ExpectedVersion, Guid CommandId);
    private sealed record PreviewCustomerMergeRequest(Guid StoreId, Guid TargetCustomerId);
    private sealed record MergeCustomerRequest(Guid StoreId, Guid TargetCustomerId,
        uint ExpectedSourceVersion, uint ExpectedTargetVersion, string? Reason, Guid CommandId);
    private sealed record CreateCardTypeRequest(string? Name, int? ValidityDays, Guid CommandId);
    private sealed record OpenMembershipRequest(Guid StoreId, Guid CardTypeId, string? CardNo, string? Note, Guid CommandId);
    private sealed record RevealCustomerMobileRequest(Guid StoreId, string? Purpose, Guid CommandId);
    private sealed record ExportCustomersRequest(Guid StoreId, string? Query, bool IncludeFullMobile,
        string? Purpose, Guid CommandId);
}
