using Microsoft.Extensions.Options;

namespace QualityVision.Infrastructure.Detection;

/// <summary>
/// Validates <see cref="DetectionOptions"/> at startup (<c>ValidateOnStart</c>). A custom validator, rather than
/// lambdas, so the error names the resolved model path — the most likely misconfiguration on demo day.
/// </summary>
internal sealed class DetectionOptionsValidator : IValidateOptions<DetectionOptions>
{
    public ValidateOptionsResult Validate(string? name, DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            failures.Add("Detection:ModelPath is required.");
        }
        else
        {
            string resolved = options.ResolveModelPath();
            if (!File.Exists(resolved))
            {
                failures.Add($"Detection:ModelPath '{options.ModelPath}' was not found (resolved to '{resolved}').");
            }
        }

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
}
