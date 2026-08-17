namespace Erp.Application.Common;

public sealed record ApplicationError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null);

public sealed record Result<T>(T? Value, ApplicationError? Error)
{
    public bool IsSuccess => Error is null;
}

public static class ResultFactory
{
    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result<T> Failure<T>(string code, string message) => new(default, new ApplicationError(code, message));
}
