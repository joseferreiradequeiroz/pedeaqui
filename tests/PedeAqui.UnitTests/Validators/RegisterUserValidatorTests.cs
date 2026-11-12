using PedeAqui.Application.UseCases.Users.Validate;
using PedeAqui.Exception;
using PedeAqui.UnitTests.Builders;

namespace PedeAqui.UnitTests.Validators;

public class RegisterUserValidatorTests
{
    private readonly RegisterUserValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_ReturnsValid()
    {
        var result = _validator.Validate(RequestBuilder.RegisterUser());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyName_ReturnsNameRequired(string name)
    {
        var request = RequestBuilder.RegisterUser();
        request.Name = name;

        var result = _validator.Validate(request);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ResourceErrorMessages.NAME_REQUIRED, error.ErrorMessage);
    }

    [Fact]
    public void Validate_EmptyEmail_ReturnsOnlyEmailRequired()
    {
        var request = RequestBuilder.RegisterUser();
        request.Email = string.Empty;

        var result = _validator.Validate(request);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ResourceErrorMessages.EMAIL_REQUIRED, error.ErrorMessage);
    }

    [Fact]
    public void Validate_InvalidEmail_ReturnsEmailInvalid()
    {
        var request = RequestBuilder.RegisterUser();
        request.Email = "email-invalido";

        var result = _validator.Validate(request);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ResourceErrorMessages.EMAIL_INVALID, error.ErrorMessage);
    }

    [Theory]
    [InlineData("", ResourceErrorMessages.PASSWORD_REQUIRED)]
    [InlineData("Aa1!aaa", ResourceErrorMessages.PASSWORD_MIN_LENGTH)]
    [InlineData("aa1!aaaa", ResourceErrorMessages.PASSWORD_UPPERCASE)]
    [InlineData("AA1!AAAA", ResourceErrorMessages.PASSWORD_LOWERCASE)]
    [InlineData("Aaa!aaaa", ResourceErrorMessages.PASSWORD_NUMBER)]
    [InlineData("Aa1aaaaa", ResourceErrorMessages.PASSWORD_SPECIAL_CHARACTER)]
    public void Validate_WeakPassword_ReturnsSpecificMessage(string password, string expectedMessage)
    {
        var request = RequestBuilder.RegisterUser();
        request.Password = password;

        var result = _validator.Validate(request);

        var error = Assert.Single(result.Errors);
        Assert.Equal(expectedMessage, error.ErrorMessage);
    }

    [Fact]
    public void Validate_PasswordWithExactlyEightCharacters_ReturnsValid()
    {
        var request = RequestBuilder.RegisterUser();
        request.Password = "Aa1!aaaa";

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
