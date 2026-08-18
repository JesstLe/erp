using Erp.Domain.Cashier;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Cashier;

internal sealed record PaymentChannelCredentialProfile(PaymentChannelProvider Provider, string ProfileName,
    string AppId, string? MerchantId, string? MerchantCertificateSerial, string? ApiV3Key,
    string MerchantPrivateKeyPath, string CounterpartyPublicKeyPath, string? CounterpartyPublicKeyId,
    string NotifyUrl, string? GatewayUrl);

internal sealed record PaymentChannelCredentialReadiness(bool IsPresent, IReadOnlyList<string> Missing);

internal sealed class PaymentChannelCredentialResolver(IConfiguration configuration)
{
    public PaymentChannelCredentialReadiness Inspect(PaymentChannelProvider provider, string profile)
    {
        var section = configuration.GetSection($"PaymentChannels:Profiles:{profile}");
        var missing = new List<string>();
        var requiredValues = provider == PaymentChannelProvider.WeChatPay
            ? new[] { "AppId", "MerchantId", "MerchantCertificateSerial", "ApiV3Key", "PlatformPublicKeyId" }
            : new[] { "AppId" };
        foreach (var key in requiredValues)
            if (string.IsNullOrWhiteSpace(section[key])) missing.Add(key);
        foreach (var key in provider == PaymentChannelProvider.WeChatPay
                     ? new[] { "AppId", "MerchantId", "MerchantCertificateSerial", "PlatformPublicKeyId" }
                     : new[] { "AppId" })
            if (section[key] is { } token && !string.IsNullOrWhiteSpace(token) &&
                (token.Length > 64 || token.Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-')))
                missing.Add($"{key}(格式无效)");

        if (provider == PaymentChannelProvider.WeChatPay && section["ApiV3Key"] is { } apiV3Key &&
            !string.IsNullOrWhiteSpace(apiV3Key) && apiV3Key.Length != 32)
            missing.Add("ApiV3Key(必须32字符)");

        var requiredFiles = provider == PaymentChannelProvider.WeChatPay
            ? new[] { "MerchantPrivateKeyPath", "PlatformPublicKeyPath" }
            : new[] { "MerchantPrivateKeyPath", "AlipayPublicKeyPath" };
        foreach (var key in requiredFiles)
        {
            var path = section[key];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) missing.Add($"{key}(文件不存在)");
        }

        var requiredUrls = provider == PaymentChannelProvider.WeChatPay
            ? new[] { "NotifyUrl" }
            : new[] { "NotifyUrl", "GatewayUrl" };
        foreach (var key in requiredUrls)
        {
            var value = section[key];
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                missing.Add($"{key}(必须为HTTPS)");
        }
        if (provider == PaymentChannelProvider.Alipay &&
            Uri.TryCreate(section["GatewayUrl"], UriKind.Absolute, out var gateway) &&
            gateway.Host is not ("openapi.alipay.com" or "openapi-sandbox.dl.alipaydev.com"))
            missing.Add("GatewayUrl(只允许支付宝官方网关)");
        return new PaymentChannelCredentialReadiness(missing.Count == 0, missing.Distinct().ToList());
    }

    public bool TryResolve(PaymentChannelProvider provider, string profile,
        out PaymentChannelCredentialProfile? credentials, out IReadOnlyList<string> missing)
    {
        var readiness = Inspect(provider, profile);
        missing = readiness.Missing;
        if (!readiness.IsPresent)
        {
            credentials = null;
            return false;
        }
        var section = configuration.GetSection($"PaymentChannels:Profiles:{profile}");
        credentials = new PaymentChannelCredentialProfile(provider, profile, section["AppId"]!,
            section["MerchantId"], section["MerchantCertificateSerial"], section["ApiV3Key"],
            section["MerchantPrivateKeyPath"]!, provider == PaymentChannelProvider.WeChatPay
                ? section["PlatformPublicKeyPath"]! : section["AlipayPublicKeyPath"]!,
            provider == PaymentChannelProvider.WeChatPay ? section["PlatformPublicKeyId"] : null,
            section["NotifyUrl"]!, section["GatewayUrl"]);
        return true;
    }

    public static bool IsEnvironmentCompatible(PaymentChannelEnvironment environment,
        PaymentChannelCredentialProfile credentials, out string message)
    {
        if (credentials.Provider == PaymentChannelProvider.WeChatPay)
        {
            message = environment == PaymentChannelEnvironment.Production
                ? string.Empty : "微信支付 APIv3 当前不提供本系统可用的沙箱环境";
            return environment == PaymentChannelEnvironment.Production;
        }
        var expectedHost = environment == PaymentChannelEnvironment.Sandbox
            ? "openapi-sandbox.dl.alipaydev.com" : "openapi.alipay.com";
        var valid = Uri.TryCreate(credentials.GatewayUrl, UriKind.Absolute, out var gateway) &&
            string.Equals(gateway.Host, expectedHost, StringComparison.OrdinalIgnoreCase);
        message = valid ? string.Empty : $"支付宝 {environment} 配置必须使用 {expectedHost}";
        return valid;
    }
}
