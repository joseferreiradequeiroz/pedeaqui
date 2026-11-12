using System.Net;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Domain.Payments;

public class PaymentGatewayUnavailableException : PedeAquiException
{
    public PaymentGatewayUnavailableException(System.Exception? innerException)
        : base(ResourceErrorMessages.PAYMENT_GATEWAY_UNAVAILABLE, innerException)
    {
    }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.ServiceUnavailable;

    public override IList<string> GetErrorMessages() => [Message];
}
