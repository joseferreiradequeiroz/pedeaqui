using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PedeAqui.IntegrationTests.Edge;

[ApiController]
[AllowAnonymous]
[Route("test/edge")]
public class EdgeProbeController : ControllerBase
{
    public const string INTERNAL_DETAIL = "detalhe-interno-que-nao-pode-vazar";

    [HttpGet("unhandled")]
    public IActionResult Unhandled() => throw new InvalidOperationException(INTERNAL_DETAIL);

    [HttpGet("slow")]
    public async Task<IActionResult> Slow(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return Ok();
    }
}
