namespace QualityVision.Infrastructure.Persistence;

/// <summary>One persisted inspection (row of the single <c>Inspections</c> table).</summary>
public sealed class InspectionRecord
{
    /// <summary>Database key.</summary>
    public long Id { get; set; }

    /// <summary>When the object entered the Prüfzone (stored as UTC ticks).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Detector class name, e.g. "cup".</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>German display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Verdict name (<c>Verdict.ToString()</c>), stored as text.</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>Detector confidence (0..1).</summary>
    public double Confidence { get; set; }
}
