using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CarinaStudio;
using CarinaStudio.AppSuite.ViewModels;
using System;

namespace Carina.PixelViewer.ViewModels
{
    /// <summary>
    /// Application info view-model.
    /// </summary>
    class AppInfo : ApplicationInfo
    {
        // Icon.
        public override IBitmap Icon => AvaloniaLocator.Current.GetService<IAssetLoader>().Let(loader =>
        {
            return loader.Open(new Uri("avares://PixelViewer/AppIcon.ico")).Use(stream => new Bitmap(stream));
        });


        // URI of project site.
        public override Uri? GitHubProjectUri => new Uri("https://github.com/carina-studio/PixelViewer");


        // URI of privacy policy.
        public override Uri? PrivacyPolicyUri => new Uri("https://github.com/carina-studio/PixelViewer/blob/master/PRIVACY-POLICY.md");
    }
}
