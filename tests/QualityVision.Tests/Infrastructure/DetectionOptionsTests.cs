using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using QualityVision.Infrastructure;
using QualityVision.Infrastructure.Detection;

namespace QualityVision.Tests.Infrastructure;

/// <summary>Validation is exercised through the real DI registration (the validator itself is internal).</summary>
public sealed class DetectionOptionsTests
{
    private static string ExistingModel => Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "DejaVu-LICENSE.txt");

    private static DetectionOptions Resolve(Dictionary<string, string?> values)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddQualityVisionInfrastructure(config);
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<DetectionOptions>>().Value;
    }

    [Fact]
    public void Defaults_MatchContract()
    {
        var options = new DetectionOptions();

        Assert.Equal("models/yolo11n.onnx", options.ModelPath);
        Assert.Equal(0.45, options.Confidence);
        Assert.Equal(0.7, options.Iou);
    }

    [Fact]
    public void ResolveModelPath_RelativePath_IsRootedAtBaseDirectory()
    {
        var options = new DetectionOptions { ModelPath = "models/yolo11n.onnx" };

        string resolved = options.ResolveModelPath();

        Assert.True(Path.IsPathRooted(resolved));
        Assert.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), resolved);
        Assert.EndsWith("yolo11n.onnx", resolved);
    }

    [Fact]
    public void ResolveModelPath_RootedPath_IsReturnedUnchanged()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "x.onnx");

        Assert.Equal(rooted, new DetectionOptions { ModelPath = rooted }.ResolveModelPath());
    }

    [Fact]
    public void Validation_ValidConfiguration_Binds()
    {
        DetectionOptions options = Resolve(new()
        {
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:Confidence"] = "0.3",
            ["Detection:Iou"] = "0.5",
        });

        Assert.Equal(0.3, options.Confidence);
        Assert.Equal(0.5, options.Iou);
    }

    [Fact]
    public void Validation_MissingModelFile_ThrowsNamingThePath()
    {
        var ex = Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:ModelPath"] = "models/does-not-exist.onnx",
        }));

        Assert.Contains("does-not-exist.onnx", string.Join(' ', ex.Failures));
    }

    [Fact]
    public void Validation_BlankModelPath_Throws()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { ["Detection:ModelPath"] = " " }));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-0.1")]
    [InlineData("1.5")]
    [InlineData("NaN")]
    public void Validation_ConfidenceOutsideOpenUnitInterval_Throws(string value)
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:Confidence"] = value,
        }));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void Validation_IouOutsideOpenUnitInterval_Throws(string value)
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:Iou"] = value,
        }));
    }
}
