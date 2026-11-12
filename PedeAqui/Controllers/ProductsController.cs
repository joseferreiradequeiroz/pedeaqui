using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PedeAqui.Application.UseCases.Products.Delete;
using PedeAqui.Application.UseCases.Products.GetAll;
using PedeAqui.Application.UseCases.Products.GetById;
using PedeAqui.Application.UseCases.Products.Register;
using PedeAqui.Application.UseCases.Products.Update;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Enums;

namespace PedeAqui.Controllers;

[Route("api/products")]
[ApiController]
[Authorize(Roles = Roles.ADMIN)]
public class ProductsController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromServices] IGetAllProductsUseCase useCase)
    {
        var response = await useCase.Execute();
        return Ok(response);
    }

    [HttpGet("{id:long}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        [FromRoute] long id,
        [FromServices] IGetProductByIdUseCase useCase)
    {
        var response = await useCase.Execute(id);
        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] ProductRequest request,
        [FromServices] IRegisterProductUseCase useCase)
    {
        var response = await useCase.Execute(request);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromRoute] long id,
        [FromBody] ProductRequest request,
        [FromServices] IUpdateProductUseCase useCase)
    {
        await useCase.Execute(id, request);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] long id,
        [FromServices] IDeleteProductUseCase useCase)
    {
        await useCase.Execute(id);
        return NoContent();
    }
}
