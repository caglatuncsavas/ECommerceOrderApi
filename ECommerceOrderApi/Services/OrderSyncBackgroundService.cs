using ECommerceOrderApi.Services.Interfaces;
using ECommerceOrderApi.V1.Responses;
using System.Text.Json;
using ECommerceOrderApi.Data.Enums;

namespace ECommerceOrderApi.Services;

// Her 5 dakikada bir external API'den sipariş listesini senkronize eden background service
// Token yönetimi ile entegre çalışır
public class OrderSyncBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<OrderSyncBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(" Order Sync Background Service başlatıldı");

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOrdersFromExternalApi(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " Sipariş senkronizasyonu sırasında hata");
            }
            await Task.Delay(_syncInterval, stoppingToken);
        }

        logger.LogInformation(" Order Sync Background Service durduruluyor");
    }

    private async Task SyncOrdersFromExternalApi(CancellationToken cancellationToken)
    {
        using IServiceScope scope = serviceProvider.CreateScope();

        ITokenService tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        IHttpClientFactory httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        try
        {
            logger.LogInformation(" Sipariş senkronizasyonu başlatılıyor...");

            DateTime startTime = DateTime.UtcNow;

            string? token = await tokenService.GetValidToken();

            if (string.IsNullOrEmpty(token))
            {
                logger.LogError(" Token alınamadığı için sipariş senkronizasyonu atlandı");
                return;
            }

            List<QueryOrderResponse> orders = await FetchOrdersFromExternalApi(
                token, httpClientFactory, configuration, cancellationToken);

            TimeSpan duration = DateTime.UtcNow - startTime;

            logger.LogInformation(" Sipariş senkronizasyonu tamamlandı. {Count} sipariş alındı, süre: {Duration}ms",
                orders.Count, duration.TotalMilliseconds);

        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            logger.LogWarning(" Rate limit nedeniyle sipariş senkronizasyonu atlandı: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, " Sipariş senkronizasyonu sırasında beklenmeyen hata");
        }
    }

    private async Task<List<QueryOrderResponse>> FetchOrdersFromExternalApi(
        string token,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        string? ordersEndpoint = configuration["ExternalApi:OrdersEndpoint"];
        bool useMockResponse = configuration.GetValue<bool>("ExternalApi:UseMockResponse", false);

        if (string.IsNullOrEmpty(ordersEndpoint))
        {
            logger.LogError(" Orders endpoint konfigürasyonu eksik");
            return new List<QueryOrderResponse>();
        }

        // Mock response için test modu
        if (useMockResponse)
        {
            logger.LogInformation(" Mock orders response kullanılıyor");
            return new List<QueryOrderResponse>
            {
                new QueryOrderResponse
                {
                    OrderId = Random.Shared.Next(1, 1000),
                    UserId = Guid.NewGuid(),
                    TotalAmount = Random.Shared.Next(100, 5000),
                    CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
                    Status = OrderStatus.Shipped
                },
                new QueryOrderResponse
                {
                    OrderId = Random.Shared.Next(1, 1000),
                    UserId = Guid.NewGuid(),
                    TotalAmount = Random.Shared.Next(100, 5000),
                    CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
                    Status = OrderStatus.Pending
                }
            };
        }

        using HttpClient httpClient = httpClientFactory.CreateClient();

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ordersEndpoint);

        request.Headers.Add("Authorization", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(" External API'den sipariş alımında hata: {StatusCode} - {Content}",
                response.StatusCode, errorContent);

            throw new HttpRequestException($"External API'den sipariş alımında hata: {response.StatusCode}");
        }

        string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

        List<QueryOrderResponse> orders = JsonSerializer.Deserialize<List<QueryOrderResponse>>(jsonContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<QueryOrderResponse>();

        return orders;
    }
} 