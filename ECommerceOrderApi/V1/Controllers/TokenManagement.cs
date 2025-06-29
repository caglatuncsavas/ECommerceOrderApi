using ECommerceOrderApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderApi.V1.Controllers;

/// <summary>
/// Token yönetimi test ve monitoring endpoint'leri
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
public class TokenManagement(
    ITokenService tokenService,
    ILogger<TokenManagement> logger) : ControllerBase
{
    /// <summary>
    /// Mevcut token durumunu kontrol eder
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTokenStatus()
    {
        try
        {
            logger.LogInformation("🔍 Token durumu sorgulanıyor...");

            string? token = await tokenService.GetValidToken();

            if (!string.IsNullOrEmpty(token))
            {
                return Ok(new
                {
                    status = "success",
                    message = "Token geçerli",
                    hasValidToken = true,
                    timestamp = DateTime.UtcNow
                });
            }

            return Ok(new
            {
                status = "warning",
                message = "Token alınamadı",
                hasValidToken = false,
                timestamp = DateTime.UtcNow
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            logger.LogWarning("⚠️ Rate limit aşıldı: {Message}", ex.Message);

            return BadRequest(new
            {
                status = "error",
                message = "Rate limit aşıldı",
                hasValidToken = false,
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Token durumu kontrolünde hata");

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                status = "error",
                message = "Token durumu kontrol edilemedi",
                hasValidToken = false,
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Rate limit durumunu kontrol eder
    /// </summary>
    [HttpGet("rate-limit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetRateLimitStatus()
    {
        try
        {
            bool canRequest = tokenService.CanRequestToken();

            return Ok(new
            {
                status = "success",
                canRequestToken = canRequest,
                message = canRequest ? "Token istegi yapilabilir" : "Rate limit aktif",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Rate limit durumu kontrolünde hata");

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                status = "error",
                message = "Rate limit durumu kontrol edilemedi",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Yeni token almayı zorlar (Test amaçlı)
    /// </summary>
    [HttpPost("force-renew")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForceTokenRenewal()
    {
        try
        {
            logger.LogInformation("🔄 Manuel token yenileme başlatılıyor...");

            if (!tokenService.CanRequestToken())
            {
                return BadRequest(new
                {
                    status = "error",
                    message = "Rate limit nedeniyle token yenilenemez",
                    timestamp = DateTime.UtcNow
                });
            }

            var newToken = await tokenService.RequestNewTokenAsync();

            if (newToken != null)
            {
                return Ok(new
                {
                    status = "success",
                    message = "Token başarıyla yenilendi",
                    tokenType = newToken.TokenType,
                    expiresIn = newToken.ExpiresIn,
                    createdAt = newToken.CreatedAt,
                    timestamp = DateTime.UtcNow
                });
            }

            return BadRequest(new
            {
                status = "error",
                message = "Token yenilenemedi",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Manuel token yenilemede hata");

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                status = "error",
                message = "Token yenilenemedi",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }
} 