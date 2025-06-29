using ECommerceOrderApi.Services.Interfaces;

namespace ECommerceOrderApi.Services;

// Token'ı proaktif olarak yenileyen background service
// Her 5 dakikada bir token durumunu kontrol eder ve gerektiğinde yeniler
public class TokenRenewalBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<TokenRenewalBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(" Token Renewal Background Service başlatıldı");

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRenewToken(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " Token renewal sırasında hata");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        logger.LogInformation(" Token Renewal Background Service durduruluyor");
    }

    private async Task CheckAndRenewToken(CancellationToken cancellationToken)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        ITokenService tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        try
        {
            logger.LogDebug(" Token durumu kontrol ediliyor...");

            string? token = await tokenService.GetValidToken();

            if (!string.IsNullOrEmpty(token))
            {
                logger.LogDebug(" Token kontrolü tamamlandı");
            }
            else
            {
                logger.LogWarning(" Token alınamadı");
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            logger.LogWarning("Rate limit nedeniyle token yenilenemedi: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Token kontrolü sırasında beklenmeyen hata");
        }
    }
} 