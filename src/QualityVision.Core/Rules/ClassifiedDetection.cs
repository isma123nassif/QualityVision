using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Core.Rules;

/// <summary>A detection together with the verdict the rules engine assigned to it.</summary>
public sealed record ClassifiedDetection(CoreDetection Detection, Verdict Verdict);
