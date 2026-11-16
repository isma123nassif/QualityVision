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

        Assert.Equal(DetectionDevice.Auto, options.Device);
        Assert.Equal(0, options.GpuId);
        Assert.Equal("models/yolo11m.onnx", options.ModelPath);
        Assert.Equal("models/yolo11n.onnx", options.CpuModelPath);
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

    [Fact]
    public void Validation_MissingCpuModelFile_ThrowsNamingThePath()
    {
        var ex = Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:CpuModelPath"] = "models/cpu-missing.onnx",
        }));

        Assert.Contains("cpu-missing.onnx", string.Join(' ', ex.Failures));
    }

    [Fact]
    public void Validation_BlankCpuModelPath_Throws()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:CpuModelPath"] = " ",
        }));
    }

    [Fact]
    public void Validation_MissingGpuModelWithDeviceCpu_StillThrows()
    {
        // Both files are required whatever the device (Auto may need either one).
        Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:Device"] = "Cpu",
            ["Detection:ModelPath"] = "models/gpu-missing.onnx",
            ["Detection:CpuModelPath"] = ExistingModel,
        }));
    }

    [Theory]
    [InlineData("Auto", DetectionDevice.Auto)]
    [InlineData("Cuda", DetectionDevice.Cuda)]
    [InlineData("Cpu", DetectionDevice.Cpu)]
    [InlineData("cpu", DetectionDevice.Cpu)]
    public void Validation_DeviceNames_Bind(string value, DetectionDevice expected)
    {
        DetectionOptions options = Resolve(new()
        {
            ["Detection:Device"] = value,
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:CpuModelPath"] = ExistingModel,
        });

        Assert.Equal(expected, options.Device);
    }

    [Fact]
    public void Validation_UndefinedDeviceNumber_Throws()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:Device"] = "99",
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:CpuModelPath"] = ExistingModel,
        }));
    }

    [Fact]
    public void Validation_NegativeGpuId_Throws()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Detection:GpuId"] = "-1",
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:CpuModelPath"] = ExistingModel,
        }));
    }

    [Fact]
    public void Validation_GpuIdAndCpuModelPath_Bind()
    {
        DetectionOptions options = Resolve(new()
        {
            ["Detection:GpuId"] = "1",
            ["Detection:ModelPath"] = ExistingModel,
            ["Detection:CpuModelPath"] = ExistingModel,
        });

        Assert.Equal(1, options.GpuId);
        Assert.Equal(ExistingModel, options.CpuModelPath);
    }

    [Fact]
    public void ResolveCpuModelPath_RelativePath_IsRootedAtBaseDirectory()
    {
        string resolved = new DetectionOptions().ResolveCpuModelPath();

        Assert.True(Path.IsPathRooted(resolved));
        Assert.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), resolved);
        Assert.EndsWith("yolo11n.onnx", resolved);
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
