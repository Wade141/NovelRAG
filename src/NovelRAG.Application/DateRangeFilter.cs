namespace NovelRAG.Application;

public static class DateRangeFilter
{
    public static (DateTimeOffset? From, DateTimeOffset? To) ToUtcBounds(DateOnly? start, DateOnly? end, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (start > end) throw new ArgumentException("开始日期不能晚于结束日期。");
        try
        {
            return (start is { } first ? Midnight(first, zone) : null,
                end is { } last ? Midnight(last.AddDays(1), zone) : null);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException("日期超出可查询范围，请选择其他日期。");
        }
    }

    private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw new ArgumentException("所选日期的午夜在本地时区不存在，请选择其他日期。");
        // A repeated midnight starts at its earliest occurrence (the larger offset).
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
