namespace RPA.Agent.Authentication;

/// <summary>
/// Ajan komut satiri girisleri. Su an tek mod: <c>--activate &lt;kod&gt;</c> — operatorun Studio'dan
/// aldigi tek kullanimlik aktivasyon kodunu tuketip credential'i korumali depoya yazar, sonra cikar
/// (uzun surecli host dongusu baslatilmaz).
/// </summary>
public static class AgentCommandLine
{
    private const string ActivateFlag = "--activate";
    private const string ActivateUiFlag = "--activate-ui";

    /// <summary>
    /// <c>--activate-ui</c> bayragi verildiyse true — kod satiri yerine aktivasyon PENCERESI acilir.
    /// Operator AgentId/InstallationId/kodu ekrandan girer; appsettings.json duzenlemek gerekmez.
    /// </summary>
    public static bool IsActivationUi(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Any(argument => string.Equals(argument, ActivateUiFlag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <c>--activate KOD</c> veya <c>--activate=KOD</c> bicimlerini okur. Bayrak yoksa false.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Bayrak verilmis ama degeri yoksa. Sessizce normal baslatmaya dusmek, operatorun kodu
    /// unuttugu durumda "aktive oldu" yanilgisi yaratirdi.
    /// </exception>
    public static bool TryGetActivationCode(string[] args, out string activationCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        activationCode = "";

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument.StartsWith(ActivateFlag + "=", StringComparison.OrdinalIgnoreCase))
            {
                activationCode = argument[(ActivateFlag.Length + 1)..];
            }
            else if (string.Equals(argument, ActivateFlag, StringComparison.OrdinalIgnoreCase))
            {
                activationCode = index + 1 < args.Length ? args[index + 1] : "";
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(activationCode))
            {
                throw new ArgumentException(
                    $"{ActivateFlag} icin aktivasyon kodu verilmedi. Kullanim: {ActivateFlag} <kod>");
            }

            return true;
        }

        return false;
    }
}
