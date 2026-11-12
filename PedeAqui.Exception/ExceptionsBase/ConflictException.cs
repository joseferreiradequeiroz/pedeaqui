using System.Net;

namespace PedeAqui.Exception.ExceptionsBase;

public class ConflictException : PedeAquiException
{
    public ConflictException(string message) : base(message)
    {
    }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.Conflict;

    public override IList<string> GetErrorMessages() => [Message];
}
