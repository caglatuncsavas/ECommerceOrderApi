namespace ECommerceOrderApi.V1.Requests;

public class CreateOrderRequest
{
    public Guid UserId { get; set; }
    
    public List<OrderItemRequest> Items { get; set; } = new List<OrderItemRequest>();
}

public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
