using System.Net;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Domain.Payments;

public class PaymentGatewayRejectedException : PedeAquiException
{
    public PaymentGatewayRejectedException(HttpStatusCode gatewayStatusCode)
        : base(ResourceErrorMessages.PAYMENT_GATEWAY_REJECTED)
    {
        GatewayStatusCode = gatewayStatusCode;
    }

    public HttpStatusCode GatewayStatusCode { get; }

    public override HttpStatusCode GetStatusCode() => HttpStatusCode.BadGateway;

    public override IList<string> GetErrorMessages() => [Message];
}
