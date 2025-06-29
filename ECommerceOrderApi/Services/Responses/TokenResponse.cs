namespace ECommerceOrderApi.Services.Responses;

public class TokenResponse
{
    public string? TokenType { get; set; }

    public int ExpiresIn { get; set; }

    public string? AccessToken { get; set; }

    public DateTime CreatedAt { get; set; }

    // Token'ın süresi doldu mu kontrol eder
    public bool IsExpired => DateTime.UtcNow >= CreatedAt.AddSeconds(ExpiresIn);

    // Token'ın yenilenmesi gerekiyor mu kontrol eder (son 10 dakikada)
    public bool ShouldRenew => DateTime.UtcNow >= CreatedAt.AddSeconds(ExpiresIn - 600); // 10 dakika buffer
} 