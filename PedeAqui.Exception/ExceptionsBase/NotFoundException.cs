using System.Net;

namespace PedeAqui.Exception.ExceptionsBase;

public class NotFoundException : PedeAquiException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.NotFound;

    public override IList<string> GetErrorMessages() => [Message];
}
