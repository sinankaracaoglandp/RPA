namespace RPA.Infrastructure.Tests;

using RPA.Domain.Interfaces;
using Moq;

/// <summary>
/// Mock-based test implementation of IActivityExecutionContext.
/// Used by activity tests to provide variable storage and logging.
/// </summary>
public class TestActivityExecutionContext : IActivityExecutionContext
{
    private readonly Dictionary<string, object?> _variables = new();
    private readonly List<string> _logs = new();

    public string TimeZone => "UTC";
    public Guid JobRunId => Guid.NewGuid();

    public T GetVariable<T>(string name)
    {
        if (_variables.TryGetValue(name, out var value))
        {
            return (T?)(value ?? default!) ?? default!;
        }
        return default!;
    }

    public void SetVariable(string name, object? value)
    {
        _variables[name] = value;
    }

    public void Log(string message, LogLevel level = LogLevel.Information)
    {
        _logs.Add($"[{level}] {message}");
    }

    public async Task<string> GetCredentialAsync(string credentialName)
    {
        return "mock-credential";
    }

    public async Task<string?> GetAssetAsync(string assetName)
    {
        return "mock-asset";
    }

    public IReadOnlyList<string> GetLogs() => _logs.AsReadOnly();
}
