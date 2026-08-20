using Erp.Infrastructure.Identity;

namespace Erp.Api.IntegrationTests;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("abcd1234")]
    [InlineData("ABCD1234")]
    [InlineData("Mix1234!")]
    public void AcceptsLettersAndNumbersWithoutRequiringCaseMixOrSpecialCharacters(string password)
    {
        Assert.True(PasswordPolicy.IsValid(password));
    }

    [Theory]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    [InlineData("abc1234")]
    public void RejectsMissingComponentsOrInsufficientLength(string password)
    {
        Assert.False(PasswordPolicy.IsValid(password));
    }

    [Fact]
    public async Task IdentityValidatorUsesTheSameLetterAndNumberRule()
    {
        var validator = new LetterAndDigitPasswordValidator();
        var accepted = await validator.ValidateAsync(null!, new ApplicationUser(), "abcd1234");
        var missingNumber = await validator.ValidateAsync(null!, new ApplicationUser(), "abcdefgh");

        Assert.True(accepted.Succeeded);
        Assert.False(missingNumber.Succeeded);
        Assert.Contains(missingNumber.Errors, error => error.Code == "PasswordRequiresDigit");
    }
}
