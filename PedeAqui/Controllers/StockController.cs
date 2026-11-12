using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PedeAqui.Application.UseCases.Stock.Entry;
using PedeAqui.Application.UseCases.Stock.Exit;
using PedeAqui.Application.UseCases.Stock.GetByProduct;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Enums;

namespace PedeAqui.Controllers;

[Route("api/products/{productId:long}/stock")]
[ApiController]
[Authorize(Roles = Roles.ADMIN)]
public class StockController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(StockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromRoute] long productId,
        [FromServices] IGetStockByProductUseCase useCase)
    {
        var response = await useCase.Execute(productId);
        return Ok(response);
    }

    [HttpPost("entries")]
    [ProducesResponseType(typeof(StockMovementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegisterEntry(
        [FromRoute] long productId,
        [FromBody] StockMovementRequest request,
        [FromServices] IRegisterStockEntryUseCase useCase)
    {
        var response = await useCase.Execute(productId, request);
        return CreatedAtAction(nameof(Get), new { productId }, response);
    }

    [HttpPost("exits")]
    [ProducesResponseType(typeof(StockMovementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegisterExit(
        [FromRoute] long productId,
        [FromBody] StockMovementRequest request,
        [FromServices] IRegisterStockExitUseCase useCase)
    {
        var response = await useCase.Execute(productId, request);
        return CreatedAtAction(nameof(Get), new { productId }, response);
    }
}
