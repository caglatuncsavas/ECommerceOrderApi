using ECommerceOrderApi.Data.Entities;
using ECommerceOrderApi.Data.Enums;

namespace ECommerceOrderApi.V1.Responses;

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new List<OrderItemResponse>();
}

public class OrderItemResponse
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
} 