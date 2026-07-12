namespace RPA.Infrastructure.Tests;

using System.Data;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow.Activities.Code;
using Xunit;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

public class CodeActivityTests
{
    private sealed class FakeCtx : IActivityExecutionContext
    {
        private readonly Dictionary<string, object?> _vars;
        public FakeCtx(Dictionary<string, object?> vars) => _vars = vars;

        public T GetVariable<T>(string name)
        {
            if (_vars.TryGetValue(name, out var v) && v is T t) return t;
            if (v is null) return default!;
            return (T)v!;
        }

        public void SetVariable(string name, object? value) => _vars[name] = value;
        public Task<string> GetCredentialAsync(string name) => Task.FromResult("");
        public Task<string?> GetAssetAsync(string name) => Task.FromResult<string?>(null);
        public void Log(string msg, LogLevel level = LogLevel.Information) { }
        public string TimeZone => "UTC";
        public Guid JobRunId => Guid.Empty;
    }

    private static List<Dictionary<string, object?>> SampleRows() => new()
    {
        new() { ["MATNR"] = "100-100", ["MENGE"] = 10 },
        new() { ["MATNR"] = "100-200", ["MENGE"] = 50 },
    };

    [Fact]
    public void DataTableConverter_RoundTrips()
    {
        var table = DataTableConverter.ToDataTable(SampleRows());

        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.Columns.Contains("MATNR"));
        Assert.True(table.Columns.Contains("MENGE"));
        Assert.Equal("100-200", table.Rows[1]["MATNR"]);

        var rows = DataTableConverter.ToRows(table);
        Assert.Equal(2, rows.Count);
        Assert.Equal("100-100", rows[0]["MATNR"]);
    }

    [Fact]
    public async Task DataToDataTable_ProducesRealDataTable()
    {
        var ctx = new FakeCtx(new() { ["rows"] = SampleRows() });
        var result = await new DataToDataTableActivity().ExecuteAsync(ctx);

        var table = Assert.IsType<DataTable>(result["table"]);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Columns.Count);
    }

    [Fact]
    public async Task InvokeCsharp_ReadsRows_ComputesViaDataTable_WritesOutput()
    {
        var ctx = new FakeCtx(new()
        {
            ["rows"] = SampleRows(),
            ["code"] = "var dt = ToDataTable(Get(\"rows\"));\n"
                     + "Set(\"adet\", dt.Rows.Count);\n"
                     + "Set(\"ilk\", (string)dt.Rows[0][\"MATNR\"]);",
        });

        var result = await new InvokeCsharpActivity().ExecuteAsync(ctx);

        Assert.Equal(2, result["adet"]);
        Assert.Equal("100-100", result["ilk"]);
    }

    [Fact]
    public async Task InvokeCsharp_CompileError_ThrowsBusiness()
    {
        var ctx = new FakeCtx(new() { ["code"] = "this is not valid c# @@@" });

        await Assert.ThrowsAsync<BusinessException>(() => new InvokeCsharpActivity().ExecuteAsync(ctx));
    }
}
