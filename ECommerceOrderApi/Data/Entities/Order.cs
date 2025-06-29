using ECommerceOrderApi.Data.Enums;

namespace ECommerceOrderApi.Data.Entities;

public class Order
{
    public int Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; }

    public List<OrderItem> Items { get; set; } = new List<OrderItem>();

    public decimal TotalAmount { get; set; }

    public OrderStatus Status { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime UpdatedAt { get; set; } 

    public DateTime CreatedAt { get; set; }
}

public class OrderItem
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

