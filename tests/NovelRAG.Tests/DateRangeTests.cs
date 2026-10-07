using NovelRAG.Application;
using Xunit;

namespace NovelRAG.Tests;

public sealed class DateRangeTests
{
    private static readonly TimeZoneInfo EastEight = TimeZoneInfo.CreateCustomTimeZone("Test UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    [Theory]
    [InlineData("2026-10-07", "2026-10-07", "2026-10-06T16:00:00Z", "2026-10-07T16:00:00Z")]
    [InlineData("2026-12-31", "2027-01-01", "2026-12-30T16:00:00Z", "2027-01-01T16:00:00Z")]
    [InlineData("2024-02-29", "2024-02-29", "2024-02-28T16:00:00Z", "2024-02-29T16:00:00Z")]
    [InlineData("2026-10-07", null, "2026-10-06T16:00:00Z", null)]
    [InlineData(null, "2026-10-07", null, "2026-10-07T16:00:00Z")]
    [InlineData(null, null, null, null)]
    public void Inclusive_local_dates_become_exclusive_utc_end(string? start, string? end, string? from, string? to)
    {
        var actual = DateRangeFilter.ToUtcBounds(ParseDate(start), ParseDate(end), EastEight);
        Assert.Equal(ParseInstant(from), actual.From);
        Assert.Equal(ParseInstant(to), actual.To);
        if (actual.From is { } lower) Assert.Equal(TimeSpan.Zero, lower.Offset);
        if (actual.To is { } upper) Assert.Equal(TimeSpan.Zero, upper.Offset);
    }

    [Fact]
    public void Reversed_range_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DateRangeFilter.ToUtcBounds(new(2026, 10, 8), new(2026, 10, 7), EastEight));
    }

    [Theory]
    [InlineData(3, 8, "2026-03-08T05:00:00Z", "2026-03-09T04:00:00Z")]
    [InlineData(11, 1, "2026-11-01T04:00:00Z", "2026-11-02T05:00:00Z")]
    public void Dst_day_end_is_next_local_midnight_not_twenty_four_hours(int month, int day, string from, string to)
    {
        var zone = ZoneWithTransitions(-5, 3, 8, 2, 11, 1, 2);
        var actual = DateRangeFilter.ToUtcBounds(new(2026, month, day), new(2026, month, day), zone);
        Assert.Equal(ParseInstant(from), actual.From);
        Assert.Equal(ParseInstant(to), actual.To);
    }

    [Fact]
    public void Invalid_midnight_is_rejected_for_either_boundary()
    {
        var zone = ZoneWithTransitions(2, 6, 1, 0, 9, 1, 1);
        Assert.Throws<ArgumentException>(() => DateRangeFilter.ToUtcBounds(new(2026, 6, 1), null, zone));
        Assert.Throws<ArgumentException>(() => DateRangeFilter.ToUtcBounds(null, new(2026, 5, 31), zone));
    }

    [Fact]
    public void Ambiguous_midnight_chooses_earlier_utc_instant()
    {
        var zone = ZoneWithTransitions(2, 6, 1, 0, 9, 1, 1);
        var actual = DateRangeFilter.ToUtcBounds(new(2026, 9, 1), null, zone);
        Assert.Equal(ParseInstant("2026-08-31T21:00:00Z"), actual.From);
        Assert.Equal(actual.From, DateRangeFilter.ToUtcBounds(null, new(2026, 8, 31), zone).To);
    }

    [Fact]
    public void Unrepresentable_boundaries_are_rejected_without_clamping()
    {
        Assert.Throws<ArgumentException>(() => DateRangeFilter.ToUtcBounds(null, DateOnly.MaxValue, TimeZoneInfo.Utc));
        Assert.Throws<ArgumentException>(() => DateRangeFilter.ToUtcBounds(DateOnly.MinValue, null, EastEight));
    }

    private static DateOnly? ParseDate(string? value) => value is null ? null : DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static DateTimeOffset? ParseInstant(string? value) => value is null ? null : DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static TimeZoneInfo ZoneWithTransitions(int hours, int startMonth, int startDay, int startHour, int endMonth, int endDay, int endHour)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, startHour, 0, 0), startMonth, startDay);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, endHour, 0, 0), endMonth, endDay);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Test DST", TimeSpan.FromHours(hours), "Test DST", "Standard", "Daylight", [rule]);
    }
}
