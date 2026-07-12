namespace RPA.Agent.Tests.Vision;

using RPA.Agent.Vision;
using Xunit;

public class OcrTextMatchTests
{
    [Theory]
    [InlineData("  Kaydet  ", "kaydet", "contains", true)]
    [InlineData("Kaydet ve Kapat", "kaydet", "contains", true)]
    [InlineData("Kaydet ve Kapat", "kaydet", "exact", false)]
    [InlineData("Kaydet", "kaydet", "exact", true)]
    [InlineData("İptal", "iptal", "contains", true)]  // TR büyük İ / küçük i toleransı
    [InlineData("Save", "kaydet", "contains", false)]
    public void Matches_NormalizesWhitespaceAndCase(string ocrWord, string query, string mode, bool expected)
    {
        Assert.Equal(expected, OcrTextMatch.Matches(ocrWord, query, mode));
    }
}
