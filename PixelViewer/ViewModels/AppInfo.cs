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
            return loader.AsNonNull().Open(new Uri("avares://PixelViewer/AppIcon.ico")).Use(stream => new Bitmap(stream));
        });


        // URI of project site.
        public override Uri? GitHubProjectUri => new Uri("https://github.com/carina-studio/PixelViewer");


        // URI of privacy policy.
        public override Uri? PrivacyPolicyUri => this.Application.CultureInfo.ToString() switch
        {
            "zh-TW" => new Uri("https://carina-studio.github.io/PixelViewer/privacy_policy_zh-TW.html"),
            _ => new Uri("https://carina-studio.github.io/PixelViewer/privacy_policy.html"),
        };


        // URI of User Agreement.
        public override Uri? UserAgreementUri => this.Application.CultureInfo.ToString() switch
        {
            "zh-TW" => new Uri("https://carina-studio.github.io/PixelViewer/user_agreement_zh-TW.html"),
            _ => new Uri("https://carina-studio.github.io/PixelViewer/user_agreement.html"),
        };
    }
}
