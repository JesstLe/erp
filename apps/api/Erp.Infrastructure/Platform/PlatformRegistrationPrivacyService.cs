using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Erp.Infrastructure.Platform;

internal sealed class PlatformRegistrationPrivacyService
{
    private readonly IDataProtector emailProtector;
    private readonly byte[] contactHashKey;

    public PlatformRegistrationPrivacyService(IDataProtectionProvider provider, IConfiguration configuration,
        IHostEnvironment environment)
    {
        emailProtector = provider.CreateProtector("Erp.Platform.Registration.Email.v1");
        var configured = configuration["PlatformRegistration:ContactHashPepper"];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(configured) || configured.Length < 32))
            throw new InvalidOperationException(
                "生产环境必须配置至少32字符的 PlatformRegistration:ContactHashPepper");
        contactHashKey = SHA256.HashData(Encoding.UTF8.GetBytes(configured ??
            "erp-development-only-platform-registration-contact-pepper-v1"));
    }

    public ProtectedEmail? ProtectEmail(string? input)
    {
        var normalized = NormalizeEmail(input);
        return normalized is null ? null : new ProtectedEmail(emailProtector.Protect(normalized),
            HMACSHA256.HashData(contactHashKey, Encoding.UTF8.GetBytes(normalized)));
    }

    public string? MaskEmail(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return null;
        var email = emailProtector.Unprotect(ciphertext);
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }

    private static string? NormalizeEmail(string? input)
    {
        var normalized = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > 254) throw new ArgumentException("联系邮箱长度不能超过254位");
        try
        {
            var parsed = new MailAddress(normalized);
            if (!string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("联系邮箱格式不正确");
            return normalized;
        }
        catch (FormatException)
        {
            throw new ArgumentException("联系邮箱格式不正确");
        }
    }
}

internal sealed record ProtectedEmail(string Ciphertext, byte[] LookupHash);
