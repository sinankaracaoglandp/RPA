namespace RPA.Infrastructure.Tests;

using System;
using RPA.Infrastructure.Scheduling;

/// <summary>
/// Task 3.3 — Cron ifadesi ayrıştırma + zaman dilimi farkındalıklı bir sonraki çalışma zamanı
/// hesaplama testleri (Spec Bölüm 7). Cronos kütüphanesi üzerine ince bir katman.
/// </summary>
public class CronScheduleCalculatorTests
{
    [Fact]
    public void GetNextOccurrence_Daily_ReturnsNextDaySameTime()
    {
        // Her gün 09:00 UTC.
        var from = new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc); // 09:00 geçmiş
        var next = CronScheduleCalculator.GetNextOccurrence("0 9 * * *", "UTC", from);

        Assert.NotNull(next);
        Assert.Equal(new DateTime(2026, 7, 6, 9, 0, 0, DateTimeKind.Utc), next!.Value);
    }

    [Fact]
    public void GetNextOccurrence_Weekly_ReturnsNextMatchingWeekday()
    {
        // Her Pazartesi 08:00 UTC. 2026-07-05 Pazar.
        var from = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var next = CronScheduleCalculator.GetNextOccurrence("0 8 * * MON", "UTC", from);

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Monday, next!.Value.DayOfWeek);
        Assert.Equal(new DateTime(2026, 7, 6, 8, 0, 0, DateTimeKind.Utc), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_TimeZoneAware_ConvertsToUtc()
    {
        // Europe/Istanbul (UTC+3, DST yok Cronos'ta IANA kullanılır) 09:00 yerel -> 06:00 UTC.
        var from = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var next = CronScheduleCalculator.GetNextOccurrence("0 9 * * *", "Turkey Standard Time", from);

        Assert.NotNull(next);
        Assert.Equal(DateTimeKind.Utc, next!.Value.Kind);
        Assert.Equal(6, next.Value.Hour);
    }

    [Fact]
    public void GetNextOccurrence_InvalidCron_ReturnsNull()
    {
        var next = CronScheduleCalculator.GetNextOccurrence("not a cron", "UTC", DateTime.UtcNow);
        Assert.Null(next);
    }

    [Fact]
    public void GetNextOccurrence_UnknownTimeZone_ReturnsNull()
    {
        var next = CronScheduleCalculator.GetNextOccurrence("0 9 * * *", "Not/AZone", DateTime.UtcNow);
        Assert.Null(next);
    }

    [Fact]
    public void IsDue_ReturnsTrueWhenNextOccurrenceAtOrBeforeNow()
    {
        var lastFire = new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 7, 5, 9, 0, 1, DateTimeKind.Utc);

        Assert.True(CronScheduleCalculator.IsDue("0 9 * * *", "UTC", lastFire, now));
    }

    [Fact]
    public void IsDue_ReturnsFalseWhenNotYetDue()
    {
        var lastFire = new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(CronScheduleCalculator.IsDue("0 9 * * *", "UTC", lastFire, now));
    }
}
