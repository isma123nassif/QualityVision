namespace QualityVision.Core.Geometry;

/// <summary>
/// Rectangle in normalized image coordinates (0..1), independent of the frame resolution.
/// Used for the Prüfzone (drawn in M2, used for counting logic in M4).
/// </summary>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    /// <summary>True if the point lies inside the rectangle; edges are inclusive.</summary>
    public bool Contains(double x, double y) =>
        x >= X && x <= X + Width && y >= Y && y <= Y + Height;

    /// <summary>
    /// All values within [0,1], positive size, and the rectangle fits inside the image.
    /// NaN values make every comparison false, so NaN is invalid.
    /// </summary>
    public bool IsValid =>
        X >= 0 && Y >= 0
        && Width > 0 && Height > 0
        && X + Width <= 1 && Y + Height <= 1;
}
