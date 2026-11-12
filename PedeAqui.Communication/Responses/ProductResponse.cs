namespace PedeAqui.Communication.Responses;

public class ProductResponse
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}

public class ProductsResponse
{
    public List<ProductResponse> Products { get; set; } = [];
}
