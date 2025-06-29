using ECommerceOrderApi.Data;
using ECommerceOrderApi.Data.Entities;
using ECommerceOrderApi.Data.Enums;
using ECommerceOrderApi.V1.Requests;
using ECommerceOrderApi.V1.Responses;
using ECommerceOrderApi.Services.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;


namespace ECommerceOrderApi.V1.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class Orders(
   ECommerceDbContext context,
   IValidator<CreateOrderRequest> createOrderRequestValidator,
   ITokenService tokenService,
   IHttpClientFactory httpClientFactory,
   IConfiguration configuration,
   ILogger<Orders> logger) : ControllerBase
{

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<QueryOrderResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> QueryOrders([FromQuery] QueryOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = "UserId query parameter is required."
            });
        }

        try
        {
            //  External API'den sipariş listesi çek (Token Management ile)
            logger.LogInformation(" External API'den sipariş listesi alınıyor - UserId: {UserId}", request.UserId);

            // Token al (Rate limit korumalı)
            string? token = await tokenService.GetValidToken();
            
            if (string.IsNullOrEmpty(token))
            {
                logger.LogError(" Token alınamadı");

               ProblemDetails problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Token Error",
                    Detail = "Unable to retrieve a valid token for external API."
                };
                return StatusCode(StatusCodes.Status500InternalServerError, problemDetails);
            }

            // External API'yi çağır
            List<QueryOrderResponse> externalOrders = await GetOrdersFromExternalApiAsync(token, request.UserId, cancellationToken);

            logger.LogInformation(" External API'den {Count} sipariş alındı", externalOrders.Count);

            return Ok(externalOrders);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            logger.LogWarning(" Rate limit aşıldı, local DB'den sipariş listesi dönülüyor");
            
            // Rate limit aşıldıysa fallback olarak local DB'den çek
            return await GetOrdersFromLocalDatabase(request.UserId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, " External API'den sipariş alınırken hata oluştu");
            
            // Hata durumunda fallback olarak local DB'den çek
            return await GetOrdersFromLocalDatabase(request.UserId, cancellationToken);
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(QueryOrderResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> GetOrder([FromRoute] int id, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        ThrowExceptionIfOrderNotFound(order);

         QueryOrderResponse response = new QueryOrderResponse
        {
            OrderId = order!.Id,
            UserId = order.UserId,
            CreatedAt = order.CreatedAt,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            Items = order.Items.Select(oi => new OrderItemResponse
            {
                ProductId = oi.ProductId,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                TotalPrice = oi.TotalPrice
            }).ToList()
        };

        return Ok(response);
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

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> DeleteOrder([FromRoute] int id, CancellationToken cancellationToken)
    {
        ThrowExceptionIfOrderIdLessThanOrEqualZero(id);

        Order? order = await context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        ThrowExceptionIfOrderNotFound(order);

        order!.IsDeleted = true;
        order.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return NoContent();
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

    private static void ThrowExceptionIfOrderNotFound(Order? order)
    {
        if (order is null)
        {
            throw new InvalidOperationException("Order not found");
        }
    }

    private static void ThrowExceptionIfOrderIdLessThanOrEqualZero(int id)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Order ID must be greater than zero.", nameof(id));
        }
    }

    /// <summary>
    /// External API'den sipariş listesi çeker (Token ile)
    /// </summary>
    private async Task<List<QueryOrderResponse>> GetOrdersFromExternalApiAsync(string token, Guid userId, CancellationToken cancellationToken)
    {
        var ordersEndpoint = configuration["ExternalApi:OrdersEndpoint"] ?? "https://api.example.com/api/orders";
        var useMockResponse = configuration.GetValue<bool>("ExternalApi:UseMockResponse", true);

        if (useMockResponse)
        {
            // DEMO AMAÇLI MOCK RESPONSE
            logger.LogInformation("📋 Mock external API response dönülüyor...");
            
            await Task.Delay(300, cancellationToken); // API gecikmesini simüle et
            
            var mockOrders = new List<QueryOrderResponse>
            {
                new() 
                {
                    OrderId = 1001,
                    UserId = userId,
                    CreatedAt = DateTime.Now.AddDays(-2),
                    TotalAmount = 150.50m,
                    Status = OrderStatus.Pending,
                    Items = new List<OrderItemResponse>
                    {
                        new() { ProductId = 1, Quantity = 2, UnitPrice = 75.25m, TotalPrice = 150.50m }
                    }
                },
                new() 
                {
                    OrderId = 1002,
                    UserId = userId,
                    CreatedAt = DateTime.Now.AddDays(-1),
                    TotalAmount = 275.25m,
                    Status = OrderStatus.Pending,
                    Items = new List<OrderItemResponse>
                    {
                        new() { ProductId = 2, Quantity = 1, UnitPrice = 275.25m, TotalPrice = 275.25m }
                    }
                }
            };
            
            return mockOrders;
        }
        else
        {
            // GERÇEK API ÇAĞRISI
            using var httpClient = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{ordersEndpoint}?userId={userId}");
            request.Headers.Add("Authorization", token);

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("❌ External API'den sipariş alımında hata: {StatusCode} - {Content}", 
                    response.StatusCode, errorContent);
                
                throw new HttpRequestException($"External API'den sipariş alımında hata: {response.StatusCode}");
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var externalOrders = JsonSerializer.Deserialize<List<QueryOrderResponse>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<QueryOrderResponse>();

            return externalOrders;
        }
    }

    /// <summary>
    /// Fallback: Local DB'den sipariş listesi çeker
    /// </summary>
    private async Task<IActionResult> GetOrdersFromLocalDatabase(Guid userId, CancellationToken cancellationToken)
    {
        logger.LogInformation("💾 Fallback: Local DB'den sipariş listesi alınıyor - UserId: {UserId}", userId);

        List<Order> orders = await context.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .ToListAsync(cancellationToken);

        List<QueryOrderResponse> response = orders.Select(o => new QueryOrderResponse
        {
            OrderId = o.Id,
            UserId = o.UserId,
            CreatedAt = o.CreatedAt,
            TotalAmount = o.TotalAmount,
            Status = o.Status,
            Items = o.Items.Select(oi => new OrderItemResponse
            {
                ProductId = oi.ProductId,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                TotalPrice = oi.TotalPrice
            }).ToList()
        }).ToList();

        return Ok(response);
    }
}
