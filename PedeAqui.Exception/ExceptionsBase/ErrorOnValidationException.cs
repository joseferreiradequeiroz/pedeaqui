using System.Net;

namespace PedeAqui.Exception.ExceptionsBase;

public class ErrorOnValidationException : PedeAquiException
{
    public IList<string> Errors { get; }

    public ErrorOnValidationException(IList<string> errorMessages)
        : base(ResourceErrorMessages.INVALID_REQUEST)
    {
        Errors = errorMessages;
    }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.BadRequest;

    public override IList<string> GetErrorMessages() => Errors;
}
