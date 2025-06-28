using ECommerceOrderApi.Data;
using ECommerceOrderApi.Data.Entities;
using ECommerceOrderApi.Data.Enums;
using ECommerceOrderApi.V1.Requests;
using ECommerceOrderApi.V1.Responses;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace ECommerceOrderApi.V1.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class Orders(
   ECommerceDbContext context,
   IValidator<CreateOrderRequest> createOrderRequestValidator,
   ILogger<Orders> logger) : ControllerBase
{

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<QueryOrderResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> QueryOrders([FromQuery] QueryOrderRequest request, CancellationToken cancellationToken)
    {
        // Kullanıcıya ait siparişleri ve kalemlerini çek
        List<Order> orders = await context.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == request.UserId)
            .ToListAsync(cancellationToken);

      if(request.)
    }


    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(CreateOrderResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        await createOrderRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        bool userExists = await context.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);

        if (!userExists)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Internal Server Error. Please contact support."
            );
        }

        await CheckProductStocksOrThrowAsync(request.Items, context, cancellationToken);

        Order order = new Order()
        {
            UserId = request.UserId,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.Pending, 
            Items = new List<OrderItem>(),
            TotalAmount = 0m
        };

        foreach (OrderItemRequest item in request.Items)
        {
            Product product = await context.Products.FirstAsync(p => p.Id == item.ProductId, cancellationToken);

            product.Stock -= item.Quantity;

            OrderItem orderItem = new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                UnitPrice = product.Price,
                TotalPrice = product.Price * item.Quantity
            };

            order.Items.Add(orderItem);
            order.TotalAmount += orderItem.TotalPrice;
        }

        await context.Orders.AddAsync(order, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        CreateOrderResponse response = new CreateOrderResponse
        {
            OrderId = order.Id,
            UserId = order.UserId,
            CreatedAt = order.CreatedAt,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            Items = order.Items.Select(oi => new OrderItemResponse
            {
                ProductId = oi.ProductId,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                TotalPrice = oi.TotalPrice
            }).ToList()
        };

        return Created("", response);
    }

    private async Task CheckProductStocksOrThrowAsync(List<OrderItemRequest> items, ECommerceDbContext context, CancellationToken cancellationToken)
    {

        foreach (OrderItemRequest item in items)
        {
            var product = await context.Products
                .Where(p => p.Id == item.ProductId)
                .Select(p => new { p.Id, p.Stock })
                .FirstOrDefaultAsync(cancellationToken);

            if (product is null)
            {
                throw new ValidationException($"Product ID {item.ProductId} not found.");
            }

            if (product.Stock < item.Quantity)
            {
                throw new ValidationException($"Insufficient stock for Product ID {item.ProductId}. Available stock: {product.Stock}, requested quantity: {item.Quantity}.");
            }
        }
    }
}
