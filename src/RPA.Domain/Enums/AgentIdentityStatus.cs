namespace RPA.Domain.Enums;

public enum AgentIdentityStatus
{
    PendingActivation,
    Activated,
    Disabled,
    Deactivated,
}

public static class AgentIdentityStatusExtensions
{
    public static bool ConsumesSeat(this AgentIdentityStatus value) =>
        value is AgentIdentityStatus.Activated or AgentIdentityStatus.Disabled;
}
