using System.Net;

namespace PedeAqui.Exception.ExceptionsBase;

public abstract class PedeAquiException : SystemException
{
    protected PedeAquiException(string message) : base(message)
    {
    }

    protected PedeAquiException(string message, System.Exception? innerException) : base(message, innerException)
    {
    }

    public abstract HttpStatusCode GetStatusCode();

    public abstract IList<string> GetErrorMessages();
}
