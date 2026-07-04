namespace RPA.WebAPI.Authentication;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Interfaces;

/// <summary>
/// Kimlik doğrulama endpoint'leri (Spec Bölüm 10). Login herkese açık; JWT döner.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;

    public AuthController(IAuthenticationService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// AD kullanıcı adı/şifre ile giriş yapar, JWT + roller döner.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Kullanıcı adı ve şifre zorunludur." });
        }

        var result = await _authService.AuthenticateAsync(request.Username, request.Password);

        if (!result.Success)
        {
            return Unauthorized(new { error = result.ErrorMessage ?? "Kimlik doğrulama başarısız." });
        }

        return Ok(new LoginResponse
        {
            Token = result.JwtToken!,
            Roles = result.Roles,
        });
    }

    /// <summary>
    /// Middleware testi için korumalı örnek endpoint — geçerli JWT gerektirir.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var username = User.Identity?.Name
            ?? User.FindFirst("sub")?.Value
            ?? "unknown";
        var roles = User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();
        return Ok(new { username, roles });
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}
