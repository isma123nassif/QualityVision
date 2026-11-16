using Microsoft.Extensions.Options;

namespace QualityVision.Infrastructure.Detection;

/// <summary>
/// Validates <see cref="DetectionOptions"/> at startup (<c>ValidateOnStart</c>). A custom validator, rather than
/// lambdas, so the error names the resolved model path — the most likely misconfiguration on demo day.
/// Both model files are required whatever the device: <see cref="DetectionDevice.Auto"/> may need either one.
/// </summary>
internal sealed class DetectionOptionsValidator : IValidateOptions<DetectionOptions>
{
    public ValidateOptionsResult Validate(string? name, DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!Enum.IsDefined(options.Device))
        {
            failures.Add("Detection:Device must be Auto, Cuda or Cpu.");
        }

        if (options.GpuId < 0)
        {
            failures.Add("Detection:GpuId must be 0 or greater.");
        }

        ValidateModel(failures, "Detection:ModelPath", options.ModelPath, options.ResolveModelPath);
        ValidateModel(failures, "Detection:CpuModelPath", options.CpuModelPath, options.ResolveCpuModelPath);

        // Relational patterns are false for NaN, so NaN is rejected too.
        if (options.Confidence is not (> 0 and < 1))
        {
            failures.Add("Detection:Confidence must be greater than 0 and less than 1.");
        }

        if (options.Iou is not (> 0 and < 1))
        {
            failures.Add("Detection:Iou must be greater than 0 and less than 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateModel(List<string> failures, string key, string path, Func<string> resolve)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add($"{key} is required.");
            return;
        }

        string resolved = resolve();
        if (!File.Exists(resolved))
        {
            failures.Add($"{key} '{path}' was not found (resolved to '{resolved}').");
        }
    }
}
