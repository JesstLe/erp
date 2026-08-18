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
            using var http = new HttpClient();
            var gateway = new AlipayGateway(http);

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
    public void WechatBillParserTreatsRefundRowAsRefundOnly()
    {
        var csv = "交易时间,微信订单号,商户订单号,交易状态,订单金额,手续费,商户退款单号,微信退款单号,申请退款金额,退款状态\n" +
                  "2026-08-17 09:00:00,WX-PAY-1,PAY-1,SUCCESS,500.00,0.30,,,,\n" +
                  "2026-08-17 10:00:00,WX-PAY-OLD,PAY-OLD,SUCCESS,500.00,0.30,RF-1,WX-RF-1,50.00,PROCESSING\n";

        var entries = PaymentChannelBillCsv.ParseWechat(csv);

        Assert.Collection(entries.OrderBy(x => x.MatchKey),
            payment =>
            {
                Assert.Equal("PAY:PAY-1", payment.MatchKey);
                Assert.Equal(50_000, payment.AmountMinor);
            },
            refund =>
            {
                Assert.Equal("REFUND:RF-1", refund.MatchKey);
                Assert.Equal(5_000, refund.AmountMinor);
            });
        Assert.DoesNotContain(entries, x => x.MatchKey == "PAY:PAY-OLD");
    }

    [Fact]
    public void AlipayBillParserHandlesQuotedValuesAndNegativeRefundAmounts()
    {
        var csv = "支付宝交易号,商户订单号,业务类型,订单金额（元）,商家实收（元）,服务费（元）,退款批次号/请求号,退款金额（元）\n" +
                  "ALI-PAY-1,PAY-1,交易,600.00,600.00,0.60,,\n" +
                  "ALI-PAY-1,PAY-1,\"退款,成功\",600.00,-50.00,-0.05,RF-1,-50.00\n";

        var entries = PaymentChannelBillCsv.ParseAlipay(csv);

        Assert.Equal(2, entries.Count);
        Assert.Equal(60_000, entries.Single(x => x.MatchKey == "PAY:PAY-1").AmountMinor);
        var refund = entries.Single(x => x.MatchKey == "REFUND:RF-1");
        Assert.Equal(5_000, refund.AmountMinor);
        Assert.Equal(5, refund.FeeMinor);
        Assert.Equal("退款,成功", refund.ChannelStatus);
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

    [Fact]
    public async Task WechatBillDownloadVerifiesSignedLinkAndProviderFileHash()
    {
        using var merchantRsa = RSA.Create(2048);
        using var platformRsa = RSA.Create(2048);
        var merchantKeyPath = Path.GetTempFileName();
        var platformKeyPath = Path.GetTempFileName();
        var now = new DateTimeOffset(2026, 8, 18, 2, 30, 0, TimeSpan.Zero);
        const string platformKeyId = "PLATFORM_KEY_20260818";
        var csv = "交易时间,微信订单号,商户订单号,交易状态,订单金额,手续费,商户退款单号,微信退款单号,申请退款金额,退款状态\n" +
                  "2026-08-17 09:00:00,WX-1,PAY-1,SUCCESS,123.00,0.12,,,,\n";
        var billBytes = Encoding.UTF8.GetBytes(csv);
#pragma warning disable CA5350
        var sha1 = Convert.ToHexString(SHA1.HashData(billBytes));
#pragma warning restore CA5350
        try
        {
            await File.WriteAllTextAsync(merchantKeyPath, merchantRsa.ExportRSAPrivateKeyPem());
            await File.WriteAllTextAsync(platformKeyPath, platformRsa.ExportSubjectPublicKeyInfoPem());
            var handler = new WechatBillHandler(platformRsa, platformKeyId, now, billBytes, sha1);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mch.weixin.qq.com") };
            var gateway = new WechatPayGateway(http, new FixedTimeProvider(now));
            var credentials = new PaymentChannelCredentialProfile(PaymentChannelProvider.WeChatPay,
                "TEST_WECHAT", "wx2026000000000001", "1900000001", "MERCHANT_CERT_20260818",
                "12345678901234567890123456789012", merchantKeyPath, platformKeyPath, platformKeyId,
                "https://erp.example.test/api/integrations/payment-notifications/wechat", null);

            var result = await gateway.DownloadBillAsync(credentials, new DateOnly(2026, 8, 17),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            var entry = Assert.Single(result.Entries);
            Assert.Equal("PAY:PAY-1", entry.MatchKey);
            Assert.Equal(12_300, entry.AmountMinor);
            Assert.Equal(SHA256.HashData(billBytes), result.SourceSha256);
            Assert.Contains("WECHATPAY2-SHA256-RSA2048", handler.LinkAuthorization,
                StringComparison.Ordinal);
            Assert.Contains("WECHATPAY2-SHA256-RSA2048", handler.DownloadAuthorization,
                StringComparison.Ordinal);
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

    private sealed class WechatBillHandler(RSA platformRsa, string keyId, DateTimeOffset now,
        byte[] billBytes, string sha1) : HttpMessageHandler
    {
        public string LinkAuthorization { get; private set; } = string.Empty;
        public string DownloadAuthorization { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/v3/billdownload/file")
            {
                DownloadAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(billBytes),
                });
            }

            LinkAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            var body = $$"""
                {"hash_type":"SHA1","hash_value":"{{sha1}}","download_url":"https://api.mch.weixin.qq.com/v3/billdownload/file?token=test"}
                """;
            var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            const string nonce = "bill-response-nonce";
            var signature = Convert.ToBase64String(platformRsa.SignData(
                Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{body}\n"), HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Wechatpay-Timestamp", timestamp);
            response.Headers.TryAddWithoutValidation("Wechatpay-Nonce", nonce);
            response.Headers.TryAddWithoutValidation("Wechatpay-Signature", signature);
            response.Headers.TryAddWithoutValidation("Wechatpay-Serial", keyId);
            return Task.FromResult(response);
        }
    }
}
