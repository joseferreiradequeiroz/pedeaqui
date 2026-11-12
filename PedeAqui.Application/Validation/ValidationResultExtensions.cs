using FluentValidation.Results;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.Validation;

public static class ValidationResultExtensions
{
    public static void ThrowIfInvalid(this ValidationResult result)
    {
        if (result.IsValid)
            return;

        var errorMessages = result.Errors.Select(error => error.ErrorMessage).ToList();
        throw new ErrorOnValidationException(errorMessages);
    }
}
