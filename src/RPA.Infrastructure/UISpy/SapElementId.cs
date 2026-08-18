namespace RPA.Infrastructure.UISpy;

/// <summary>
/// SAP GUI Scripting element ID biçimlendirmesi. COM'dan bağımsız saf mantık (platform-nötr,
/// birim testlenebilir).
/// </summary>
public static class SapElementId
{
    /// <summary>
    /// SAP mutlak ID'sini (<c>/app/con[0]/ses[0]/wnd[0]/usr/ctxtRMMG1-MATNR</c>) oturumdan bağımsız
    /// göreli forma (<c>wnd[0]/usr/ctxtRMMG1-MATNR</c>) indirger — <c>findById</c> ve
    /// <c>Sap.Gui.*</c> aktivitelerinin beklediği biçim budur. Aksi halde tasarım anındaki
    /// bağlantı/oturum indeksi ID'ye gömülür ve çalışma anında başka bir oturumda kırılır.
    /// </summary>
    public static string Normalize(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return string.Empty;
        }

        var id = rawId.Trim();
        var marker = id.IndexOf("/wnd[", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            return id[(marker + 1)..];
        }

        return id.StartsWith("wnd[", StringComparison.OrdinalIgnoreCase) ? id : id.TrimStart('/');
    }
}
