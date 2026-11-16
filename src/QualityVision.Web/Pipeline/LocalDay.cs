namespace QualityVision.Web.Pipeline;

/// <summary>Local-calendar-day boundaries (shift reports are per local day, timestamps are stored in UTC).</summary>
public static class LocalDay
{
    /// <summary>Half-open range [start of <paramref name="from"/>, start of the day after <paramref name="to"/>).</summary>
    /// <param name="from">First local day (inclusive).</param>
    /// <param name="to">Last local day (inclusive).</param>
    /// <param name="time">Provides the local time zone.</param>
    public static (DateTimeOffset From, DateTimeOffset To) Range(DateOnly from, DateOnly to, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return (StartOf(from, time), StartOf(to.AddDays(1), time));
    }

    /// <summary>Range covering the single local day <paramref name="day"/>.</summary>
    /// <param name="day">The local day.</param>
    /// <param name="time">Provides the local time zone.</param>
    public static (DateTimeOffset From, DateTimeOffset To) Range(DateOnly day, TimeProvider time) => Range(day, day, time);

    private static DateTimeOffset StartOf(DateOnly day, TimeProvider time)
    {
        DateTime local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, time.LocalTimeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
