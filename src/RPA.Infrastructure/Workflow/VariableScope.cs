namespace RPA.Infrastructure.Workflow;

using Newtonsoft.Json.Linq;

/// <summary>
/// Değişken scope zinciri — global (workflow), component (izole çağrı) ve node-local
/// scope'ları yönetir. Çözümleme en üstteki (en iç) scope'tan tabana (global) doğru yürür.
/// Spec Bölüm 5.2 — değişken izolasyonu.
/// </summary>
public sealed class VariableScope
{
    // Taban = global; her PushScope yeni bir üst katman ekler.
    private readonly List<Dictionary<string, object?>> _scopes = new()
    {
        new Dictionary<string, object?>(StringComparer.Ordinal),
    };

    /// <summary>Yeni bir izole scope başlatır (örn. "component-abc").</summary>
    public void PushScope(string scopeName)
    {
        _ = scopeName; // İsim tanılama/loglama için; şu an yalnızca katman eklenir.
        _scopes.Add(new Dictionary<string, object?>(StringComparer.Ordinal));
    }

    /// <summary>En üstteki scope'u kaldırır (global scope korunur).</summary>
    public void PopScope()
    {
        if (_scopes.Count > 1)
        {
            _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    /// <summary>Değişkeni okur; bulunamazsa <c>default(T)</c> döner.</summary>
    public T GetVariable<T>(string name)
    {
        return TryGetVariable(name, out var value)
            ? ConvertValue<T>(value)
            : default!;
    }

    /// <summary>Değişkeni ayarlar. Mevcutsa bulunduğu scope'ta günceller; değilse en üst scope'a yazar.</summary>
    public void SetVariable(string name, object? value)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].ContainsKey(name))
            {
                _scopes[i][name] = Normalize(value);
                return;
            }
        }
        _scopes[^1][name] = Normalize(value);
    }

    /// <summary>Değişkeni yalnızca en üst (en iç) scope'a yazar — döngü/component-local için.</summary>
    public void SetLocalVariable(string name, object? value)
    {
        _scopes[^1][name] = Normalize(value);
    }

    /// <summary>Global (taban) scope'a yazar — workflow argümanları/değişkenleri için.</summary>
    public void SetGlobalVariable(string name, object? value)
    {
        _scopes[0][name] = Normalize(value);
    }

    /// <summary>Scope zincirinde değişkeni arar.</summary>
    public bool TryGetVariable(string name, out object? value)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(name, out value))
            {
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>JToken'ları .NET tiplerine indirger (saklama tutarlılığı için).</summary>
    private static object? Normalize(object? value)
    {
        return value is JToken token ? JTokenToNative(token) : value;
    }

    internal static object? JTokenToNative(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => null,
            JTokenType.Integer => (long)token,
            JTokenType.Float => (double)token,
            JTokenType.Boolean => (bool)token,
            JTokenType.String or JTokenType.Guid or JTokenType.Uri => (string?)token,
            JTokenType.Date => (DateTime)token,
            // Diziler/nesneler tip bilgisini korumak için JToken olarak bırakılır.
            _ => token,
        };
    }

    private static T ConvertValue<T>(object? value)
    {
        if (value is null)
        {
            return default!;
        }
        if (value is T typed)
        {
            return typed;
        }
        if (value is JToken token)
        {
            try
            {
                return token.ToObject<T>()!;
            }
            catch
            {
                // Aşağıdaki genel dönüşüme düş.
            }
        }
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            return (T)Convert.ChangeType(value, targetType);
        }
        catch
        {
            return default!;
        }
    }
}
