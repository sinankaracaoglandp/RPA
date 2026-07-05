namespace RPA.Domain.Enums;

/// <summary>
/// SAP hibrit kanal türü (Spec Bölüm 6).
/// <see cref="Nco"/> birincil programatik kanal (BAPI/RFC); <see cref="Gui"/> fallback GUI Scripting kanalı.
/// </summary>
public enum SapChannelType
{
    /// <summary>SAP GUI Scripting (fallback) — sapfewse.ocx üzerinden UI otomasyonu.</summary>
    Gui,

    /// <summary>SAP .NET Connector (NCo 3.1) — birincil programatik BAPI/RFC kanalı (Task 4.2).</summary>
    Nco
}
