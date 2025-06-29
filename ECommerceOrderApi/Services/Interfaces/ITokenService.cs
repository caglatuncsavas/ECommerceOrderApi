using ECommerceOrderApi.Services.Responses;

namespace ECommerceOrderApi.Services.Interfaces;

public interface ITokenService
{
    Task<string?> GetValidToken();
    Task<TokenResponse?> RequestNewTokenAsync();
    bool CanRequestToken();
} 