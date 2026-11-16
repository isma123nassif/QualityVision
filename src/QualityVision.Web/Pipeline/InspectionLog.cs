using Microsoft.Extensions.Logging.Abstractions;

using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;

namespace QualityVision.Web.Pipeline;

/// <summary>In-memory log of counted inspections: recent events and counters since app start.</summary>
public interface IInspectionLog
{
    /// <summary>Records one inspection and raises <see cref="EventAdded"/>.</summary>
    /// <param name="e">The counted inspection.</param>
    void Add(InspectionEvent e);

    /// <summary>The latest events, newest first (ring buffer of 500).</summary>
    /// <param name="max">Maximum number of events to return.</param>
    IReadOnlyList<InspectionEvent> Recent(int max = 50);

    /// <summary>Counters and latest events taken under one lock, so they are mutually consistent.</summary>
    /// <param name="max">Maximum number of recent events (newest first).</param>
    (InspectionCounts Counts, IReadOnlyList<InspectionEvent> Recent) Snapshot(int max);

    /// <summary>
    /// Replaces counters and the ring buffer with persisted state (called once at startup, before the pipeline runs).
    /// Raises no <see cref="EventAdded"/>, so seeded events are not persisted again.
    /// </summary>
    /// <param name="counts">Today's counts per verdict.</param>
    /// <param name="recent">Latest events of today, newest first.</param>
    void Seed(InspectionCounts counts, IReadOnlyList<InspectionEvent> recent);

    /// <summary>Counters for the current day (seeded from the database at startup).</summary>
    InspectionCounts Counts { get; }

    /// <summary>
    /// Counts per verdict for each of the last <paramref name="minutes"/> wall-clock minutes (oldest first, includes the
    /// current minute, empty minutes included with zero counts).
    /// </summary>
    /// <remarks>
    /// Bounded by the 500-entry ring buffer: above roughly 33 events/min, older minutes of a 15-minute window show
    /// too few events. Acceptable for the demo; persistence (M6, SQLite) is the natural fix.
    /// </remarks>
    /// <param name="minutes">Number of minutes in the window.</param>
    IReadOnlyList<MinuteBucket> PerMinute(int minutes = 15);

    /// <summary>Raised after an event was added (for M5 SignalR); invoked outside the internal lock.</summary>
    event Action<InspectionEvent>? EventAdded;
}

/// <summary>Counted inspections per verdict.</summary>
/// <param name="Ok">Good parts (i.O.).</param>
/// <param name="Nok">Defective parts (n.i.O.).</param>
/// <param name="Danger">Dangerous objects.</param>
/// <param name="Unknown">Objects without a rule.</param>
public readonly record struct InspectionCounts(int Ok, int Nok, int Danger, int Unknown)
{
    /// <summary>All inspected objects.</summary>
    public int Total => Ok + Nok + Danger + Unknown;

    /// <summary>
    /// Ausschussquote: NOK / (OK + NOK), 0 when no part was judged. Danger (safety event) and Unknown (not an
    /// inspected part) are separate KPIs and not part of the denominator.
    /// </summary>
    public double ScrapRate => Ok + Nok == 0 ? 0 : (double)Nok / (Ok + Nok);
}

/// <summary>Counts per verdict within one wall-clock minute.</summary>
/// <param name="Minute">Start of the minute (UTC).</param>
/// <param name="Ok">Good parts (i.O.).</param>
/// <param name="Nok">Defective parts (n.i.O.).</param>
/// <param name="Danger">Dangerous objects.</param>
/// <param name="Unknown">Objects without a rule.</param>
public readonly record struct MinuteBucket(DateTimeOffset Minute, int Ok, int Nok, int Danger, int Unknown);

/// <summary>Thread-safe singleton: written by <see cref="DetectionService"/>, read by the UI and endpoints.</summary>
public sealed class InspectionLog : IInspectionLog
{
    /// <summary>Number of events kept in memory.</summary>
    public const int Capacity = 500;

    private readonly ILogger<InspectionLog> _logger;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly InspectionEvent[] _buffer = new InspectionEvent[Capacity];
    private int _next;   // index the next event is written to
    private int _count;  // number of valid entries (≤ Capacity)
    private InspectionCounts _counts;

    /// <summary>Creates the log.</summary>
    /// <param name="logger">Logger for failing subscribers (optional; defaults to no logging).</param>
    /// <param name="time">Clock for <see cref="PerMinute"/> (optional; defaults to the system clock).</param>
    public InspectionLog(ILogger<InspectionLog>? logger = null, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<InspectionLog>.Instance;
    }

    public event Action<InspectionEvent>? EventAdded;

    public InspectionCounts Counts
    {
        get
        {
            lock (_gate)
            {
                return _counts;
            }
        }
    }

    public void Seed(InspectionCounts counts, IReadOnlyList<InspectionEvent> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        lock (_gate)
        {
            _counts = counts;
            _next = 0;
            _count = 0;
            for (int i = Math.Min(recent.Count, Capacity) - 1; i >= 0; i--)
            {
                _buffer[_next] = recent[i];
                _next = (_next + 1) % Capacity;
                _count++;
            }
        }
    }

    public void Add(InspectionEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        lock (_gate)
        {
            _buffer[_next] = e;
            _next = (_next + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
            _counts = e.Verdict switch
            {
                Verdict.Ok => _counts with { Ok = _counts.Ok + 1 },
                Verdict.Nok => _counts with { Nok = _counts.Nok + 1 },
                Verdict.Danger => _counts with { Danger = _counts.Danger + 1 },
                Verdict.Unknown => _counts with { Unknown = _counts.Unknown + 1 },
                _ => _counts, // Ignore is never counted
            };
        }

        // Outside the lock: a slow or re-entrant subscriber cannot block or deadlock writers.
        Action<InspectionEvent>? handlers = EventAdded;
        if (handlers is null)
        {
            return;
        }

        // Each subscriber on its own: one throwing handler must neither lose the rest of the frame's entries nor
        // block the other subscribers.
        foreach (Action<InspectionEvent> handler in handlers.GetInvocationList().Cast<Action<InspectionEvent>>())
        {
            try
            {
                handler(e);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EventAdded subscriber failed for inspection {Sequence}", e.Sequence);
            }
        }
    }

    public (InspectionCounts Counts, IReadOnlyList<InspectionEvent> Recent) Snapshot(int max)
    {
        lock (_gate)
        {
            return (_counts, RecentLocked(max));
        }
    }

    public IReadOnlyList<InspectionEvent> Recent(int max = 50)
    {
        lock (_gate)
        {
            return RecentLocked(max);
        }
    }

    public IReadOnlyList<MinuteBucket> PerMinute(int minutes = 15)
    {
        int n = Math.Max(minutes, 0);
        long nowTicks = _time.GetUtcNow().UtcTicks;
        nowTicks -= nowTicks % TimeSpan.TicksPerMinute;
        long firstTicks = nowTicks - ((n - 1L) * TimeSpan.TicksPerMinute);
        var ok = new int[n];
        var nok = new int[n];
        var danger = new int[n];
        var unknown = new int[n];

        lock (_gate)
        {
            for (int i = 0; i < _count; i++)
            {
                InspectionEvent e = _buffer[(_next - 1 - i + Capacity) % Capacity];
                long ticks = e.Timestamp.UtcTicks;
                long minuteTicks = ticks - (ticks % TimeSpan.TicksPerMinute);
                if (minuteTicks < firstTicks || minuteTicks > nowTicks)
                {
                    continue;
                }

                int idx = (int)((minuteTicks - firstTicks) / TimeSpan.TicksPerMinute);
                switch (e.Verdict)
                {
                    case Verdict.Ok:
                        ok[idx]++;
                        break;
                    case Verdict.Nok:
                        nok[idx]++;
                        break;
                    case Verdict.Danger:
                        danger[idx]++;
                        break;
                    case Verdict.Unknown:
                        unknown[idx]++;
                        break;
                    default:
                        break;
                }
            }
        }

        var result = new MinuteBucket[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = new MinuteBucket(
                new DateTimeOffset(firstTicks + (i * TimeSpan.TicksPerMinute), TimeSpan.Zero),
                ok[i],
                nok[i],
                danger[i],
                unknown[i]);
        }

        return result;
    }

    private InspectionEvent[] RecentLocked(int max)
    {
        int n = Math.Clamp(max, 0, _count);
        var result = new InspectionEvent[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = _buffer[(_next - 1 - i + Capacity) % Capacity];
        }

        return result;
    }
}
