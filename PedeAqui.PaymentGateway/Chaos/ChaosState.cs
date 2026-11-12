using Microsoft.Extensions.Options;

namespace PedeAqui.PaymentGateway.Chaos;

public class ChaosState(IOptions<ChaosOptions> initialOptions)
{
    public ChaosOptions Current { get; set; } = initialOptions.Value;
}
