using QualityVision.Core.Rules;
using QualityVision.Web.Endpoints;

namespace QualityVision.Tests.Web;

public sealed class InspectionCsvTests
{
    [Fact]
    public void Header_HasGermanColumnsWithSemicolons() =>
        Assert.Equal("Zeitpunkt;Klasse;Bezeichnung;Bewertung;Konfidenz", InspectionCsv.Header);

    [Fact]
    public void FormatRow_PlainValues_UsesLocalFormatGermanLabelAndDecimalComma()
    {
        var time = new DateTimeOffset(2026, 11, 15, 9, 5, 7, TimeSpan.FromHours(1));

        string row = InspectionCsv.FormatRow(time, "cup", "Tasse", Verdict.Nok, 0.5);

        Assert.Equal("2026-11-15 09:05:07;cup;Tasse;n.i.O.;0,50", row);
    }

    [Theory]
    [InlineData(Verdict.Ok, "i.O.")]
    [InlineData(Verdict.Danger, "GEFAHR")]
    [InlineData(Verdict.Unknown, "Unbekannt")]
    public void FormatRow_Verdict_RendersGermanLabel(Verdict verdict, string label)
    {
        string row = InspectionCsv.FormatRow(DateTimeOffset.UnixEpoch, "a", "b", verdict, 0.123);

        Assert.EndsWith($";{label};0,12", row, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("a\rb", "\"a\rb\"")]
    public void Escape_QuotesOnlyWhenNeeded(string input, string expected) =>
        Assert.Equal(expected, InspectionCsv.Escape(input));

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+x", "'+x")]
    [InlineData("-x", "'-x")]
    [InlineData("@x", "'@x")]
    [InlineData("=a;b", "\"'=a;b\"")]
    [InlineData("a=b", "a=b")]
    public void Escape_NeutralizesFormulaInjection(string input, string expected) =>
        Assert.Equal(expected, InspectionCsv.Escape(input));

    [Fact]
    public void Escape_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => InspectionCsv.Escape(null!));

    [Fact]
    public void FormatRow_SeparatorInDisplayName_IsEscaped()
    {
        string row = InspectionCsv.FormatRow(DateTimeOffset.UnixEpoch, "cup", "Tasse; groß", Verdict.Ok, 1);

        Assert.Equal("1970-01-01 00:00:00;cup;\"Tasse; groß\";i.O.;1,00", row);
    }
}
