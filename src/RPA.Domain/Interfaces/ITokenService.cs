namespace RPA.Domain.Interfaces;

/// <summary>
/// Access/refresh token üretim ve doğrulama sözleşmesi.
/// </summary>
public interface ITokenService
{
    AuthTokenPair GenerateTokenPair(string username, IEnumerable<string> roles);

    RefreshTokenValidationResult ValidateRefreshToken(string refreshToken);
}

public sealed class AuthTokenPair
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; init; }
}

public sealed class RefreshTokenValidationResult
{
    public bool Success { get; init; }
    public string? Username { get; init; }
    public List<string> Roles { get; init; } = new();
    public string? ErrorMessage { get; init; }

    public static RefreshTokenValidationResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };

    public static RefreshTokenValidationResult Ok(string username, List<string> roles) =>
        new() { Success = true, Username = username, Roles = roles };
}
