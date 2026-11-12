using FluentValidation;
using PedeAqui.Communication.Requests;
using PedeAqui.Exception;

namespace PedeAqui.Application.UseCases.Users.Validate;

public class RegisterUserValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserValidator()
    {
        RuleFor(user => user.Name)
            .NotEmpty().WithMessage(ResourceErrorMessages.NAME_REQUIRED);

        RuleFor(user => user.Email)
            .NotEmpty().WithMessage(ResourceErrorMessages.EMAIL_REQUIRED)
            .EmailAddress().WithMessage(ResourceErrorMessages.EMAIL_INVALID)
            .When(user => !string.IsNullOrWhiteSpace(user.Email), ApplyConditionTo.CurrentValidator);

        RuleFor(user => user.Password)
            .SetValidator(new PasswordValidator<RegisterUserRequest>());
    }
}
