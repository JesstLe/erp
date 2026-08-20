using Microsoft.AspNetCore.Identity;

namespace Erp.Infrastructure.Identity;

internal static class PasswordPolicy
{
    internal const int MinimumLength = 8;
    internal const int MaximumLength = 256;
    internal const string RequirementText = "密码至少8位，且同时包含英文字母和数字；特殊字符可选";

    internal static bool IsValid(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length is >= MinimumLength and <= MaximumLength &&
        password.Any(IsAsciiLetter) &&
        password.Any(char.IsDigit);

    internal static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

internal sealed class LetterAndDigitPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user,
        string? password)
    {
        var errors = new List<IdentityError>();
        if (password is null || password.Length > PasswordPolicy.MaximumLength)
            errors.Add(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"密码不能超过{PasswordPolicy.MaximumLength}位",
            });
        if (password is null || !password.Any(PasswordPolicy.IsAsciiLetter))
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresLetter",
                Description = "密码必须至少包含一个英文字母",
            });
        if (password is null || !password.Any(char.IsDigit))
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresDigit",
                Description = "密码必须至少包含一个数字",
            });

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }
}
