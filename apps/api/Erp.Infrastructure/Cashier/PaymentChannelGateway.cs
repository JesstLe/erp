using Erp.Domain.Cashier;

namespace Erp.Infrastructure.Cashier;

internal enum PaymentChannelTradeState { Pending, Paid, Closed, Failed, Unknown }
internal enum PaymentChannelRefundState { Pending, Succeeded, Failed, Unknown }

internal sealed record PaymentChannelCreateRequest(string OutTradeNo, long AmountMinor, string Subject,
    DateTimeOffset ExpiresAtUtc);
internal sealed record PaymentChannelQrResult(bool IsSuccess, string? QrPayload, string? ErrorCode,
    string? ErrorMessage);
internal sealed record PaymentChannelQueryResult(bool IsSuccess, PaymentChannelTradeState State,
    string? ProviderTradeNo, long? AmountMinor, DateTimeOffset? PaidAtUtc, string? ErrorCode,
    string? ErrorMessage);
internal sealed record PaymentChannelCloseResult(bool IsSuccess, PaymentChannelTradeState State,
    string? ErrorCode, string? ErrorMessage);
internal sealed record PaymentChannelNotification(bool IsVerified, string? ProviderEventId, string? EventType,
    string? OutTradeNo, string? ProviderTradeNo, PaymentChannelTradeState State, long? AmountMinor,
    DateTimeOffset? PaidAtUtc, byte[] PayloadSha256, string? ErrorCode);
internal sealed record PaymentChannelNotificationEnvelope(IReadOnlyDictionary<string, string> Headers,
    string Body, IReadOnlyDictionary<string, string>? Form = null);
internal sealed record PaymentChannelRefundRequest(string OutTradeNo, string ProviderTradeNo,
    string OutRefundNo, long RefundAmountMinor, long TotalAmountMinor, string Reason);
internal sealed record PaymentChannelRefundResult(bool IsSuccess, PaymentChannelRefundState State,
    string? ProviderRefundNo, long? RefundAmountMinor, string? ErrorCode, string? ErrorMessage);
internal sealed record PaymentChannelBillEntry(PaymentChannelReconciliationItemType ItemType,
    string MatchKey, string? OutTradeNo, string? OutRefundNo, string? ProviderTradeNo,
    long AmountMinor, long FeeMinor, string ChannelStatus);
internal sealed record PaymentChannelBillResult(bool IsSuccess, IReadOnlyList<PaymentChannelBillEntry> Entries,
    byte[]? SourceSha256, string? ErrorCode, string? ErrorMessage);

internal interface IPaymentChannelGateway
{
    PaymentChannelProvider Provider { get; }
    Task<PaymentChannelQrResult> CreateQrAsync(PaymentChannelCredentialProfile credentials,
        PaymentChannelCreateRequest request, CancellationToken cancellationToken);
    Task<PaymentChannelQueryResult> QueryAsync(PaymentChannelCredentialProfile credentials, string outTradeNo,
        CancellationToken cancellationToken);
    Task<PaymentChannelCloseResult> CloseAsync(PaymentChannelCredentialProfile credentials, string outTradeNo,
        CancellationToken cancellationToken);
    Task<PaymentChannelRefundResult> RefundAsync(PaymentChannelCredentialProfile credentials,
        PaymentChannelRefundRequest request, CancellationToken cancellationToken);
    Task<PaymentChannelRefundResult> QueryRefundAsync(PaymentChannelCredentialProfile credentials,
        PaymentChannelRefundRequest request, CancellationToken cancellationToken);
    Task<PaymentChannelBillResult> DownloadBillAsync(PaymentChannelCredentialProfile credentials,
        DateOnly businessDate, CancellationToken cancellationToken);
    PaymentChannelNotification VerifyNotification(PaymentChannelCredentialProfile credentials,
        PaymentChannelNotificationEnvelope notification);
}

internal sealed class PaymentChannelGatewayRegistry(IEnumerable<IPaymentChannelGateway> gateways)
{
    private readonly Dictionary<PaymentChannelProvider, IPaymentChannelGateway> _gateways = gateways
        .ToDictionary(x => x.Provider);

    public IPaymentChannelGateway Get(PaymentChannelProvider provider) =>
        _gateways.TryGetValue(provider, out var gateway)
            ? gateway
            : throw new InvalidOperationException($"未注册 {provider} 支付渠道适配器");
}
