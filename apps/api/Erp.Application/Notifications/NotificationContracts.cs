namespace Erp.Application.Notifications;

public sealed record NotificationItemDto(string Id, string Type, string Title, string Description,
    string Severity, string TargetUrl, DateTimeOffset OccurredAtUtc);

public sealed record NotificationInboxDto(int PendingCount, IReadOnlyList<NotificationItemDto> Items);

public interface INotificationService
{
    Task<NotificationInboxDto> GetInboxAsync(Guid tenantId, Guid storeId, Guid userId,
        IReadOnlyList<string> roles, CancellationToken cancellationToken);
}
