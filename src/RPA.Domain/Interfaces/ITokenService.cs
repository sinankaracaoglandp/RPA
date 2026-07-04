namespace RPA.Domain.Interfaces;

/// <summary>
/// JWT üretim sözleşmesi. Uygulama secret'ı ile imzalı token üretir (Spec Bölüm 10).
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Kullanıcı adı ve rollerden imzalı JWT üretir.
    /// Claim'ler: sub (kullanıcı), role[] (AD grupları), exp.
    /// </summary>
    string GenerateToken(string username, IEnumerable<string> roles);
}
