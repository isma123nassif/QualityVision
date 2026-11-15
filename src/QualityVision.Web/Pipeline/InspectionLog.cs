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

    /// <summary>Counters since app start (M6 will load them from the database).</summary>
    InspectionCounts Counts { get; }

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

/// <summary>Thread-safe singleton: written by <see cref="DetectionService"/>, read by the UI and endpoints.</summary>
public sealed class InspectionLog : IInspectionLog
{
    /// <summary>Number of events kept in memory.</summary>
    public const int Capacity = 500;

    private readonly ILogger<InspectionLog> _logger;
    private readonly Lock _gate = new();
    private readonly InspectionEvent[] _buffer = new InspectionEvent[Capacity];
    private int _next;   // index the next event is written to
    private int _count;  // number of valid entries (≤ Capacity)
    private InspectionCounts _counts;

    /// <summary>Creates the log.</summary>
    /// <param name="logger">Logger for failing subscribers (optional; defaults to no logging).</param>
    public InspectionLog(ILogger<InspectionLog>? logger = null)
    {
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
