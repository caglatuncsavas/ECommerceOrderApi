using ECommerceOrderApi.Services.Interfaces;
using ECommerceOrderApi.Services.Responses;
using System.Text;
using System.Text.Json;

namespace ECommerceOrderApi.Services;

// External API'den token alımını yöneten servis
// Rate limit koruması ve caching desteği sağlar
public class TokenService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TokenService> logger) : ITokenService
{
    private static TokenResponse? _cachedToken;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private static int _requestCount = 0;
    private readonly TimeSpan _rateLimitWindow = TimeSpan.FromHours(1);
    private const int MaxRequestsPerHour = 5;

    // Geçerli bir token döner. Cache'den veya yeni istek ile.
    public async Task<string?> GetValidToken()
    {
        await _semaphore.WaitAsync();

        try
        {
            // Mevcut token geçerliyse döner
            if (_cachedToken != null && !_cachedToken.IsExpired && !_cachedToken.ShouldRenew)
            {
                logger.LogInformation(" Cached token kullanılıyor. Expires: {ExpiresAt}", 
                    _cachedToken.CreatedAt.AddSeconds(_cachedToken.ExpiresIn));
                return $"{_cachedToken.TokenType} {_cachedToken.AccessToken}";
            }

            // Token yenilenmesi gerekiyorsa
            if (_cachedToken?.ShouldRenew == true)
            {
                logger.LogInformation(" Token yenileniyor (10 dakika buffer)...");
            }
            else if (_cachedToken?.IsExpired == true)
            {
                logger.LogWarning(" Token süresi dolmuş, yenisi alınıyor...");
            }
            else
            {
                logger.LogInformation(" İlk token alımı yapılıyor...");
            }

            // Rate limit kontrolü
            if (!CanRequestToken())
            {
                logger.LogError(" Rate limit aşıldı! Son 1 saat içinde {Count} istek yapıldı (Max: {Max})", 
                    _requestCount, MaxRequestsPerHour);
                throw new InvalidOperationException("Rate limit exceeded for token requests");
            }

            // Yeni token al
            TokenResponse? newToken = await RequestNewTokenAsync();

            if (newToken != null)
            {
                _cachedToken = newToken;
                logger.LogInformation(" Yeni token başarıyla alındı. Expires in: {ExpiresIn} saniye", 
                    newToken.ExpiresIn);
                return $"{newToken.TokenType} {newToken.AccessToken}";
            }

            logger.LogError(" Token alınamadı");
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // External API'den yeni token ister
    public async Task<TokenResponse?> RequestNewTokenAsync()
    {
        string? tokenEndpoint = configuration["ExternalApi:TokenEndpoint"];
        string? clientId = configuration["ExternalApi:ClientId"];
        string? clientSecret = configuration["ExternalApi:ClientSecret"];
        bool useMockResponse = configuration.GetValue<bool>("ExternalApi:UseMockResponse", false);

        if (string.IsNullOrEmpty(tokenEndpoint) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            logger.LogError(" Token endpoint konfigürasyonu eksik");
            return null;
        }

        try
        {
            UpdateRateLimitCounters();

            // Mock response için test modu
            if (useMockResponse)
            {
                logger.LogInformation(" Mock token response kullanılıyor");
                return new TokenResponse
                {
                    TokenType = "Bearer",
                    ExpiresIn = 3600, // 1 saat
                    AccessToken = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow
                };
            }

            using HttpClient httpClient = httpClientFactory.CreateClient("TokenService");

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);

            // OAuth2 Client Credentials Grant
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Add("Authorization", $"Basic {credentials}");

            FormUrlEncodedContent content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            request.Content = content;

            logger.LogInformation(" Token isteği gönderiliyor: {Endpoint}", tokenEndpoint);

            using HttpResponseMessage response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();

                logger.LogError(" Token alımında hata: {StatusCode} - {Content}", 
                    response.StatusCode, errorContent);

                return null;
            }

            string jsonContent = await response.Content.ReadAsStringAsync();
            
            JsonDocument jsonDoc = JsonDocument.Parse(jsonContent);
            JsonElement root = jsonDoc.RootElement;

            TokenResponse tokenResponse = new TokenResponse
            {
                TokenType = root.GetProperty("token_type").GetString() ?? "Bearer",
                ExpiresIn = root.GetProperty("expires_in").GetInt32(),
                AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            logger.LogInformation(" Token başarıyla alındı. Type: {TokenType}, Expires in: {ExpiresIn}s", 
                tokenResponse.TokenType, tokenResponse.ExpiresIn);

            return tokenResponse;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, " Token alımında beklenmeyen hata");
            return null;
        }
    }

    // Rate limit kontrolü yapar
    public bool CanRequestToken()
    {
        DateTime now = DateTime.UtcNow;

        // Rate limit window'u geçtiyse sayacı sıfırla
        if (now - _lastRequestTime > _rateLimitWindow)
        {
            _requestCount = 0;
            logger.LogInformation(" Rate limit window sıfırlandı");
        }

        return _requestCount < MaxRequestsPerHour;
    }

    // Rate limit sayaçlarını günceller
    private void UpdateRateLimitCounters()
    {
        DateTime now = DateTime.UtcNow;

        // İlk istek veya window geçtiyse
        if (_lastRequestTime == DateTime.MinValue || now - _lastRequestTime > _rateLimitWindow)
        {
            _requestCount = 1;
            _lastRequestTime = now;

            logger.LogInformation(" Rate limit sayacı başlatıldı: {Count}/{Max}", _requestCount, MaxRequestsPerHour);
        }
        else
        {
            _requestCount++;
            logger.LogInformation(" Rate limit sayacı güncellendi: {Count}/{Max}", _requestCount, MaxRequestsPerHour);
        }
    }
} 