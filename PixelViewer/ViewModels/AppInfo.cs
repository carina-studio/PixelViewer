using Avalonia.Media;
using CarinaStudio;
using CarinaStudio.AppSuite.ViewModels;
using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.ViewModels
{
    /// <summary>
    /// Application info view-model.
    /// </summary>
    class AppInfo : ApplicationInfo
    {
        // Constructor.
        public AppInfo()
        {
            var isProVersion = this.Application.ProductManager.Let(it =>
                !it.IsMock && it.IsProductActivated(PixelViewer.Products.Professional));
            if (isProVersion)
                this.Badges = new[] { this.Application.FindResourceOrDefault<IImage?>("Image/Icon.Professional").AsNonNull() };
            else
                this.Badges = Array.Empty<IImage>();
        }
        
        
        // Badges.
        public override IList<IImage> Badges { get; }
        
        
        // URI of project site.
        public override Uri? GitHubProjectUri => new("https://github.com/carina-studio/PixelViewer");


        /// <inheritdoc/>
        public override Uri? WebsiteUri => new("https://carinastudio.azurewebsites.net/PixelViewer/");
    }
}
