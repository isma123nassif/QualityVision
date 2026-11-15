using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using QualityVision.Core.Detection;
using QualityVision.Infrastructure.Camera;
using QualityVision.Infrastructure.Detection;
using QualityVision.Infrastructure.Imaging;

namespace QualityVision.Infrastructure;

/// <summary>Registers the Infrastructure adapters (camera sources, YOLO detector, frame renderer) and their options.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddQualityVisionInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CameraOptions>()
            .Bind(configuration.GetSection(CameraOptions.SectionName))
            .Validate(static o => Enum.IsDefined(o.Source),
                "Camera:Source must be one of Phone, Url, Webcam, VideoFile.")
            .Validate(static o => o.Source != CameraSourceKind.Url || IsStreamUrl(o.Url),
                "Camera:Url must be an absolute http, https or rtsp URL when Camera:Source is Url.")
            .Validate(static o => o.Source != CameraSourceKind.VideoFile || !string.IsNullOrWhiteSpace(o.VideoFile),
                "Camera:VideoFile is required when Camera:Source is VideoFile.")
            .Validate(static o => o.WebcamIndex >= 0,
                "Camera:WebcamIndex must be 0 or greater.")
            .Validate(static o => o.RequestedWidth is > 0 and <= 4096 && o.RequestedHeight is > 0 and <= 4096,
                "Camera:RequestedWidth and Camera:RequestedHeight must be between 1 and 4096.")
            .Validate(static o => o.MaxFps is > 0 and <= 60,
                "Camera:MaxFps must be between 1 and 60.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<DetectionOptions>()
            .Bind(configuration.GetSection(DetectionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<DetectionOptions>, DetectionOptionsValidator>());

        // Singleton: shared by the HTTP frame endpoint (producer) and the camera pipeline (consumer).
        services.AddSingleton<PhoneFrameSource>();
        services.AddSingleton<FrameSourceFactory>();

        // One model instance for the whole app: exposed as itself (WarmUp) and as the Core port, same instance.
        services.AddSingleton<YoloObjectDetector>();
        services.AddSingleton<IObjectDetector>(static sp => sp.GetRequiredService<YoloObjectDetector>());
        services.AddSingleton<SkiaFrameRenderer>();

        return services;
    }

    private static bool IsStreamUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "rtsp");
    }
}
