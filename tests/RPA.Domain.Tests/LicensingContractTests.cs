using RPA.Domain.Enums;
using RPA.Domain.Licensing;

namespace RPA.Domain.Tests;

public class LicensingContractTests
{
    [Theory]
    [InlineData(AgentIdentityStatus.PendingActivation, false)]
    [InlineData(AgentIdentityStatus.Activated, true)]
    [InlineData(AgentIdentityStatus.Disabled, true)]
    [InlineData(AgentIdentityStatus.Deactivated, false)]
    public void AgentIdentityStatus_ConsumesSeat_AsSpecified(AgentIdentityStatus status, bool expected)
        => Assert.Equal(expected, status.ConsumesSeat());

    [Fact]
    public void OfflineLicensePayload_RequiresStableIdentityFields()
    {
        var payload = OfflineLicensePayload.Create("LIC-1", 1, "ACME", "ACME Sanayi A.S.", "enterprise", "install-1", "ABC", 5,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2027-01-01T00:00:00Z"), ["Studio"]);

        Assert.Equal(5, payload.MaxActivatedAgents);
        Assert.Equal(1, payload.Revision);
        Assert.Equal("ACME Sanayi A.S.", payload.CustomerName);
        Assert.Equal("enterprise", payload.Edition);
    }

    [Theory]
    [InlineData("", "enterprise")]
    [InlineData("  ", "enterprise")]
    [InlineData("ACME Sanayi A.S.", "")]
    [InlineData("ACME Sanayi A.S.", "  ")]
    public void OfflineLicensePayload_RequiresCustomerNameAndEdition(string customerName, string edition)
        => Assert.Throws<ArgumentException>(() => OfflineLicensePayload.Create(
            "LIC-1", 1, "ACME", customerName, edition, "install-1", "ABC", 5,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2027-01-01T00:00:00Z"), ["Studio"]));

    [Fact]
    public void OfflineLicensePayload_DefensivelyCopiesAndNormalizesFeatures()
    {
        var features = new List<string> { "Studio", "Agent", "Studio" };
        var payload = new OfflineLicensePayload(1, "LIC-1", 1, "ACME", "ACME Sanayi A.S.", "enterprise", "install-1", "ABC", 5,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2027-01-01T00:00:00Z"), features);

        features.Add("WebAPI");

        Assert.Equal(["Agent", "Studio"], payload.Features.ToArray());
    }

    [Fact]
    public void LicenseStatus_DefensivelyCopiesFeatures()
    {
        var features = new List<string> { "Studio" };
        var status = new LicenseStatus(true, true, "LIC-1", 1, "ACME", "ACME Sanayi A.S.", "enterprise",
            DateTimeOffset.Parse("2027-01-01T00:00:00Z"), 5, 1, features);

        features.Add("Agent");

        Assert.Equal(["Studio"], status.Features.ToArray());
        Assert.Equal("ACME Sanayi A.S.", status.CustomerName);
        Assert.Equal("enterprise", status.Edition);
    }
}
