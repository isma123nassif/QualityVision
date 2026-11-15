using QualityVision.Core.Detection;

namespace QualityVision.Tests.Core;

public sealed class GermanLabelsTests
{
    [Theory]
    [InlineData("bottle", "Flasche")]
    [InlineData("cup", "Tasse")]
    [InlineData("scissors", "Schere")]
    [InlineData("cell phone", "Handy")]
    [InlineData("person", "Person")]
    [InlineData("laptop", "Laptop")]
    [InlineData("keyboard", "Tastatur")]
    [InlineData("mouse", "Maus")]
    [InlineData("book", "Buch")]
    [InlineData("spoon", "Löffel")]
    [InlineData("fork", "Gabel")]
    [InlineData("knife", "Messer")]
    [InlineData("remote", "Fernbedienung")]
    [InlineData("wine glass", "Weinglas")]
    [InlineData("banana", "Banane")]
    [InlineData("apple", "Apfel")]
    [InlineData("orange", "Orange")]
    [InlineData("chair", "Stuhl")]
    [InlineData("tv", "Monitor")]
    [InlineData("clock", "Uhr")]
    [InlineData("vase", "Vase")]
    [InlineData("teddy bear", "Teddy")]
    [InlineData("toothbrush", "Zahnbürste")]
    [InlineData("bus", "Bus")]
    [InlineData("car", "Auto")]
    [InlineData("bicycle", "Fahrrad")]
    [InlineData("backpack", "Rucksack")]
    [InlineData("handbag", "Handtasche")]
    [InlineData("umbrella", "Regenschirm")]
    [InlineData("potted plant", "Pflanze")]
    public void Get_KnownClass_ReturnsGermanName(string className, string expected)
    {
        Assert.Equal(expected, GermanLabels.Get(className));
    }

    [Theory]
    [InlineData("traffic light")]
    [InlineData("zebra")]
    [InlineData("")]
    public void Get_UnknownClass_FallsBackToOriginalName(string className)
    {
        Assert.Equal(className, GermanLabels.Get(className));
    }

    [Fact]
    public void Get_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GermanLabels.Get(null!));
    }
}
