using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Erp.Infrastructure.Customers;

internal sealed partial class CustomerPrivacyService
{
    private readonly IDataProtector protector;
    private readonly byte[] lookupKey;

    public CustomerPrivacyService(IDataProtectionProvider provider, IConfiguration configuration, IHostEnvironment environment)
    {
        protector = provider.CreateProtector("Erp.Customer.Mobile.v1");
        var configured = configuration["CustomerPrivacy:LookupPepper"];
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("生产环境必须配置 CustomerPrivacy:LookupPepper");
        lookupKey = SHA256.HashData(Encoding.UTF8.GetBytes(configured ?? "erp-development-only-customer-mobile-pepper-v1"));
    }

    public ProtectedMobile Protect(string input)
    {
        var normalized = Normalize(input);
        return new ProtectedMobile(protector.Protect(normalized), Hash(normalized), normalized[^4..]);
    }

    public byte[] Hash(string normalizedMobile) => HMACSHA256.HashData(lookupKey, Encoding.UTF8.GetBytes(Normalize(normalizedMobile)));

    public static string Normalize(string input)
    {
        var normalized = new string(input.Where(char.IsDigit).ToArray());
        if (!ChineseMobile().IsMatch(normalized)) throw new ArgumentException("请输入有效的中国大陆手机号");
        return normalized;
    }

    public static string MaskMobile(string lastFour) => $"*******{lastFour}";

    public static string MaskName(string name)
    {
        var value = name.Trim();
        return value.Length switch
        {
            0 => "匿名顾客",
            1 => $"{value}*",
            2 => $"{value[0]}*",
            _ => $"{value[0]}{new string('*', Math.Min(3, value.Length - 2))}{value[^1]}",
        };
    }

    public static string MaskCardNo(string cardNo) => cardNo.Length <= 4 ? "****" : $"****{cardNo[^4..]}";

    [GeneratedRegex("^1[3-9][0-9]{9}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseMobile();
}

internal sealed record ProtectedMobile(string Ciphertext, byte[] LookupHash, string LastFour);
