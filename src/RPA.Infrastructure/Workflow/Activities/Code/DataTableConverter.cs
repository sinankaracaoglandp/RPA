namespace RPA.Infrastructure.Workflow.Activities.Code;

using System.Collections;
using System.Data;

/// <summary>
/// Platformun satır-listesi gösterimi (<c>List&lt;Dictionary&lt;string, object?&gt;&gt;</c> — SAP GUI
/// GridRead, NCo ReadTable, BAPI tabloları) ile gerçek <see cref="System.Data.DataTable"/> arasında
/// dönüşüm. Böylece kullanıcı verisini .NET DataTable API'si (filtre, sıralama, tipli sütun) ve
/// <c>System.InvokeCode</c> C# aktivitesi ile işleyebilir.
/// </summary>
public static class DataTableConverter
{
    /// <summary>Satır listesini (veya zaten DataTable ise kendisini) bir DataTable'a çevirir.</summary>
    public static DataTable ToDataTable(object? rows)
    {
        if (rows is DataTable existing)
        {
            return existing;
        }

        var table = new DataTable();
        var materialized = AsRows(rows).ToList();

        // Sütunları ilk görülme sırasına göre topla.
        foreach (var row in materialized)
        {
            foreach (var key in row.Keys)
            {
                if (!table.Columns.Contains(key))
                {
                    table.Columns.Add(key, typeof(object));
                }
            }
        }

        foreach (var row in materialized)
        {
            var dr = table.NewRow();
            foreach (var kv in row)
            {
                dr[kv.Key] = kv.Value ?? DBNull.Value;
            }
            table.Rows.Add(dr);
        }

        return table;
    }

    /// <summary>DataTable'ı platformun satır-listesi gösterimine çevirir.</summary>
    public static List<Dictionary<string, object?>> ToRows(DataTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = new List<Dictionary<string, object?>>(table.Rows.Count);
        foreach (DataRow dr in table.Rows)
        {
            var row = new Dictionary<string, object?>(table.Columns.Count);
            foreach (DataColumn col in table.Columns)
            {
                var value = dr[col];
                row[col.ColumnName] = value == DBNull.Value ? null : value;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> AsRows(object? rows)
    {
        switch (rows)
        {
            case null:
                yield break;
            case IEnumerable<Dictionary<string, object?>> typed:
                foreach (var r in typed) { yield return r; }
                yield break;
            case IEnumerable<IReadOnlyDictionary<string, object?>> ro:
                foreach (var r in ro) { yield return r; }
                yield break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    if (item is IDictionary<string, object?> dict)
                    {
                        yield return new Dictionary<string, object?>(dict);
                    }
                    else if (item is IDictionary raw)
                    {
                        var converted = new Dictionary<string, object?>();
                        foreach (DictionaryEntry entry in raw)
                        {
                            converted[entry.Key?.ToString() ?? string.Empty] = entry.Value;
                        }
                        yield return converted;
                    }
                }
                yield break;
            default:
                yield break;
        }
    }
}
