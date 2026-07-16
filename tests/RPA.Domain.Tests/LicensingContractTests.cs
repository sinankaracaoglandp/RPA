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
        var payload = OfflineLicensePayload.Create("LIC-1", 1, "ACME", "install-1", "ABC", 5,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2027-01-01T00:00:00Z"), ["Studio"]);

        Assert.Equal(5, payload.MaxActivatedAgents);
        Assert.Equal(1, payload.Revision);
    }
}
