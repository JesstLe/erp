using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Domain.Cashier;
using Erp.Infrastructure.Cashier;

namespace Erp.Api.IntegrationTests;

public sealed class PaymentChannelCryptographyTests
{
    [Fact]
    public void WechatRequestAuthorizationUsesDocumentedCanonicalMessage()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportRSAPrivateKeyPem();
        var body = "{\"amount\":{\"total\":12300}}";
        var now = DateTimeOffset.FromUnixTimeSeconds(1_776_124_800);
        const string nonce = "0123456789abcdef0123456789abcdef";

        var authorization = WechatPayV3Crypto.CreateAuthorization("POST", "/v3/pay/transactions/native", body,
            "1900000001", "7777777777777777777777777777777777777777", privateKey, now, nonce);

        var signature = Extract(authorization, "signature");
        var message = $"POST\n/v3/pay/transactions/native\n{now.ToUnixTimeSeconds()}\n{nonce}\n{body}\n";
        Assert.True(rsa.VerifyData(Encoding.UTF8.GetBytes(message), Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Equal("1900000001", Extract(authorization, "mchid"));
    }

    [Fact]
    public void WechatNotificationAesGcmDecryptsOnlyWithMatchingContext()
    {
        const string apiV3Key = "12345678901234567890123456789012";
        const string nonce = "0123456789ab";
        const string associated = "transaction";
        const string plaintext = "{\"out_trade_no\":\"PAY-001\"}";
        var key = Encoding.UTF8.GetBytes(apiV3Key);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(Encoding.UTF8.GetBytes(nonce), plainBytes, cipher, tag,
                Encoding.UTF8.GetBytes(associated));
        var combined = cipher.Concat(tag).ToArray();

        var decrypted = WechatPayV3Crypto.DecryptNotification(apiV3Key, nonce, associated,
            Convert.ToBase64String(combined));

        Assert.Equal(plaintext, decrypted);
        Assert.ThrowsAny<CryptographicException>(() => WechatPayV3Crypto.DecryptNotification(apiV3Key, nonce,
            "different", Convert.ToBase64String(combined)));
    }

    [Fact]
    public void AlipayNotificationMustMatchSignatureAppAndAmount()
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(publicKeyPath, Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()));
            var form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["app_id"] = "2026000000000001",
                ["notify_id"] = "notify-001",
                ["out_trade_no"] = "PAY-001",
                ["trade_no"] = "2026081822001000000001",
                ["trade_status"] = "TRADE_SUCCESS",
                ["total_amount"] = "123.00",
                ["gmt_payment"] = "2026-08-18 09:00:00",
                ["sign_type"] = "RSA2",
            };
            var signContent = string.Join('&', form.Where(x => x.Key != "sign" && x.Key != "sign_type")
                .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
            form["sign"] = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(signContent),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var credentials = new PaymentChannelCredentialProfile(PaymentChannelProvider.Alipay, "TEST_ALIPAY",
                "2026000000000001", null, null, null, publicKeyPath, publicKeyPath, null,
                "https://erp.example.test/api/integrations/payment-notifications/alipay",
                "https://openapi.alipay.com/gateway.do");
            var gateway = new AlipayGateway();

            var verified = gateway.VerifyNotification(credentials,
                new PaymentChannelNotificationEnvelope(new Dictionary<string, string>(), "signed-form", form));

            Assert.True(verified.IsVerified);
            Assert.Equal(PaymentChannelTradeState.Paid, verified.State);
            Assert.Equal(12_300, verified.AmountMinor);
            form["total_amount"] = "1.00";
            var tampered = gateway.VerifyNotification(credentials,
                new PaymentChannelNotificationEnvelope(new Dictionary<string, string>(), "tampered-form", form));
            Assert.False(tampered.IsVerified);
            Assert.Equal("CHANNEL_SIGNATURE_INVALID", tampered.ErrorCode);
        }
        finally
        {
            File.Delete(publicKeyPath);
        }
    }

    [Fact]
    public async Task WechatRefundUsesStableMerchantRefundNumberAndRequiresSignedMatchingResponse()
    {
        using var merchantRsa = RSA.Create(2048);
        using var platformRsa = RSA.Create(2048);
        var merchantKeyPath = Path.GetTempFileName();
        var platformKeyPath = Path.GetTempFileName();
        var now = new DateTimeOffset(2026, 8, 18, 9, 30, 0, TimeSpan.Zero);
        const string platformKeyId = "PLATFORM_KEY_20260818";
        const string responseNonce = "refund-response-nonce";
        try
        {
            await File.WriteAllTextAsync(merchantKeyPath, merchantRsa.ExportRSAPrivateKeyPem());
            await File.WriteAllTextAsync(platformKeyPath, platformRsa.ExportSubjectPublicKeyInfoPem());
            var responseBody = """
                {"refund_id":"503001202608180001","out_refund_no":"RF202608180006","status":"SUCCESS","amount":{"total":12300,"refund":2300,"currency":"CNY"}}
                """;
            var handler = new SignedWechatResponseHandler(platformRsa, platformKeyId, now, responseNonce,
                responseBody);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mch.weixin.qq.com") };
            var gateway = new WechatPayGateway(http, new FixedTimeProvider(now));
            var credentials = new PaymentChannelCredentialProfile(PaymentChannelProvider.WeChatPay,
                "TEST_WECHAT", "wx2026000000000001", "1900000001", "MERCHANT_CERT_20260818",
                "12345678901234567890123456789012", merchantKeyPath, platformKeyPath, platformKeyId,
                "https://erp.example.test/api/integrations/payment-notifications/wechat", null);
            var request = new PaymentChannelRefundRequest("PAY202608180001-A1",
                "4200002026081800000001", "RF202608180006", 2_300, 12_300,
                string.Concat(Enumerable.Repeat("退", 26)) + "🙂");

            var result = await gateway.RefundAsync(credentials, request, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(PaymentChannelRefundState.Succeeded, result.State);
            Assert.Equal("503001202608180001", result.ProviderRefundNo);
            Assert.Equal(2_300, result.RefundAmountMinor);
            Assert.Equal(HttpMethod.Post, handler.RequestMethod);
            Assert.Equal("/v3/refund/domestic/refunds", handler.RequestPath);
            Assert.Contains("\"out_refund_no\":\"RF202608180006\"", handler.RequestBody,
                StringComparison.Ordinal);
            Assert.Contains("\"refund\":2300", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("WECHATPAY2-SHA256-RSA2048", handler.Authorization, StringComparison.Ordinal);
            using var sentBody = JsonDocument.Parse(handler.RequestBody);
            Assert.Equal(string.Concat(Enumerable.Repeat("退", 26)),
                sentBody.RootElement.GetProperty("reason").GetString());
        }
        finally
        {
            File.Delete(merchantKeyPath);
            File.Delete(platformKeyPath);
        }
    }

    private static string Extract(string authorization, string key)
    {
        var prefix = $"{key}=\"";
        var start = authorization.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += prefix.Length;
        var end = authorization.IndexOf('"', start);
        Assert.True(end > start);
        return authorization[start..end];
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SignedWechatResponseHandler(RSA platformRsa, string keyId, DateTimeOffset now,
        string nonce, string responseBody) : HttpMessageHandler
    {
        public HttpMethod? RequestMethod { get; private set; }
        public string? RequestPath { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public string Authorization { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestPath = request.RequestUri?.PathAndQuery;
            RequestBody = request.Content is null ? string.Empty :
                await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var message = $"{timestamp}\n{nonce}\n{responseBody}\n";
            var signature = Convert.ToBase64String(platformRsa.SignData(Encoding.UTF8.GetBytes(message),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Wechatpay-Timestamp", timestamp);
            response.Headers.TryAddWithoutValidation("Wechatpay-Nonce", nonce);
            response.Headers.TryAddWithoutValidation("Wechatpay-Signature", signature);
            response.Headers.TryAddWithoutValidation("Wechatpay-Serial", keyId);
            return response;
        }
    }
}
