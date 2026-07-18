namespace RPA.Domain.Tests.ValueObjects;

using RPA.Domain.ValueObjects;
using Xunit;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// Desktop.SendKeys yapısal tuş dizisi (chord + text) ayrıştırma ve doğrulama testleri.
/// Ham <c>keys</c> alanı JSON adım dizisi taşır; JSON değilse tek text adımı (geriye uyumlu).
/// </summary>
public class KeystrokeSequenceParserTests
{
    [Fact]
    public void Parse_JsonArray_ReturnsTypedSteps()
    {
        const string json = """
        [
          { "type": "chord", "modifiers": ["ctrl"], "key": "A", "waitMs": 0 },
          { "type": "text",  "text": "09.07.2026", "waitMs": 100 },
          { "type": "chord", "modifiers": [], "key": "Enter" }
        ]
        """;

        var steps = KeystrokeSequenceParser.Parse(json);

        Assert.Equal(3, steps.Count);

        Assert.Equal(KeystrokeStepType.Chord, steps[0].Type);
        Assert.Equal(new[] { "ctrl" }, steps[0].Modifiers);
        Assert.Equal("A", steps[0].Key);

        Assert.Equal(KeystrokeStepType.Text, steps[1].Type);
        Assert.Equal("09.07.2026", steps[1].Text);
        Assert.Equal(100, steps[1].WaitMs);

        Assert.Equal(KeystrokeStepType.Chord, steps[2].Type);
        Assert.Empty(steps[2].Modifiers);
        Assert.Equal("Enter", steps[2].Key);
    }

    [Fact]
    public void Parse_PlainText_ReturnsSingleTextStep()
    {
        var steps = KeystrokeSequenceParser.Parse("09.07.2026");

        var step = Assert.Single(steps);
        Assert.Equal(KeystrokeStepType.Text, step.Type);
        Assert.Equal("09.07.2026", step.Text);
    }

    [Fact]
    public void Parse_LegacyModifierSyntax_TreatedAsPlainText()
    {
        // Eski yanıltıcı '^s' değeri artık düz metindir (chord olarak yorumlanmaz).
        var steps = KeystrokeSequenceParser.Parse("^s");

        var step = Assert.Single(steps);
        Assert.Equal(KeystrokeStepType.Text, step.Type);
        Assert.Equal("^s", step.Text);
    }

    [Fact]
    public void Parse_Modifiers_AreNormalizedToLowercase()
    {
        var steps = KeystrokeSequenceParser.Parse(
            """[ { "type": "chord", "modifiers": ["Ctrl", "SHIFT"], "key": "End" } ]""");

        Assert.Equal(new[] { "ctrl", "shift" }, steps[0].Modifiers);
        Assert.Equal("End", steps[0].Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Empty_ThrowsBusiness(string raw)
    {
        Assert.Throws<BusinessException>(() => KeystrokeSequenceParser.Parse(raw));
    }

    [Fact]
    public void Parse_UnknownKey_ThrowsBusiness()
    {
        Assert.Throws<BusinessException>(() => KeystrokeSequenceParser.Parse(
            """[ { "type": "chord", "key": "Banana" } ]"""));
    }

    [Fact]
    public void Parse_ChordWithoutKey_ThrowsBusiness()
    {
        Assert.Throws<BusinessException>(() => KeystrokeSequenceParser.Parse(
            """[ { "type": "chord", "modifiers": ["ctrl"] } ]"""));
    }

    [Fact]
    public void Parse_InvalidModifier_ThrowsBusiness()
    {
        Assert.Throws<BusinessException>(() => KeystrokeSequenceParser.Parse(
            """[ { "type": "chord", "modifiers": ["hyper"], "key": "A" } ]"""));
    }

    [Fact]
    public void Parse_EmptyTextStep_ThrowsBusiness()
    {
        Assert.Throws<BusinessException>(() => KeystrokeSequenceParser.Parse(
            """[ { "type": "text", "text": "" } ]"""));
    }

    [Fact]
    public void Parse_NegativeWait_ClampedToZero()
    {
        var steps = KeystrokeSequenceParser.Parse(
            """[ { "type": "text", "text": "x", "waitMs": -5 } ]""");

        Assert.Equal(0, steps[0].WaitMs);
    }
}
