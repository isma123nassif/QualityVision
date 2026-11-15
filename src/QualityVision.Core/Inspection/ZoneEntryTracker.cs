using QualityVision.Core.Rules;

namespace QualityVision.Core.Inspection;

/// <summary>
/// Counts an object once when its class <em>enters</em> the Prüfzone (ADR-002 §2). Per class: Absent → Present emits
/// one entry; while Present, every evaluated frame without the class increments a miss counter (reset when seen);
/// after <c>absenceFrames</c> consecutive misses the class is Absent again. A gap of N − 1 frames therefore never
/// recounts (detector flicker), a gap of N frames followed by a reappearance counts again.
/// Not thread-safe by contract: only the detection loop calls it. No clock: the caller stamps event times.
/// </summary>
public sealed class ZoneEntryTracker
{
    private readonly int _absenceFrames;

    // Present classes → consecutive frames without that class. A class not in the map is Absent.
    private readonly Dictionary<string, int> _present = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _presentKeys = [];

    /// <summary>Creates a tracker.</summary>
    /// <param name="absenceFrames">Consecutive evaluated frames of absence after which a class can count again (≥ 1).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="absenceFrames"/> is less than 1.</exception>
    public ZoneEntryTracker(int absenceFrames = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(absenceFrames, 1);
        _absenceFrames = absenceFrames;
    }

    /// <summary>The configured number of absence frames (N).</summary>
    public int AbsenceFrames => _absenceFrames;

    /// <summary>
    /// Call exactly once per evaluated frame with the detections inside the zone. Returns the detections that count
    /// as a new entry in this frame: at most one per class (the one with the highest confidence).
    /// Detections with <see cref="Verdict.Ignore"/> are skipped even if passed in.
    /// </summary>
    /// <param name="inZone">This frame's classified detections whose centre is inside the zone.</param>
    public IReadOnlyList<ClassifiedDetection> Update(IReadOnlyList<ClassifiedDetection> inZone)
    {
        ArgumentNullException.ThrowIfNull(inZone);

        // Best (highest confidence) detection per class in this frame; insertion order = first appearance.
        var best = new Dictionary<string, ClassifiedDetection>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (ClassifiedDetection item in inZone)
        {
            if (item.Verdict == Verdict.Ignore)
            {
                continue;
            }

            string className = item.Detection.ClassName;
            if (!best.TryGetValue(className, out ClassifiedDetection? current))
            {
                best[className] = item;
                order.Add(className);
            }
            else if (item.Detection.Confidence > current.Detection.Confidence)
            {
                best[className] = item;
            }
        }

        // Classes that were present but are missing in this frame: count the miss, drop after N misses.
        // Iterate over a copy of the keys because the loop modifies the dictionary.
        _presentKeys.Clear();
        _presentKeys.AddRange(_present.Keys);
        foreach (string className in _presentKeys)
        {
            if (best.ContainsKey(className))
            {
                continue;
            }

            int misses = _present[className] + 1;
            if (misses >= _absenceFrames)
            {
                _present.Remove(className);
            }
            else
            {
                _present[className] = misses;
            }
        }

        // Classes seen in this frame: Absent → Present is an entry; Present stays Present with the counter reset.
        List<ClassifiedDetection> entries = [];
        foreach (string className in order)
        {
            if (!_present.ContainsKey(className))
            {
                entries.Add(best[className]);
            }

            _present[className] = 0;
        }

        return entries;
    }

    /// <summary>Forgets all state: every class is Absent again.</summary>
    public void Reset() => _present.Clear();
}
