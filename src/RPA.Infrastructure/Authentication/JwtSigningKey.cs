namespace RPA.Infrastructure.Authentication;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// JWT imza anahtarinin TEK turetme kaynagi. Ayni salt/iterasyon/uzunluk kombinasyonu daha once
/// uc ayri yerde (JwtTokenService, AgentTokenService, Program.cs) elle tekrarlaniyordu; birinde
/// yapilan bir degisiklik digerleriyle sessizce uyumsuz kalir ve uretilen token'lar dogrulanamazdi.
/// </summary>
/// <remarks>
/// Turetilen anahtar secret basina onbelleklenir: PBKDF2 bilerek pahalidir (10k iterasyon) ve
/// her token uretiminde yeniden calistirilmasi icin bir sebep yoktur — girdi sabittir.
/// </remarks>
public static class JwtSigningKey
{
    /// <summary>Sabit salt — degistirilmesi mevcut tum token'lari gecersiz kilar.</summary>
    private const string Salt = "RPA.JwtTokenService.v1";
    private const int Iterations = 10000;
    private const int KeyLength = 32;

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Secret'tan 32 baytlik imza anahtarini turetir. Secret bos veya 32 bayttan kisaysa
    /// <see cref="InvalidOperationException"/> atar.
    /// </summary>
    public static byte[] Derive(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("JWT secret yapılandırılmamış veya 32 byte'tan kısa.");
        }

        // Kopya dondurulur: cagiran diziyi degistirse bile onbellek bozulmasin.
        return Cache.GetOrAdd(secret, static value => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(value),
            Encoding.UTF8.GetBytes(Salt),
            Iterations,
            HashAlgorithmName.SHA256,
            KeyLength)).ToArray();
    }
}
