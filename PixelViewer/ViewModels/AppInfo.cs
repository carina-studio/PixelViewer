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
        public override Uri? GitHubProjectUri => new("https://github.com/carina-studio/PixelViewer");


        /// <inheritdoc/>
        public override Uri? WebsiteUri => new("https://carinastudio.azurewebsites.net/PixelViewer/");
    }
}
