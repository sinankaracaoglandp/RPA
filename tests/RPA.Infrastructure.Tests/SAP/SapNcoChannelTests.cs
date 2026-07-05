namespace RPA.Infrastructure.Tests.SAP;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.SAP;
using RPA.Infrastructure.SAP.Nco;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// SAP NCo (programatik) veri kanalı testleri (Task 4.2). Spec Bölüm 5.2, 6.
/// Fiziksel RFC bağlantısı fake/mock'lanır — gerçek SAP DEP entegrasyon testi kapsam dışıdır.
/// Arrange-Act-Assert deseni.
/// </summary>
public class SapNcoChannelTests
{
    // =============================================================== fakes

    /// <summary>Yapılandırılabilir fake RFC bağlantısı — sonuç döner veya iletişim hatası fırlatır.</summary>
    private sealed class FakeRfcConnection : IRfcConnection
    {
        private readonly Func<string, RfcInvokeResult> _responder;
        public FakeRfcConnection(string id, Func<string, RfcInvokeResult> responder)
        {
            Id = id;
            _responder = responder;
        }

        public string Id { get; }
        public bool IsAlive { get; set; } = true;
        public int InvokeCount { get; private set; }
        public bool Disposed { get; private set; }
        public string? LastFunction { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastImporting { get; private set; }
        public IReadOnlyDictionary<string, List<Dictionary<string, object?>>>? LastTables { get; private set; }

        public Task<RfcInvokeResult> InvokeAsync(
            string functionName,
            IReadOnlyDictionary<string, object?> importing,
            IReadOnlyDictionary<string, List<Dictionary<string, object?>>> tables,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvokeCount++;
            LastFunction = functionName;
            LastImporting = importing;
            LastTables = tables;
            return Task.FromResult(_responder(functionName));
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>Fake fabrika — kaç bağlantı açtığını sayar, ilk N açılışta iletişim hatası fırlatabilir.</summary>
    private sealed class FakeFactory : IRfcConnectionFactory
    {
        private readonly Func<string, RfcInvokeResult> _responder;
        private int _openCount;
        public FakeFactory(Func<string, RfcInvokeResult>? responder = null)
        {
            _responder = responder ?? (_ => new RfcInvokeResult());
        }

        public int OpenAttempts { get; private set; }
        public int FailFirstNOpens { get; set; }
        public List<FakeRfcConnection> Created { get; } = new();

        public Task<IRfcConnection> OpenAsync(SapConnectionSettings settings, CancellationToken cancellationToken)
        {
            OpenAttempts++;
            if (OpenAttempts <= FailFirstNOpens)
            {
                throw new RfcCommunicationException("open failed (simulated)");
            }

            var conn = new FakeRfcConnection($"C{++_openCount}", _responder);
            Created.Add(conn);
            return Task.FromResult<IRfcConnection>(conn);
        }
    }

    private static SapConnectionSettings Settings() => new()
    {
        SystemId = "DEV",
        Client = "100",
        User = "TESTUSER",
        Password = "pw",
        Language = "TR",
    };

    private static SapConnectionPool NewPool(IRfcConnectionFactory factory, SapNcoPoolOptions? options = null) =>
        new(factory, Settings(), options ?? new SapNcoPoolOptions(),
            NullLogger<SapConnectionPool>.Instance);

    private static SapNcoChannel NewChannel(SapConnectionPool pool) =>
        new(pool, NullLogger<SapNcoChannel>.Instance);

    private static RfcInvokeResult Success() => new()
    {
        Tables = new()
        {
            ["RETURN"] = new() { new() { ["TYPE"] = "S", ["MESSAGE"] = "OK" } },
        },
    };

    private static RfcInvokeResult Error() => new()
    {
        Tables = new()
        {
            ["RETURN"] = new() { new() { ["TYPE"] = "E", ["MESSAGE"] = "Malzeme zaten mevcut" } },
        },
    };

    private static IActivityExecutionContext Ctx() => new TestActivityExecutionContext();

    // =============================================================== RfcStructure helper

    [Fact]
    public void RfcStructure_MapsAndReadsFields_CaseInsensitive()
    {
        var s = new RfcStructure().Set("MATNR", "M1").Set("WERKS", "1000");

        Assert.Equal("M1", s["matnr"]);
        Assert.Equal("1000", s.GetString("WERKS"));
        Assert.Equal(2, s.Count);
        Assert.Equal("", s.GetString("MISSING"));
    }

    [Fact]
    public void RfcStructure_BuildOptions_SplitsWhereInto72CharLines()
    {
        var where = string.Join(" AND ", Enumerable.Repeat("FIELD = 'X'", 20));

        var options = RfcStructure.BuildOptions(where);

        Assert.NotEmpty(options);
        Assert.All(options, o => Assert.True(((string)o["TEXT"]!).Length <= RfcStructure.ReadTableOptionLineLength));
    }

    [Fact]
    public void RfcStructure_BuildOptions_SingleOverlongToken_IsHardSplit()
    {
        var longToken = new string('X', 200); // no spaces → forced fixed-width split

        var options = RfcStructure.BuildOptions(longToken);

        Assert.True(options.Count >= 3);
        Assert.All(options, o => Assert.True(((string)o["TEXT"]!).Length <= RfcStructure.ReadTableOptionLineLength));
        Assert.Equal(200, options.Sum(o => ((string)o["TEXT"]!).Length));
    }

    [Fact]
    public void RfcStructure_BuildOptions_EmptyWhere_ReturnsEmpty()
    {
        Assert.Empty(RfcStructure.BuildOptions(null));
        Assert.Empty(RfcStructure.BuildOptions("   "));
    }

    [Fact]
    public void RfcStructure_ParseReadTableResult_SlicesFixedWidthRows()
    {
        var result = new RfcInvokeResult
        {
            Tables = new()
            {
                ["FIELDS"] = new()
                {
                    new() { ["FIELDNAME"] = "MATNR", ["OFFSET"] = "0", ["LENGTH"] = "5" },
                    new() { ["FIELDNAME"] = "WERKS", ["OFFSET"] = "5", ["LENGTH"] = "4" },
                },
                ["DATA"] = new()
                {
                    new() { ["WA"] = "M0001100 " },
                    new() { ["WA"] = "M00022000" },
                },
            },
        };

        var rows = RfcStructure.ParseReadTableResult(result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("M0001", rows[0]["MATNR"]);
        Assert.Equal("100", rows[0]["WERKS"]);
        Assert.Equal("M0002", rows[1]["MATNR"]);
        Assert.Equal("2000", rows[1]["WERKS"]);
    }

    [Fact]
    public void RfcStructure_ExtractReturn_WorstTypeWins_TableAndExport()
    {
        var result = new RfcInvokeResult
        {
            Tables = new()
            {
                ["RETURN"] = new()
                {
                    new() { ["TYPE"] = "S", ["MESSAGE"] = "ok" },
                    new() { ["TYPE"] = "E", ["MESSAGE"] = "boom" },
                },
            },
        };

        var ret = RfcStructure.ExtractReturn(result);

        Assert.Equal('E', ret.Type);
        Assert.True(ret.IsError);
        Assert.Equal("boom", ret.Message);
    }

    [Fact]
    public void RfcStructure_ToCallResult_SuccessWhenNoError()
    {
        var callResult = RfcStructure.ToCallResult(Success());

        Assert.True(callResult.Success);
        Assert.Equal('S', callResult.ReturnType);
        Assert.True(callResult.TableOutputs.ContainsKey("RETURN"));
    }

    // =============================================================== SapConnectionPool

    [Fact]
    public async Task Pool_Borrow_OpensConnection_AndTracksActive()
    {
        var factory = new FakeFactory();
        using var pool = NewPool(factory);

        var conn = await pool.BorrowAsync();

        Assert.Equal(1, pool.ActiveCount);
        Assert.Equal(0, pool.IdleCount);
        Assert.Equal(1, factory.OpenAttempts);
        pool.Return(conn);
    }

    [Fact]
    public async Task Pool_Return_ReusesIdleConnection()
    {
        var factory = new FakeFactory();
        using var pool = NewPool(factory);

        var first = await pool.BorrowAsync();
        pool.Return(first);
        Assert.Equal(1, pool.IdleCount);

        var second = await pool.BorrowAsync();

        Assert.Same(first, second);
        Assert.Equal(1, factory.OpenAttempts); // no new connection opened
    }

    [Fact]
    public async Task Pool_DeadIdleConnection_IsDiscardedAndReplaced()
    {
        var factory = new FakeFactory();
        using var pool = NewPool(factory);

        var first = (FakeRfcConnection)await pool.BorrowAsync();
        first.IsAlive = false;
        pool.Return(first); // dead → not pooled

        Assert.Equal(0, pool.IdleCount);
        var second = await pool.BorrowAsync();

        Assert.NotSame(first, second);
        Assert.True(first.Disposed);
        pool.Return(second);
    }

    [Fact]
    public async Task Pool_Borrow_RetriesOnCommunicationFailure()
    {
        var factory = new FakeFactory { FailFirstNOpens = 2 };
        using var pool = NewPool(factory, new SapNcoPoolOptions { MaxRetries = 2 });

        var conn = await pool.BorrowAsync();

        Assert.NotNull(conn);
        Assert.Equal(3, factory.OpenAttempts); // 2 failures + 1 success
        pool.Return(conn);
    }

    [Fact]
    public async Task Pool_Borrow_ExhaustsRetries_ThrowsSystemException()
    {
        var factory = new FakeFactory { FailFirstNOpens = 5 };
        using var pool = NewPool(factory, new SapNcoPoolOptions { MaxRetries = 2 });

        await Assert.ThrowsAsync<SystemException>(() => pool.BorrowAsync());
    }

    [Fact]
    public async Task Pool_ReleasesCapacity_AfterFailedOpen()
    {
        var factory = new FakeFactory { FailFirstNOpens = 5 };
        using var pool = NewPool(factory, new SapNcoPoolOptions { MaxPoolSize = 1, MaxRetries = 0 });

        await Assert.ThrowsAsync<SystemException>(() => pool.BorrowAsync());

        // capacity must have been released so a subsequent borrow can proceed
        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public async Task Pool_WhenFull_BorrowTimesOut_ThrowsSystemException()
    {
        var factory = new FakeFactory();
        using var pool = NewPool(factory, new SapNcoPoolOptions { MaxPoolSize = 1, BorrowTimeoutSeconds = 0 });

        var held = await pool.BorrowAsync(); // occupies the only slot

        await Assert.ThrowsAsync<SystemException>(() => pool.BorrowAsync());
        pool.Return(held);
    }

    [Fact]
    public async Task Pool_ConcurrentBorrows_NeverExceedMaxPoolSize()
    {
        var factory = new FakeFactory();
        using var pool = NewPool(factory, new SapNcoPoolOptions { MaxPoolSize = 3, BorrowTimeoutSeconds = 5 });

        var conns = new List<IRfcConnection>();
        var tasks = Enumerable.Range(0, 3).Select(async _ =>
        {
            var c = await pool.BorrowAsync();
            lock (conns) { conns.Add(c); }
        });
        await Task.WhenAll(tasks);

        Assert.Equal(3, pool.ActiveCount);
        Assert.True(factory.OpenAttempts <= 3);
        foreach (var c in conns) pool.Return(c);
    }

    [Fact]
    public void Pool_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SapConnectionPool(null!, Settings(), new SapNcoPoolOptions(), NullLogger<SapConnectionPool>.Instance));
        Assert.Throws<ArgumentException>(() =>
            new SapConnectionPool(new FakeFactory(), Settings(), new SapNcoPoolOptions { MaxPoolSize = 0 },
                NullLogger<SapConnectionPool>.Instance));
    }

    // =============================================================== Channel: CallBapi / CallRfc

    [Fact]
    public async Task Channel_CallBapi_Success_ReturnsResult_AndReturnsConnectionToPool()
    {
        var factory = new FakeFactory(_ => Success());
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);

        var result = await channel.CallBapiAsync("BAPI_MATERIAL_SAVEDATA", new() { ["MATNR"] = "M1" });

        Assert.True(result.Success);
        Assert.Equal('S', result.ReturnType);
        Assert.Equal(1, pool.IdleCount);     // connection returned
        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public async Task Channel_CallBapi_ErrorReturn_ThrowsBusinessException()
    {
        var factory = new FakeFactory(_ => Error());
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);

        await Assert.ThrowsAsync<BusinessException>(
            () => channel.CallBapiAsync("BAPI_X", new()));

        Assert.Equal(1, pool.IdleCount); // still returned (business error, connection healthy)
    }

    [Fact]
    public async Task Channel_CallBapi_EmptyName_ThrowsBusinessException()
    {
        var channel = NewChannel(NewPool(new FakeFactory()));
        await Assert.ThrowsAsync<BusinessException>(() => channel.CallBapiAsync("  ", new()));
    }

    [Fact]
    public async Task Channel_CallRfc_PassesInputsAndTables()
    {
        var factory = new FakeFactory(_ => Success());
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);

        await channel.CallRfcAsync("Z_CUSTOM_RFC",
            new() { ["P1"] = "v1" },
            new() { ["T1"] = new() { new() { ["C"] = "1" } } });

        var conn = factory.Created[0];
        Assert.Equal("Z_CUSTOM_RFC", conn.LastFunction);
        Assert.Equal("v1", conn.LastImporting!["P1"]);
        Assert.True(conn.LastTables!.ContainsKey("T1"));
    }

    [Fact]
    public async Task Channel_Call_CommunicationFailure_RetriesThenSucceeds()
    {
        var calls = 0;
        RfcInvokeResult Responder(string _)
        {
            calls++;
            if (calls == 1) throw new RfcCommunicationException("conn reset");
            return Success();
        }

        var factory = new FakeFactory(Responder);
        using var pool = NewPool(factory, new SapNcoPoolOptions { MaxRetries = 2 });
        var channel = NewChannel(pool);

        var result = await channel.CallBapiAsync("BAPI_X", new());

        Assert.True(result.Success);
        Assert.Equal(2, calls);
        Assert.True(factory.Created[0].Disposed); // first connection discarded
    }

    [Fact]
    public async Task Channel_Call_CommunicationFailure_ExhaustsRetries_ThrowsSystemException()
    {
        var factory = new FakeFactory(_ => throw new RfcCommunicationException("always down"));
        using var pool = NewPool(factory, new SapNcoPoolOptions { MaxRetries = 2 });
        var channel = NewChannel(pool);

        await Assert.ThrowsAsync<SystemException>(() => channel.CallBapiAsync("BAPI_X", new()));
    }

    [Fact]
    public async Task Channel_Call_UnexpectedError_WrappedAsSystemException()
    {
        var factory = new FakeFactory(_ => throw new InvalidOperationException("boom"));
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);

        await Assert.ThrowsAsync<SystemException>(() => channel.CallBapiAsync("BAPI_X", new()));
    }

    [Fact]
    public async Task Channel_Call_Cancelled_Throws()
    {
        var factory = new FakeFactory(_ => Success());
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.CallBapiAsync("BAPI_X", new(), null, cts.Token));
    }

    // =============================================================== Channel: ReadTable

    [Fact]
    public async Task Channel_ReadTable_BuildsRfcReadTableRequest_AndParsesRows()
    {
        RfcInvokeResult Responder(string fn)
        {
            Assert.Equal("RFC_READ_TABLE", fn);
            return new RfcInvokeResult
            {
                Tables = new()
                {
                    ["FIELDS"] = new() { new() { ["FIELDNAME"] = "MATNR", ["OFFSET"] = "0", ["LENGTH"] = "5" } },
                    ["DATA"] = new() { new() { ["WA"] = "M0001" } },
                },
            };
        }

        var factory = new FakeFactory(Responder);
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);

        var rows = await channel.ReadTableAsync("mara", new() { "MATNR" }, "MATNR = 'M0001'");

        Assert.Single(rows);
        Assert.Equal("M0001", rows[0]["MATNR"]);
        var conn = factory.Created[0];
        Assert.Equal("MARA", conn.LastImporting!["QUERY_TABLE"]);
        Assert.NotEmpty(conn.LastTables!["OPTIONS"]);
        Assert.NotEmpty(conn.LastTables!["FIELDS"]);
    }

    [Fact]
    public async Task Channel_ReadTable_EmptyName_ThrowsBusinessException()
    {
        var channel = NewChannel(NewPool(new FakeFactory()));
        await Assert.ThrowsAsync<BusinessException>(() => channel.ReadTableAsync(""));
    }

    // =============================================================== Channel: Commit / Rollback

    [Fact]
    public async Task Channel_Commit_CallsBapiTransactionCommit_WithWait()
    {
        var factory = new FakeFactory(_ => Success());
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);

        var result = await channel.CommitAsync();

        Assert.True(result.Success);
        var conn = factory.Created[0];
        Assert.Equal("BAPI_TRANSACTION_COMMIT", conn.LastFunction);
        Assert.Equal("X", conn.LastImporting!["WAIT"]);
    }

    [Fact]
    public async Task Channel_Rollback_CallsBapiTransactionRollback()
    {
        var factory = new FakeFactory(_ => Success());
        using var pool = NewPool(factory);
        var channel = NewChannel(pool);

        await channel.RollbackAsync();

        Assert.Equal("BAPI_TRANSACTION_ROLLBACK", factory.Created[0].LastFunction);
    }

    [Fact]
    public async Task Channel_IsHealthy_TrueWhenPoolBorrowable_FalseOtherwise()
    {
        var okChannel = NewChannel(NewPool(new FakeFactory(_ => Success())));
        Assert.True(await okChannel.IsHealthyAsync());

        var badChannel = NewChannel(NewPool(
            new FakeFactory { FailFirstNOpens = 100 }, new SapNcoPoolOptions { MaxRetries = 0 }));
        Assert.False(await badChannel.IsHealthyAsync());
    }

    [Fact]
    public void Channel_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new SapNcoChannel(null!, NullLogger<SapNcoChannel>.Instance));
    }

    // =============================================================== Activities

    [Fact]
    public async Task CallBapiActivity_InvokesChannel_AndSetsOutputs()
    {
        var channel = new Mock<ISapDataChannel>();
        channel.Setup(c => c.CallBapiAsync("BAPI_X", It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<Dictionary<string, List<Dictionary<string, object?>>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SapCallResult { Success = true, Outputs = new() { ["MATNR"] = "M1" } });
        var activity = new SapNcoCallBapiActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("bapiName", "BAPI_X");
        ctx.SetVariable("inputs", new Dictionary<string, object?> { ["A"] = "1" });

        var result = await activity.ExecuteAsync(ctx);

        Assert.Equal(true, result["success"]);
        Assert.True(ctx.GetVariable<bool>("success"));
        Assert.NotNull(result["result"]);
    }

    [Fact]
    public async Task CallBapiActivity_MissingBapiName_ThrowsBusinessException()
    {
        var activity = new SapNcoCallBapiActivity(Mock.Of<ISapDataChannel>());
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(Ctx()));
    }

    [Fact]
    public async Task ReadTableActivity_SetsRowsAndCount()
    {
        var rows = new List<Dictionary<string, object?>> { new() { ["MATNR"] = "M1" } };
        var channel = new Mock<ISapDataChannel>();
        channel.Setup(c => c.ReadTableAsync("MARA", It.IsAny<List<string>?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        var activity = new SapNcoReadTableActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("tableName", "MARA");

        var result = await activity.ExecuteAsync(ctx);

        Assert.Same(rows, result["rows"]);
        Assert.Equal(1, result["rowCount"]);
    }

    [Fact]
    public async Task ReadTableActivity_MissingTableName_ThrowsBusinessException()
    {
        var activity = new SapNcoReadTableActivity(Mock.Of<ISapDataChannel>());
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(Ctx()));
    }

    [Fact]
    public async Task CommitActivity_Commit_WhenRollbackFalse()
    {
        var channel = new Mock<ISapDataChannel>();
        channel.Setup(c => c.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SapCallResult { Success = true });
        var activity = new SapNcoCommitActivity(channel.Object);

        var result = await activity.ExecuteAsync(Ctx());

        channel.Verify(c => c.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(c => c.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(true, result["success"]);
    }

    [Fact]
    public async Task CommitActivity_Rollback_WhenRollbackTrue()
    {
        var channel = new Mock<ISapDataChannel>();
        channel.Setup(c => c.RollbackAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SapCallResult { Success = true });
        var activity = new SapNcoCommitActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("rollback", true);

        await activity.ExecuteAsync(ctx);

        channel.Verify(c => c.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(c => c.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AllNcoActivities_ExposeMetadata_WithSapCategoryAndNcoCapability()
    {
        var channel = Mock.Of<ISapDataChannel>();
        IActivity[] activities =
        {
            new SapNcoCallBapiActivity(channel),
            new SapNcoReadTableActivity(channel),
            new SapNcoCommitActivity(channel),
        };

        foreach (var a in activities)
        {
            var meta = a.GetMetadata();
            Assert.StartsWith("Sap.Nco.", meta.ActivityId);
            Assert.Equal("SAP", meta.Category);
            Assert.Contains("sap-nco", meta.RequiredCapabilities);
        }
    }

    [Fact]
    public void NcoActivities_NullChannel_ThrowArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SapNcoCallBapiActivity(null!));
        Assert.Throws<ArgumentNullException>(() => new SapNcoReadTableActivity(null!));
        Assert.Throws<ArgumentNullException>(() => new SapNcoCommitActivity(null!));
    }
}
