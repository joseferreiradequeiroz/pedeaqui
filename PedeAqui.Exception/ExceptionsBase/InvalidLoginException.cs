using System.Net;

namespace PedeAqui.Exception.ExceptionsBase;

public class InvalidLoginException : PedeAquiException
{
    public InvalidLoginException() : base(ResourceErrorMessages.EMAIL_OR_PASSWORD_INVALID)
    {
    }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.Unauthorized;

    public override IList<string> GetErrorMessages() => [Message];
}
