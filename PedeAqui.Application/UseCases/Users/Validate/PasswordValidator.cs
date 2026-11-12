using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;
using PedeAqui.Exception;

namespace PedeAqui.Application.UseCases.Users.Validate;

public class PasswordValidator<T> : PropertyValidator<T, string>
{
    private const string ERROR_MESSAGE_KEY = "ErrorMessage";
    private const int MINIMUM_LENGTH = 8;

    public override string Name => "PasswordValidator";

    protected override string GetDefaultMessageTemplate(string errorCode) => $"{{{ERROR_MESSAGE_KEY}}}";

    public override bool IsValid(ValidationContext<T> context, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return Fail(context, ResourceErrorMessages.PASSWORD_REQUIRED);

        if (password.Length < MINIMUM_LENGTH)
            return Fail(context, ResourceErrorMessages.PASSWORD_MIN_LENGTH);

        if (!Regex.IsMatch(password, "[A-Z]"))
            return Fail(context, ResourceErrorMessages.PASSWORD_UPPERCASE);

        if (!Regex.IsMatch(password, "[a-z]"))
            return Fail(context, ResourceErrorMessages.PASSWORD_LOWERCASE);

        if (!Regex.IsMatch(password, "[0-9]"))
            return Fail(context, ResourceErrorMessages.PASSWORD_NUMBER);

        if (!Regex.IsMatch(password, @"[\W_]"))
            return Fail(context, ResourceErrorMessages.PASSWORD_SPECIAL_CHARACTER);

        return true;
    }

    private static bool Fail(ValidationContext<T> context, string message)
    {
        context.MessageFormatter.AppendArgument(ERROR_MESSAGE_KEY, message);
        return false;
    }
}
