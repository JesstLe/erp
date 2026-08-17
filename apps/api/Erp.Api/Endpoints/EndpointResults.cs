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
            "VALIDATION_FAILED" or "DUPLICATE_CODE" => StatusCodes.Status422UnprocessableEntity,
            "INVALID_CREDENTIALS" => StatusCodes.Status401Unauthorized,
            "ACCOUNT_LOCKED" => StatusCodes.Status423Locked,
            "FORBIDDEN_ACTION" or "FORBIDDEN_DATA_SCOPE" => StatusCodes.Status403Forbidden,
            "NOT_FOUND" => StatusCodes.Status404NotFound,
            "VERSION_CONFLICT" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(new { error, traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() }, statusCode: status);
    }
}

