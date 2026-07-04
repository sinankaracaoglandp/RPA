namespace RPA.Domain.Interfaces;

/// <summary>
/// Credential yönetimi — SAP, Web, API, E-posta, TOTP secret'ları.
/// Spec Bölüm 5.5: credential asla plaintext DB'de saklanmaz, Vault referansı tutulur.
/// İki implementasyon: HashiCorp Vault (bulut/on-prem), DPAPI (lokal Windows).
/// </summary>
public interface ICredentialVault
{
    /// <summary>
    /// Secret'ı vault'tan al.
    /// </summary>
    /// <param name="key">Vault'ta saklanan key (örn. "sap-dev-user-password")</param>
    /// <returns>SecureString — bellekte kriptolu, plaintext açılmaz loglarda</returns>
    Task<SecureString> GetSecretAsync(string key);

    /// <summary>
    /// Secret'ı vault'a koy.
    /// </summary>
    /// <param name="key">Vault key</param>
    /// <param name="secret">Değer</param>
    /// <param name="metadata">Etiketler (SAP, Web, API, Email, TOTP) — bulunabilirlik için</param>
    Task StoreSecretAsync(string key, SecureString secret, Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Secret'ı vault'tan sil.
    /// </summary>
    Task DeleteSecretAsync(string key);

    /// <summary>
    /// Vault'ta key var mı?
    /// </summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// Etiketle filtreleme (örn. type=SAP).
    /// </summary>
    Task<IEnumerable<string>> ListSecretsByTagAsync(string tag);
}

/// <summary>
/// Windows DPAPI kullanarak şifreli SecureString.
/// Plaintext bellekte hiçbir zaman açık tutulmaz; yalnızca DPAPI ile
/// şifrelenmiş byte dizisi saklanır ve <see cref="Decrypt"/> çağrısında
/// (aktivite çalıştırma zamanı) geçici olarak çözülür.
///
/// Şifreleme kullanıcı+makine kapsamında (CurrentUser scope) yapılır:
/// başka kullanıcı/makine çözemez. Windows dışı platformlarda DPAPI
/// bulunmadığından yalnızca on-premise Windows robot ortamı desteklenir.
///
/// crypt32.dll doğrudan P/Invoke ile çağrılır — böylece Domain katmanı
/// harici NuGet bağımlılığı almaz (Onion mimari kuralı).
/// </summary>
public sealed class SecureString
{
    private readonly byte[] _encryptedBytes;

    public SecureString(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        _encryptedBytes = Dpapi.Protect(System.Text.Encoding.UTF8.GetBytes(plaintext));
    }

    /// <summary>
    /// Ham şifreli byte'lardan yeniden oluşturmak için (vault'tan okurken).
    /// </summary>
    private SecureString(byte[] encryptedBytes, bool _)
    {
        _encryptedBytes = encryptedBytes;
    }

    /// <summary>
    /// Vault deposunda saklanmış DPAPI şifreli byte'lardan SecureString üretir.
    /// </summary>
    public static SecureString FromEncryptedBytes(byte[] encryptedBytes)
    {
        ArgumentNullException.ThrowIfNull(encryptedBytes);
        return new SecureString(encryptedBytes, true);
    }

    /// <summary>
    /// DPAPI şifreli ham byte kopyasını döndürür (kalıcı saklama için).
    /// </summary>
    public byte[] GetEncryptedBytes() => (byte[])_encryptedBytes.Clone();

    /// <summary>
    /// Decrypt et — sadece gerektiğinde (aktivite çalıştırma zamanı).
    /// </summary>
    public string Decrypt()
    {
        var plainBytes = Dpapi.Unprotect(_encryptedBytes);
        try
        {
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    /// <summary>Loglarda plaintext açılmaz — daima maskeli.</summary>
    public override string ToString() => "[SecureString]";

    /// <summary>
    /// Windows Data Protection API (DPAPI) crypt32.dll sarmalayıcısı.
    /// </summary>
    private static class Dpapi
    {
        public static byte[] Protect(byte[] data)
        {
            var input = new DataBlob(data);
            var output = default(DataBlob);
            try
            {
                if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CryptProtectUiForbidden, ref output))
                {
                    throw new System.ComponentModel.Win32Exception(
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                        "DPAPI CryptProtectData başarısız.");
                }
                return output.ToByteArray();
            }
            finally
            {
                input.Free();
                output.FreeLocal();
            }
        }

        public static byte[] Unprotect(byte[] data)
        {
            var input = new DataBlob(data);
            var output = default(DataBlob);
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CryptProtectUiForbidden, ref output))
                {
                    throw new System.ComponentModel.Win32Exception(
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                        "DPAPI CryptUnprotectData başarısız.");
                }
                return output.ToByteArray();
            }
            finally
            {
                input.Free();
                output.FreeLocal();
            }
        }

        private const int CryptProtectUiForbidden = 0x1;

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;

            public DataBlob(byte[] data)
            {
                cbData = data.Length;
                pbData = System.Runtime.InteropServices.Marshal.AllocHGlobal(data.Length);
                System.Runtime.InteropServices.Marshal.Copy(data, 0, pbData, data.Length);
            }

            public byte[] ToByteArray()
            {
                var managed = new byte[cbData];
                System.Runtime.InteropServices.Marshal.Copy(pbData, managed, 0, cbData);
                return managed;
            }

            /// <summary>AllocHGlobal ile ayrılan input belleğini serbest bırakır.</summary>
            public void Free()
            {
                if (pbData != IntPtr.Zero)
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(pbData);
                    pbData = IntPtr.Zero;
                }
            }

            /// <summary>DPAPI'nin LocalAlloc ile döndürdüğü çıktı belleğini serbest bırakır.</summary>
            public void FreeLocal()
            {
                if (pbData != IntPtr.Zero)
                {
                    LocalFree(pbData);
                    pbData = IntPtr.Zero;
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true,
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true,
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
