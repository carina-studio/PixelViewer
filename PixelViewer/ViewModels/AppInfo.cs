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
        // URI of project site.
        public override Uri? GitHubProjectUri => new Uri("https://github.com/carina-studio/PixelViewer");


        // URI of privacy policy.
        public override Uri? PrivacyPolicyUri => this.Application.PrivacyPolicyVersion.Let(it =>
        {
            if (it != null)
                return new Uri($"https://carinastudio.azurewebsites.net/Documents/PixelViewer/PrivacyPolicy?version={it.Major}.{it.Minor}");
            return new Uri($"https://carinastudio.azurewebsites.net/Documents/PixelViewer/PrivacyPolicy");
        });
        

        // URI of User Agreement.
        public override Uri? UserAgreementUri => this.Application.UserAgreementVersion.Let(it =>
        {
            if (it != null)
                return new Uri($"https://carinastudio.azurewebsites.net/Documents/PixelViewer/UserAgreement?version={it.Major}.{it.Minor}");
            return new Uri($"https://carinastudio.azurewebsites.net/Documents/PixelViewer/UserAgreement");
        });


        /// <inheritdoc/>
        public override Uri? WebsiteUri => new Uri("https://carinastudio.azurewebsites.net/PixelViewer/");
    }
}
