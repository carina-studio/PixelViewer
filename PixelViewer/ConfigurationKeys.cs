using CarinaStudio.Configuration;

namespace Carina.PixelViewer
{
    /// <summary>
    /// Keys for configuration.
    /// </summary>
    sealed class ConfigurationKeys
    {
        /// <summary>
        /// Maximum ratio of processor for parallel image processing.
        /// </summary>
        public static readonly SettingKey<double> MaxProcessorRatioOfParallImageProcessing = new(nameof(MaxProcessorRatioOfParallImageProcessing), 0.5);


        // Constructor.
        ConfigurationKeys()
        { }
    }
}