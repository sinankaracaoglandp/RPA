namespace RPA.Infrastructure.Workflow.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;
using static RPA.Infrastructure.Workflow.Expressions.FunctionArgs;

/// <summary>Tarih/zaman ifade fonksiyonları (kategori "Tarih"). Varsayılan kültür tr-TR.</summary>
internal static class DateFunctions
{
    private const string Cat = "Tarih";

    public static IReadOnlyList<ExpressionFunction> All => new[]
    {
        Fn("Now", "date", new List<ExpressionFunctionParam>(), "Şu anki tarih-saat.", "Now()",
            _ => DateTime.Now),
        Fn("Today", "date", new List<ExpressionFunctionParam>(), "Bugünün tarihi (saat 00:00).", "Today()",
            _ => DateTime.Today),
        Fn("AddDays", "date", new() { P("tarih", "date"), P("gun", "int") }, "Tarihe gün ekler.", "AddDays(Now(), 7)",
            a => AsDate("AddDays", a[0]).AddDays(AsInt("AddDays", a[1]))),
        Fn("AddMonths", "date", new() { P("tarih", "date"), P("ay", "int") }, "Tarihe ay ekler.", "AddMonths(Now(), 1)",
            a => AsDate("AddMonths", a[0]).AddMonths(AsInt("AddMonths", a[1]))),
        Fn("AddYears", "date", new() { P("tarih", "date"), P("yil", "int") }, "Tarihe yıl ekler.", "AddYears(Now(), 1)",
            a => AsDate("AddYears", a[0]).AddYears(AsInt("AddYears", a[1]))),
        Fn("AddHours", "date", new() { P("tarih", "date"), P("saat", "int") }, "Tarihe saat ekler.", "AddHours(Now(), 3)",
            a => AsDate("AddHours", a[0]).AddHours(AsInt("AddHours", a[1]))),
        Fn("AddMinutes", "date", new() { P("tarih", "date"), P("dakika", "int") }, "Tarihe dakika ekler.", "AddMinutes(Now(), 30)",
            a => AsDate("AddMinutes", a[0]).AddMinutes(AsInt("AddMinutes", a[1]))),
        Fn("Format", "string", new() { P("tarih", "date"), P("desen", "string"), P("kültür", "string", true) },
            "Tarihi verilen desene göre biçimler.", "Format(Now(), \"dd.MM.yyyy\")",
            a => AsDate("Format", a[0]).ToString(AsString(a[1]), Culture("Format", a, 2))),
        Fn("ToDate", "date", new() { P("metin", "string"), P("desen", "string", true), P("kültür", "string", true) },
            "Metni tarihe çevirir.", "ToDate(\"15.01.2026\", \"dd.MM.yyyy\")",
            a => ParseDate(a)),
        Fn("Year", "int", new() { P("tarih", "date") }, "Yıl bileşeni.", "Year(Now())", a => (long)AsDate("Year", a[0]).Year),
        Fn("Month", "int", new() { P("tarih", "date") }, "Ay bileşeni.", "Month(Now())", a => (long)AsDate("Month", a[0]).Month),
        Fn("Day", "int", new() { P("tarih", "date") }, "Gün bileşeni.", "Day(Now())", a => (long)AsDate("Day", a[0]).Day),
        Fn("DayOfWeek", "int", new() { P("tarih", "date") }, "Haftanın günü (0=Pazar).", "DayOfWeek(Now())",
            a => (long)(int)AsDate("DayOfWeek", a[0]).DayOfWeek),
        Fn("DateDiffDays", "int", new() { P("a", "date"), P("b", "date") }, "İki tarih arası gün farkı (a-b).", "DateDiffDays(a, b)",
            a => (long)Math.Round((AsDate("DateDiffDays", a[0]) - AsDate("DateDiffDays", a[1])).TotalDays)),
    };

    private static object ParseDate(IReadOnlyList<object?> a)
    {
        var s = AsString(a[0]);
        var culture = Culture("ToDate", a, 2);
        if (a.Count >= 2 && a[1] is not null)
        {
            var pattern = AsString(a[1]);
            if (DateTime.TryParseExact(s, pattern, culture, DateTimeStyles.None, out var exact)) { return exact; }
            throw ExpressionErrors.Business($"ToDate: '{s}' '{pattern}' desenine uymuyor.");
        }
        if (DateTime.TryParse(s, culture, DateTimeStyles.None, out var p)) { return p; }
        throw ExpressionErrors.Business($"ToDate: '{s}' tarihe çevrilemedi.");
    }

    private static ExpressionFunction Fn(string name, string ret, List<ExpressionFunctionParam> ps,
        string desc, string ex, Func<IReadOnlyList<object?>, object?> invoke)
        => new(new ExpressionFunctionInfo(name, Cat, ret, ps, desc, ex), invoke);
}
