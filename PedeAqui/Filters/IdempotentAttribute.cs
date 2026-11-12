using Microsoft.AspNetCore.Mvc;

namespace PedeAqui.Filters;

public sealed class IdempotentAttribute : TypeFilterAttribute
{
    public IdempotentAttribute() : base(typeof(IdempotencyFilter))
    {
    }
}
