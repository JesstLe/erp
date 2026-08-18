using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
}
