using ECommerceOrderApi.Data.Enums;

namespace ECommerceOrderApi.V1.Responses;

public class QueryOrderResponse
{
    public int OrderId { get; set; }

    public Guid UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal TotalAmount { get; set; }

    public OrderStatus Status { get; set; }

    public List<OrderItemResponse> Items { get; set; } = new();
}
