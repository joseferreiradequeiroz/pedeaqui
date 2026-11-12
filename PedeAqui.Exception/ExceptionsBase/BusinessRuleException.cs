using System.Net;

namespace PedeAqui.Exception.ExceptionsBase;

public class BusinessRuleException : PedeAquiException
{
    public BusinessRuleException(string message) : base(message)
    {
    }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.UnprocessableEntity;

    public override IList<string> GetErrorMessages() => [Message];
}
