namespace Carina.PixelViewer.Media;

/// <summary>
/// Timing to perform color space conversion.
/// </summary>
enum ColorSpaceConversionTiming
{
    /// <summary>
    /// Convert before rendering to display.
    /// </summary>
    BeforeRenderingToDisplay,
    /// <summary>
    /// Convert before applying filters.
    /// </summary>
    BeforeApplyingFilters,
}