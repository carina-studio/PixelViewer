using CarinaStudio.Configuration;
using System.Runtime.InteropServices;

namespace Carina.PixelViewer;

/// <summary>
/// Keys for configuration.
/// </summary>
sealed class ConfigurationKeys
{
    /// <summary>
    /// Maximum ratio of processor for parallel image processing.
    /// </summary>
    public static readonly SettingKey<double> MaxProcessorRatioOfParallelImageProcessing = new(nameof(MaxProcessorRatioOfParallelImageProcessing), CarinaStudio.Platform.IsMacOS && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0.8 : 0.5);
    /// <summary>
    /// Sensitivity of saturation adjustment.
    /// </summary>
    public static readonly SettingKey<double> SaturationAdjustmentSensitivity = new(nameof(SaturationAdjustmentSensitivity), 1);
    /// <summary>
    /// Use SIMD acceleration if available.
    /// </summary>
    public static readonly SettingKey<bool> UseSimdAcceleration = new(nameof(UseSimdAcceleration), true);
    /// <summary>
    /// Sensitivity of vibrance adjustment.
    /// </summary>
    public static readonly SettingKey<double> VibranceAdjustmentSensitivity = new(nameof(VibranceAdjustmentSensitivity), 1);
    /// <summary>
    /// Duration of zoom animation in milliseconds.
    /// </summary>
    public static readonly SettingKey<int> ZoomAnimationDuration = new(nameof(ZoomAnimationDuration), 500);


    // Constructor.
    ConfigurationKeys()
    { }
}