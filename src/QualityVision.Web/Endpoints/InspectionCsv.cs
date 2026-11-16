using System.Globalization;

using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;

namespace QualityVision.Web.Endpoints;

/// <summary>Pure CSV formatting for the export (German Excel: <c>;</c> separator, decimal comma).</summary>
public static class InspectionCsv
{
    /// <summary>Column header line (without line terminator).</summary>
    public const string Header = "Zeitpunkt;Klasse;Bezeichnung;Bewertung;Konfidenz";

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Formats one row (without line terminator).</summary>
    /// <param name="localTime">Timestamp already converted to local time.</param>
    /// <param name="className">Detector class name.</param>
    /// <param name="displayName">German display name.</param>
    /// <param name="verdict">The verdict (rendered with its German label).</param>
    /// <param name="confidence">Confidence 0..1.</param>
    public static string FormatRow(DateTimeOffset localTime, string className, string displayName, Verdict verdict, double confidence)
    {
        return string.Join(
            ';',
            localTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Escape(className),
            Escape(displayName),
            Escape(VerdictLabels.Get(verdict)),
            confidence.ToString("0.00", De));
    }

    /// <summary>
    /// Neutralizes spreadsheet formula injection (leading <c>= + - @ TAB CR</c> gets a <c>'</c> prefix), then quotes the field
    /// when it contains the separator, a quote or a line break (quotes are doubled).
    /// </summary>
    /// <param name="value">Raw field value.</param>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
