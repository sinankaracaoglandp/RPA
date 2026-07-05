namespace RPA.Infrastructure.Scheduling;

using System;
using Cronos;

/// <summary>
/// Cron ifadelerini ayrıştırıp zaman dilimi farkındalıklı bir sonraki çalışma zamanını hesaplar
/// (Task 3.3, Spec Bölüm 7). Cronos kütüphanesi (minimal bağımlılık) üzerine ince bir katman;
/// hatalı cron/timezone durumlarında istisna fırlatmak yerine null döner (Scheduler tarafında
/// güvenli şekilde atlanabilsin diye).
/// </summary>
public static class CronScheduleCalculator
{
    /// <summary>
    /// Verilen cron ifadesinin, <paramref name="fromUtc"/>'den sonraki bir sonraki çalışma
    /// zamanını UTC olarak döner. Cron/timezone geçersizse null döner.
    /// </summary>
    public static DateTime? GetNextOccurrence(string cronExpression, string timeZoneId, DateTime fromUtc)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return null;

        CronExpression parsed;
        try
        {
            parsed = CronExpression.Parse(cronExpression);
        }
        catch (CronFormatException)
        {
            return null;
        }

        TimeZoneInfo tz;
        try
        {
            tz = string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId == "UTC"
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }

        var fromUtcSafe = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var next = parsed.GetNextOccurrence(fromUtcSafe, tz, inclusive: false);
        return next.HasValue ? DateTime.SpecifyKind(next.Value, DateTimeKind.Utc) : null;
    }

    /// <summary>
    /// Son ateşlemeden (<paramref name="lastFireUtc"/>) sonraki bir sonraki çalışma zamanının
    /// <paramref name="nowUtc"/>'ye eşit veya öncesinde olup olmadığını (yani tetikleyicinin ateşlenmesi
    /// gerekip gerekmediğini) döner.
    /// </summary>
    public static bool IsDue(string cronExpression, string timeZoneId, DateTime lastFireUtc, DateTime nowUtc)
    {
        var next = GetNextOccurrence(cronExpression, timeZoneId, lastFireUtc);
        return next.HasValue && next.Value <= DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
    }
}
