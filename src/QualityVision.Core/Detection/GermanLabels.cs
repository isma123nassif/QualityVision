using System.Collections.Frozen;

namespace QualityVision.Core.Detection;

/// <summary>German display names for the COCO classes that matter for the demo.</summary>
public static class GermanLabels
{
    private static readonly FrozenDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bottle"] = "Flasche",
        ["cup"] = "Tasse",
        ["scissors"] = "Schere",
        ["cell phone"] = "Handy",
        ["person"] = "Person",
        ["laptop"] = "Laptop",
        ["keyboard"] = "Tastatur",
        ["mouse"] = "Maus",
        ["book"] = "Buch",
        ["spoon"] = "Löffel",
        ["fork"] = "Gabel",
        ["knife"] = "Messer",
        ["remote"] = "Fernbedienung",
        ["wine glass"] = "Weinglas",
        ["banana"] = "Banane",
        ["apple"] = "Apfel",
        ["orange"] = "Orange",
        ["chair"] = "Stuhl",
        ["tv"] = "Monitor",
        ["clock"] = "Uhr",
        ["vase"] = "Vase",
        ["teddy bear"] = "Teddy",
        ["toothbrush"] = "Zahnbürste",
        ["bus"] = "Bus",
        ["car"] = "Auto",
        ["bicycle"] = "Fahrrad",
        ["backpack"] = "Rucksack",
        ["handbag"] = "Handtasche",
        ["umbrella"] = "Regenschirm",
        ["potted plant"] = "Pflanze",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>German display name for a COCO class name; falls back to <paramref name="className"/> itself.</summary>
    public static string Get(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        return Labels.TryGetValue(className, out string? label) ? label : className;
    }
}
