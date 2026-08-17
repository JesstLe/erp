namespace Erp.Api;

internal static partial class ApiLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled request error. TraceId: {TraceId}")]
    public static partial void UnhandledRequest(ILogger logger, string traceId, Exception? exception);
}
