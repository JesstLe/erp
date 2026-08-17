using Erp.Application.Common;

namespace Erp.Api.Endpoints;

internal static class EndpointResults
{
    public static IResult From<T>(Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            return onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value);
        }

        var error = result.Error ?? new ApplicationError("UNKNOWN_ERROR", "请求失败");
        var status = error.Code switch
        {
            "VALIDATION_FAILED" or "DUPLICATE_CODE" or "DUPLICATE_FACILITY_GROUP" or "DUPLICATE_FACILITY_TYPE" or "DUPLICATE_FACILITY_CODE" or "DUPLICATE_MEMBER_CARD_TYPE" => StatusCodes.Status422UnprocessableEntity,
            "INVALID_CREDENTIALS" => StatusCodes.Status401Unauthorized,
            "ACCOUNT_LOCKED" => StatusCodes.Status423Locked,
            "FORBIDDEN_ACTION" or "FORBIDDEN_DATA_SCOPE" => StatusCodes.Status403Forbidden,
            "NOT_FOUND" or "FACILITY_NOT_FOUND" or "FACILITY_SESSION_NOT_FOUND" or "CLEANING_TASK_NOT_FOUND" or "CUSTOMER_NOT_FOUND" or "MEMBER_CARD_TYPE_NOT_FOUND" or "MEMBER_CARD_NOT_FOUND" or "MEMBER_ACCOUNT_NOT_FOUND" or "MEMBER_TOPUP_NOT_FOUND" or "SERVICE_ORDER_NOT_FOUND" or "PRICE_BOOK_NOT_FOUND" or "PAYMENT_NOT_FOUND" or "PAYMENT_METHOD_NOT_FOUND" or "SHIFT_NOT_FOUND" => StatusCodes.Status404NotFound,
            "VERSION_CONFLICT" or "IDEMPOTENCY_CONFLICT" or "FACILITY_NOT_AVAILABLE" or "MEMBER_CARD_NOT_ACTIVE" or "INVALID_STATE_TRANSITION" or "STATE_TRANSITION_NOT_ALLOWED" or "DUPLICATE_CUSTOMER" or "DUPLICATE_MEMBER_CARD" or "VISIT_ALREADY_HAS_ORDER" or "VISIT_NOT_READY" or "PAYMENT_ALREADY_EXISTS" or "SHIFT_NOT_OPEN" or "SHIFT_ALREADY_OPEN" => StatusCodes.Status409Conflict,
            "PAYMENT_ALLOCATION_UNBALANCED" or "PAYMENT_METHOD_NOT_ALLOWED" or "INSUFFICIENT_MEMBER_BALANCE" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(new { error, traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() }, statusCode: status);
    }
}
