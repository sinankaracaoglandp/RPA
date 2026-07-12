namespace RPA.Infrastructure.Tests.UISpy;

using RPA.Infrastructure.UISpy;
using Xunit;

public class SpyElementMessageTests
{
    [Fact]
    public void FromImage_SetsKindAndPayload()
    {
        var sessionId = Guid.NewGuid();
        var msg = SpyElementMessage.FromImage("BASE64PNG", "{\"x\":10,\"y\":20,\"width\":30,\"height\":40}", sessionId);

        Assert.Equal("image", msg.Kind);
        Assert.Equal(sessionId, msg.SessionId);
        Assert.Equal("BASE64PNG", msg.ImageBase64);
        Assert.Equal("{\"x\":10,\"y\":20,\"width\":30,\"height\":40}", msg.Region);
    }
}
