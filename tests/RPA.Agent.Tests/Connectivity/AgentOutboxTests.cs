namespace RPA.Agent.Tests.Connectivity;

using RPA.Agent.Connectivity;
using Xunit;

/// <summary>
/// Task 6 — sınırlı, kalıcı ajan outbox'ı: anahtar tabanlı idempotent flush + açık kapasite taşması.
/// </summary>
public sealed class AgentOutboxTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rpa-outbox-tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "outbox.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temizlik testi düşürmez */ }
    }

    [Fact]
    public void Enqueue_SameKeyTwice_StoresSingleEntry()
    {
        var outbox = new AgentOutbox(FilePath, capacity: 10);

        outbox.Enqueue("log:1", "payload");
        outbox.Enqueue("log:1", "payload");

        Assert.Single(outbox.Peek());
        Assert.Equal(1, outbox.Count);
    }

    [Fact]
    public void Acknowledge_IsIdempotent_AcrossRepeatedFlushes()
    {
        var outbox = new AgentOutbox(FilePath, capacity: 10);
        outbox.Enqueue("log:1", "a");
        outbox.Enqueue("log:2", "b");

        outbox.Acknowledge(["log:1"]);
        outbox.Acknowledge(["log:1"]);           // tekrar flush — no-op
        outbox.Acknowledge(["bilinmeyen"]);      // hiç görülmemiş anahtar — no-op

        var remaining = outbox.Peek();
        Assert.Single(remaining);
        Assert.Equal("log:2", remaining[0].Key);
    }

    [Fact]
    public void Entries_SurviveRestart_AndKeysStayIdempotent()
    {
        var first = new AgentOutbox(FilePath, capacity: 10);
        first.Enqueue("result:1", "payload");

        var reopened = new AgentOutbox(FilePath, capacity: 10);
        Assert.Single(reopened.Peek());
        Assert.Equal("payload", reopened.Peek()[0].Payload);

        reopened.Enqueue("result:1", "payload"); // yeniden başlatma sonrası aynı anahtar
        Assert.Single(reopened.Peek());

        reopened.Acknowledge(["result:1"]);
        Assert.Empty(new AgentOutbox(FilePath, capacity: 10).Peek());
    }

    [Fact]
    public void Enqueue_BeyondCapacity_ThrowsExplicitOverflow()
    {
        var outbox = new AgentOutbox(FilePath, capacity: 2);
        outbox.Enqueue("k1", "a");
        outbox.Enqueue("k2", "b");

        var ex = Assert.Throws<AgentOutboxOverflowException>(() => outbox.Enqueue("k3", "c"));

        Assert.Equal(2, ex.Capacity);
        Assert.Equal(2, outbox.Count); // taşma sessizce en eskiyi düşürmez
    }

    [Fact]
    public void Enqueue_DuplicateKeyAtCapacity_DoesNotOverflow()
    {
        var outbox = new AgentOutbox(FilePath, capacity: 2);
        outbox.Enqueue("k1", "a");
        outbox.Enqueue("k2", "b");

        outbox.Enqueue("k1", "a"); // mevcut anahtar kapasiteyi tüketmez

        Assert.Equal(2, outbox.Count);
    }
}
