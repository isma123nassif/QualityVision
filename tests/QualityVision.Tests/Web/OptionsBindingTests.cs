using Microsoft.Extensions.Configuration;

using QualityVision.Core.Geometry;
using QualityVision.Infrastructure.Camera;
using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class OptionsBindingTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void InspectionOptions_ZoneConfigured_BindsAllFourValues()
    {
        var config = Build(new()
        {
            ["Inspection:Zone:X"] = "0.1",
            ["Inspection:Zone:Y"] = "0.15",
            ["Inspection:Zone:Width"] = "0.5",
            ["Inspection:Zone:Height"] = "0.55",
        });

        var options = config.GetSection(InspectionOptions.SectionName).Get<InspectionOptions>()!;

        Assert.Equal(0.1, options.Zone.X);
        Assert.Equal(0.15, options.Zone.Y);
        Assert.Equal(0.5, options.Zone.Width);
        Assert.Equal(0.55, options.Zone.Height);
    }

    [Fact]
    public void InspectionOptions_BindOntoExistingInstance_BindsAllFourValues()
    {
        var config = Build(new()
        {
            ["Inspection:Zone:X"] = "0.1",
            ["Inspection:Zone:Y"] = "0.15",
            ["Inspection:Zone:Width"] = "0.5",
            ["Inspection:Zone:Height"] = "0.55",
        });
        var options = new InspectionOptions();

        config.GetSection(InspectionOptions.SectionName).Bind(options);

        Assert.Equal(new NormalizedRect(0.1, 0.15, 0.5, 0.55), options.Zone);
    }

    [Fact]
    public void InspectionOptions_OnlyXOverridden_KeepsOtherDefaults()
    {
        var defaults = new InspectionOptions().Zone;
        var config = Build(new() { ["Inspection:Zone:X"] = "0.1" });

        var options = config.GetSection(InspectionOptions.SectionName).Get<InspectionOptions>()!;

        Assert.Equal(0.1, options.Zone.X);
        Assert.Equal(defaults.Y, options.Zone.Y);
        Assert.Equal(defaults.Width, options.Zone.Width);
        Assert.Equal(defaults.Height, options.Zone.Height);
    }

    [Fact]
    public void InspectionOptions_NoConfiguration_UsesDefaults()
    {
        var options = new InspectionOptions();
        Build(new()).GetSection(InspectionOptions.SectionName).Bind(options);

        Assert.Equal(new InspectionOptions().Zone, options.Zone);
        Assert.True(options.Zone.IsValid);
    }

    [Fact]
    public void CameraOptions_SourceFromString_BindsEnum()
    {
        var config = Build(new()
        {
            ["Camera:Source"] = "VideoFile",
            ["Camera:VideoFile"] = "demo.mp4",
            ["Camera:MaxFps"] = "15",
        });

        var options = config.GetSection(CameraOptions.SectionName).Get<CameraOptions>()!;

        Assert.Equal(CameraSourceKind.VideoFile, options.Source);
        Assert.Equal("demo.mp4", options.VideoFile);
        Assert.Equal(15, options.MaxFps);
    }

    [Fact]
    public void CameraOptions_SourceLowercase_BindsEnumCaseInsensitively()
    {
        var config = Build(new() { ["Camera:Source"] = "url" });

        var options = config.GetSection(CameraOptions.SectionName).Get<CameraOptions>()!;

        Assert.Equal(CameraSourceKind.Url, options.Source);
    }

    [Fact]
    public void CameraOptions_NothingConfigured_DefaultsToPhone()
    {
        var options = new CameraOptions();
        Build(new()).GetSection(CameraOptions.SectionName).Bind(options);

        Assert.Equal(CameraSourceKind.Phone, options.Source);
    }
}
