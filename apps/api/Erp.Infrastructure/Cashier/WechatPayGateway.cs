using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Domain.Cashier;

namespace Erp.Infrastructure.Cashier;

internal sealed class WechatPayGateway(HttpClient httpClient, TimeProvider clock) : IPaymentChannelGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public PaymentChannelProvider Provider => PaymentChannelProvider.WeChatPay;

    public async Task<PaymentChannelQrResult> CreateQrAsync(PaymentChannelCredentialProfile credentials,
        PaymentChannelCreateRequest request, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            appid = credentials.AppId,
            mchid = credentials.MerchantId,
            description = request.Subject,
            out_trade_no = request.OutTradeNo,
            time_expire = request.ExpiresAtUtc.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            notify_url = credentials.NotifyUrl,
            amount = new { total = request.AmountMinor, currency = "CNY" },
        }, JsonOptions);
        var response = await SendAsync(credentials, HttpMethod.Post, "/v3/pay/transactions/native", body,
            cancellationToken);
        if (!response.IsTrusted)
            return new PaymentChannelQrResult(false, null, response.ErrorCode, response.ErrorMessage);
        if (!response.IsSuccess)
            return new PaymentChannelQrResult(false, null, response.ErrorCode, response.ErrorMessage);
        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var codeUrl = document.RootElement.GetProperty("code_url").GetString();
            return string.IsNullOrWhiteSpace(codeUrl)
                ? new PaymentChannelQrResult(false, null, "CHANNEL_INVALID_RESPONSE", "微信支付未返回付款码")
                : new PaymentChannelQrResult(true, codeUrl, null, null);
        }
        catch (JsonException)
        {
            return new PaymentChannelQrResult(false, null, "CHANNEL_INVALID_RESPONSE", "微信支付返回格式无效");
        }
    }

    public async Task<PaymentChannelQueryResult> QueryAsync(PaymentChannelCredentialProfile credentials,
        string outTradeNo, CancellationToken cancellationToken)
    {
        var path = $"/v3/pay/transactions/out-trade-no/{Uri.EscapeDataString(outTradeNo)}?mchid={Uri.EscapeDataString(credentials.MerchantId!)}";
        var response = await SendAsync(credentials, HttpMethod.Get, path, string.Empty, cancellationToken);
        if (!response.IsTrusted || !response.IsSuccess)
            return new PaymentChannelQueryResult(false, PaymentChannelTradeState.Unknown, null, null, null,
                response.ErrorCode, response.ErrorMessage);
        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            if (!MatchesMerchant(credentials, root) ||
                !string.Equals(root.GetProperty("out_trade_no").GetString(), outTradeNo, StringComparison.Ordinal))
                return new PaymentChannelQueryResult(false, PaymentChannelTradeState.Unknown, null, null, null,
                    "CHANNEL_RESULT_CONFLICT", "微信支付查单结果与本地商户或订单不一致");
            var state = MapTradeState(root.GetProperty("trade_state").GetString());
            long? amount = root.TryGetProperty("amount", out var amountElement) &&
                amountElement.TryGetProperty("total", out var totalElement) ? totalElement.GetInt64() : null;
            DateTimeOffset? paidAt = root.TryGetProperty("success_time", out var paidElement) &&
                DateTimeOffset.TryParse(paidElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsedPaidAt) ? parsedPaidAt : null;
            return new PaymentChannelQueryResult(true, state,
                root.TryGetProperty("transaction_id", out var tradeElement) ? tradeElement.GetString() : null,
                amount, paidAt, null, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return new PaymentChannelQueryResult(false, PaymentChannelTradeState.Unknown, null, null, null,
                "CHANNEL_INVALID_RESPONSE", "微信支付查单返回格式无效");
        }
    }

    public async Task<PaymentChannelCloseResult> CloseAsync(PaymentChannelCredentialProfile credentials,
        string outTradeNo, CancellationToken cancellationToken)
    {
        var path = $"/v3/pay/transactions/out-trade-no/{Uri.EscapeDataString(outTradeNo)}/close";
        var body = JsonSerializer.Serialize(new { mchid = credentials.MerchantId }, JsonOptions);
        var response = await SendAsync(credentials, HttpMethod.Post, path, body, cancellationToken);
        return response.IsTrusted && response.IsSuccess
            ? new PaymentChannelCloseResult(true, PaymentChannelTradeState.Closed, null, null)
            : new PaymentChannelCloseResult(false, PaymentChannelTradeState.Unknown, response.ErrorCode,
                response.ErrorMessage);
    }

    public async Task<PaymentChannelRefundResult> RefundAsync(PaymentChannelCredentialProfile credentials,
        PaymentChannelRefundRequest request, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            transaction_id = request.ProviderTradeNo,
            out_refund_no = request.OutRefundNo,
            reason = Utf8Prefix(request.Reason, 80),
            amount = new { refund = request.RefundAmountMinor, total = request.TotalAmountMinor, currency = "CNY" },
        }, JsonOptions);
        var response = await SendAsync(credentials, HttpMethod.Post, "/v3/refund/domestic/refunds", body,
            cancellationToken);
        return ParseRefundResponse(response, request);
    }

    public async Task<PaymentChannelRefundResult> QueryRefundAsync(
        PaymentChannelCredentialProfile credentials, PaymentChannelRefundRequest request,
        CancellationToken cancellationToken)
    {
        var path = $"/v3/refund/domestic/refunds/{Uri.EscapeDataString(request.OutRefundNo)}";
        var response = await SendAsync(credentials, HttpMethod.Get, path, string.Empty, cancellationToken);
        return ParseRefundResponse(response, request);
    }

    public async Task<PaymentChannelBillResult> DownloadBillAsync(
        PaymentChannelCredentialProfile credentials, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var path = $"/v3/bill/tradebill?bill_date={businessDate:yyyy-MM-dd}&bill_type=ALL";
        var linkResponse = await SendAsync(credentials, HttpMethod.Get, path, string.Empty, cancellationToken);
        if (!linkResponse.IsTrusted || !linkResponse.IsSuccess)
            return BillFailure(linkResponse.ErrorCode, linkResponse.ErrorMessage);
        try
        {
            using var document = JsonDocument.Parse(linkResponse.Body);
            var root = document.RootElement;
            var hashType = root.GetProperty("hash_type").GetString();
            var expectedHash = root.GetProperty("hash_value").GetString();
            var downloadUrl = root.GetProperty("download_url").GetString();
            if (!string.Equals(hashType, "SHA1", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(expectedHash) ||
                !TryWechatBillUri(downloadUrl, out var uri))
                return BillFailure("CHANNEL_BILL_INVALID_RESPONSE", "微信账单下载信息无效");
            var raw = await DownloadRawBillAsync(credentials, uri!, cancellationToken);
            if (!raw.IsSuccess || raw.Content is null)
                return BillFailure(raw.ErrorCode, raw.ErrorMessage);
            // WeChat publishes SHA-1 for this protocol-level file-integrity comparison.
            // It is not used for credentials or signatures; SHA-256 is retained for our audit record.
#pragma warning disable CA5350
            var actualHash = Convert.ToHexString(SHA1.HashData(raw.Content));
#pragma warning restore CA5350
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return BillFailure("CHANNEL_BILL_HASH_MISMATCH", "微信账单文件摘要校验失败");
            var content = new UTF8Encoding(false, true).GetString(raw.Content);
            var entries = PaymentChannelBillCsv.ParseWechat(content);
            return new PaymentChannelBillResult(true, entries, SHA256.HashData(raw.Content), null, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            FormatException or DecoderFallbackException or CryptographicException)
        {
            return BillFailure("CHANNEL_BILL_INVALID_RESPONSE", "微信账单格式无效");
        }
    }

    public PaymentChannelNotification VerifyNotification(PaymentChannelCredentialProfile credentials,
        PaymentChannelNotificationEnvelope notification)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(notification.Body));
        try
        {
            var timestamp = Header(notification.Headers, "Wechatpay-Timestamp");
            var nonce = Header(notification.Headers, "Wechatpay-Nonce");
            var signature = Header(notification.Headers, "Wechatpay-Signature");
            var keyId = Header(notification.Headers, "Wechatpay-Serial");
            if (!string.Equals(keyId, credentials.CounterpartyPublicKeyId, StringComparison.Ordinal) ||
                !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds) ||
                Math.Abs((clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).TotalMinutes) > 5)
                return Invalid(digest, "CHANNEL_NOTIFICATION_STALE_OR_KEY_MISMATCH");
            var publicKey = File.ReadAllText(credentials.CounterpartyPublicKeyPath);
            if (!WechatPayV3Crypto.VerifyResponse(publicKey, timestamp, nonce, notification.Body, signature))
                return Invalid(digest, "CHANNEL_SIGNATURE_INVALID");

            using var envelope = JsonDocument.Parse(notification.Body);
            var root = envelope.RootElement;
            var eventId = root.GetProperty("id").GetString();
            var eventType = root.GetProperty("event_type").GetString();
            var resource = root.GetProperty("resource");
            if (!string.Equals(resource.GetProperty("algorithm").GetString(), "AEAD_AES_256_GCM",
                    StringComparison.Ordinal))
                return Invalid(digest, "CHANNEL_ENCRYPTION_UNSUPPORTED");
            var plaintext = WechatPayV3Crypto.DecryptNotification(credentials.ApiV3Key!,
                resource.GetProperty("nonce").GetString()!, resource.GetProperty("associated_data").GetString() ?? "",
                resource.GetProperty("ciphertext").GetString()!);
            using var transaction = JsonDocument.Parse(plaintext);
            var payload = transaction.RootElement;
            if (!MatchesMerchant(credentials, payload)) return Invalid(digest, "CHANNEL_MERCHANT_MISMATCH");
            var state = MapTradeState(payload.GetProperty("trade_state").GetString());
            var amount = payload.GetProperty("amount").GetProperty("total").GetInt64();
            DateTimeOffset? paidAt = payload.TryGetProperty("success_time", out var paidElement) &&
                DateTimeOffset.TryParse(paidElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsedPaidAt) ? parsedPaidAt : null;
            return new PaymentChannelNotification(true, eventId, eventType,
                payload.GetProperty("out_trade_no").GetString(),
                payload.TryGetProperty("transaction_id", out var tradeElement) ? tradeElement.GetString() : null,
                state, amount, paidAt, digest, null);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException or
            InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return Invalid(digest, "CHANNEL_NOTIFICATION_INVALID");
        }
    }

    private async Task<SignedResponse> SendAsync(PaymentChannelCredentialProfile credentials, HttpMethod method,
        string pathAndQuery, string body, CancellationToken cancellationToken)
    {
        var privateKey = await File.ReadAllTextAsync(credentials.MerchantPrivateKeyPath, cancellationToken);
        using var request = new HttpRequestMessage(method, pathAndQuery);
        if (body.Length > 0)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var authorization = WechatPayV3Crypto.CreateAuthorization(method.Method, pathAndQuery, body,
            credentials.MerchantId!, credentials.MerchantCertificateSerial!, privateKey, clock.GetUtcNow());
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!TryHeader(response, "Wechatpay-Timestamp", out var timestamp) ||
                !TryHeader(response, "Wechatpay-Nonce", out var nonce) ||
                !TryHeader(response, "Wechatpay-Signature", out var signature) ||
                !TryHeader(response, "Wechatpay-Serial", out var keyId))
                return new SignedResponse(false, false, responseBody, "CHANNEL_SIGNATURE_MISSING",
                    "微信支付响应缺少验签头");
            if (!string.Equals(keyId, credentials.CounterpartyPublicKeyId, StringComparison.Ordinal))
                return new SignedResponse(false, false, responseBody, "CHANNEL_KEY_MISMATCH",
                    "微信支付响应密钥标识不匹配");
            if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds) ||
                Math.Abs((clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).TotalMinutes) > 5)
                return new SignedResponse(false, false, responseBody, "CHANNEL_RESPONSE_STALE",
                    "微信支付响应时间戳超出允许范围");
            var publicKey = await File.ReadAllTextAsync(credentials.CounterpartyPublicKeyPath, cancellationToken);
            if (!WechatPayV3Crypto.VerifyResponse(publicKey, timestamp, nonce, responseBody, signature))
                return new SignedResponse(false, false, responseBody, "CHANNEL_SIGNATURE_INVALID",
                    "微信支付响应验签失败");
            if (response.IsSuccessStatusCode)
                return new SignedResponse(true, true, responseBody, null, null);
            var (code, message) = ParseError(responseBody, response.StatusCode);
            return new SignedResponse(true, false, responseBody, code, message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or
            CryptographicException)
        {
            return new SignedResponse(false, false, string.Empty, "CHANNEL_UNAVAILABLE", "微信支付暂时不可用");
        }
    }

    private async Task<RawBillDownload> DownloadRawBillAsync(PaymentChannelCredentialProfile credentials,
        Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var privateKey = await File.ReadAllTextAsync(credentials.MerchantPrivateKeyPath, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("Authorization", WechatPayV3Crypto.CreateAuthorization(
                HttpMethod.Get.Method, uri.PathAndQuery, string.Empty, credentials.MerchantId!,
                credentials.MerchantCertificateSerial!, privateKey, clock.GetUtcNow()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new RawBillDownload(false, null, "CHANNEL_BILL_DOWNLOAD_FAILED",
                    "微信账单文件下载失败");
            const int maximumBytes = 20 * 1024 * 1024;
            if (response.Content.Headers.ContentLength is > maximumBytes)
                return new RawBillDownload(false, null, "CHANNEL_BILL_TOO_LARGE", "微信账单文件超过20MB限制");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var target = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (target.Length + read > maximumBytes)
                    return new RawBillDownload(false, null, "CHANNEL_BILL_TOO_LARGE",
                        "微信账单文件超过20MB限制");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return new RawBillDownload(true, target.ToArray(), null, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            IOException or CryptographicException)
        {
            return new RawBillDownload(false, null, "CHANNEL_UNAVAILABLE", "微信账单服务暂时不可用");
        }
    }

    private static bool MatchesMerchant(PaymentChannelCredentialProfile credentials, JsonElement root) =>
        root.TryGetProperty("mchid", out var merchant) &&
        string.Equals(merchant.GetString(), credentials.MerchantId, StringComparison.Ordinal) &&
        root.TryGetProperty("appid", out var app) &&
        string.Equals(app.GetString(), credentials.AppId, StringComparison.Ordinal);

    private static PaymentChannelTradeState MapTradeState(string? state) => state switch
    {
        "SUCCESS" => PaymentChannelTradeState.Paid,
        "NOTPAY" or "USERPAYING" => PaymentChannelTradeState.Pending,
        "CLOSED" or "REVOKED" => PaymentChannelTradeState.Closed,
        "PAYERROR" => PaymentChannelTradeState.Failed,
        _ => PaymentChannelTradeState.Unknown,
    };

    private static PaymentChannelRefundResult ParseRefundResponse(SignedResponse response,
        PaymentChannelRefundRequest request)
    {
        if (!response.IsTrusted || !response.IsSuccess)
            return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                response.ErrorCode, response.ErrorMessage);
        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            var amount = root.GetProperty("amount");
            var refundAmount = amount.GetProperty("refund").GetInt64();
            var totalAmount = amount.GetProperty("total").GetInt64();
            if (!string.Equals(root.GetProperty("out_refund_no").GetString(), request.OutRefundNo,
                    StringComparison.Ordinal) || refundAmount != request.RefundAmountMinor ||
                totalAmount != request.TotalAmountMinor)
                return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                    "CHANNEL_RESULT_CONFLICT", "微信退款结果与本地退款单或金额不一致");
            var state = root.GetProperty("status").GetString() switch
            {
                "SUCCESS" => PaymentChannelRefundState.Succeeded,
                "PROCESSING" => PaymentChannelRefundState.Pending,
                "CLOSED" or "ABNORMAL" => PaymentChannelRefundState.Failed,
                _ => PaymentChannelRefundState.Unknown,
            };
            return new PaymentChannelRefundResult(true, state,
                root.TryGetProperty("refund_id", out var refundId) ? refundId.GetString() : null,
                refundAmount, null, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return new PaymentChannelRefundResult(false, PaymentChannelRefundState.Unknown, null, null,
                "CHANNEL_INVALID_RESPONSE", "微信退款返回格式无效");
        }
    }

    private static string Utf8Prefix(string value, int maximumBytes)
    {
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.Trim().EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maximumBytes) break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }

    private static bool TryWechatBillUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            parsed.UserInfo.Length > 0 || parsed.Host is not ("api.mch.weixin.qq.com" or
                "api2.mch.weixin.qq.com"))
            return false;
        uri = parsed;
        return true;
    }

    private static PaymentChannelBillResult BillFailure(string? code, string? message) =>
        new(false, [], null, code ?? "CHANNEL_BILL_UNAVAILABLE", message ?? "微信账单暂时不可用");

    private static PaymentChannelNotification Invalid(byte[] digest, string code) =>
        new(false, null, null, null, null, PaymentChannelTradeState.Unknown, null, null, digest, code);

    private static string Header(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase)).Value
        ?? throw new KeyNotFoundException(name);

    private static bool TryHeader(HttpResponseMessage response, string name, out string value)
    {
        value = response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() ?? string.Empty : string.Empty;
        return value.Length > 0;
    }

    private static (string Code, string Message) ParseError(string body, HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var code = document.RootElement.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString() : null;
            var message = document.RootElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() : null;
            return ($"WECHAT_{code ?? ((int)status).ToString(CultureInfo.InvariantCulture)}",
                message ?? "微信支付请求失败");
        }
        catch (JsonException)
        {
            return ($"WECHAT_{(int)status}", "微信支付请求失败");
        }
    }

    private sealed record SignedResponse(bool IsTrusted, bool IsSuccess, string Body, string? ErrorCode,
        string? ErrorMessage);
    private sealed record RawBillDownload(bool IsSuccess, byte[]? Content, string? ErrorCode,
        string? ErrorMessage);
}

internal static class WechatPayV3Crypto
{
    public static string CreateAuthorization(string method, string pathAndQuery, string body, string merchantId,
        string certificateSerial, string privateKeyPem, DateTimeOffset now, string? nonce = null)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        nonce ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var message = $"{method}\n{pathAndQuery}\n{timestamp}\n{nonce}\n{body}\n";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(message),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return $"WECHATPAY2-SHA256-RSA2048 mchid=\"{merchantId}\",nonce_str=\"{nonce}\"," +
            $"signature=\"{signature}\",timestamp=\"{timestamp}\",serial_no=\"{certificateSerial}\"";
    }

    public static bool VerifyResponse(string publicKeyPem, string timestamp, string nonce, string body,
        string signature)
    {
        var message = $"{timestamp}\n{nonce}\n{body}\n";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return rsa.VerifyData(Encoding.UTF8.GetBytes(message), Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public static string DecryptNotification(string apiV3Key, string nonce, string associatedData,
        string ciphertextBase64)
    {
        var key = Encoding.UTF8.GetBytes(apiV3Key);
        if (key.Length != 32) throw new CryptographicException("APIv3 key length is invalid");
        var combined = Convert.FromBase64String(ciphertextBase64);
        if (combined.Length <= 16) throw new CryptographicException("Ciphertext is invalid");
        var ciphertext = combined.AsSpan(0, combined.Length - 16);
        var tag = combined.AsSpan(combined.Length - 16, 16);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(Encoding.UTF8.GetBytes(nonce), ciphertext, tag, plaintext,
            Encoding.UTF8.GetBytes(associatedData));
        return Encoding.UTF8.GetString(plaintext);
    }
}
