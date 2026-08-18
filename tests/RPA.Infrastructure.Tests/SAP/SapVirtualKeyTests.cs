namespace RPA.Infrastructure.Tests.SAP;

using RPA.Domain.Exceptions;
using RPA.Infrastructure.SAP;

/// <summary>
/// SAP sanal tuş (VKey) adı → numara eşlemesi. SAP'ın standart tablosu:
/// 0=Enter, 1–12=F1–F12, 13–24=Shift+F1–F12, 25–36=Ctrl+F1–F12, 37–48=Ctrl+Shift+F1–F12.
/// </summary>
public class SapVirtualKeyTests
{
    [Theory]
    [InlineData("Enter", 0)]
    [InlineData("F1", 1)]
    [InlineData("F3", 3)]   // Geri
    [InlineData("F4", 4)]   // Arama yardımı
    [InlineData("F8", 8)]   // Çalıştır
    [InlineData("F11", 11)] // Kaydet
    [InlineData("F12", 12)] // İptal
    public void Parse_FunctionKeys(string key, int expected)
        => Assert.Equal(expected, SapVirtualKey.Parse(key));

    [Theory]
    [InlineData("Shift+F1", 13)]
    [InlineData("Shift+F3", 15)]  // Çıkış
    [InlineData("Shift+F12", 24)]
    [InlineData("Ctrl+F1", 25)]
    [InlineData("Ctrl+F12", 36)]
    [InlineData("Ctrl+Shift+F1", 37)]
    [InlineData("Ctrl+Shift+F12", 48)]
    public void Parse_ModifierCombinations(string key, int expected)
        => Assert.Equal(expected, SapVirtualKey.Parse(key));

    [Theory]
    [InlineData("f8", 8)]
    [InlineData("  F8  ", 8)]
    [InlineData("shift+f3", 15)]
    [InlineData("Shift + F3", 15)]
    [InlineData("CTRL+SHIFT+F2", 38)]
    public void Parse_IsCaseAndWhitespaceInsensitive(string key, int expected)
        => Assert.Equal(expected, SapVirtualKey.Parse(key));

    [Theory]
    [InlineData("Ctrl+S", 11)]  // SAP'ta Kaydet
    [InlineData("Save", 11)]
    [InlineData("Back", 3)]
    [InlineData("Exit", 15)]
    [InlineData("Cancel", 12)]
    [InlineData("Execute", 8)]
    public void Parse_CommonShortcutNames(string key, int expected)
        => Assert.Equal(expected, SapVirtualKey.Parse(key));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("8", 8)]
    [InlineData("48", 48)]
    public void Parse_AcceptsRawVKeyNumber(string key, int expected)
        => Assert.Equal(expected, SapVirtualKey.Parse(key));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("F13")]      // SAP'ta F13 yok
    [InlineData("F0abc")]
    [InlineData("Alt+F4")]   // SAP VKey tablosunda Alt yok
    [InlineData("Ctrl+X")]
    [InlineData("49")]       // aralık dışı
    [InlineData("-1")]
    public void Parse_RejectsUnsupportedInput(string? key)
        => Assert.Throws<BusinessException>(() => SapVirtualKey.Parse(key));
}
