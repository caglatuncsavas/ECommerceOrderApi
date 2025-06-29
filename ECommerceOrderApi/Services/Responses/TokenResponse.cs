namespace ECommerceOrderApi.Services.Responses;

public class TokenResponse
{
    public string TokenType { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Token'ın süresi doldu mu kontrol eder
    /// </summary>
    public bool IsExpired => DateTime.Now >= CreatedAt.AddSeconds(ExpiresIn);

    /// <summary>
    /// Token'ın yenilenmesi gerekiyor mu kontrol eder (son 10 dakikada)
    /// </summary>
    public bool ShouldRenew => DateTime.Now >= CreatedAt.AddSeconds(ExpiresIn - 600); // 10 dakika buffer
} 