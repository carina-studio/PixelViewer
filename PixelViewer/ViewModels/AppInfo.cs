using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.AppSuite.ViewModels;
using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.ViewModels;

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
            this.Badges = [ this.Application.FindResourceOrDefault<IImage?>("Image/Icon.Professional").AsNonNull() ];
        else
            this.Badges = Array.Empty<IImage>();
        var baseUri = $"avares://{this.Application.Assembly.GetName().Name}";
        using var bannerImageStream = this.Application.EffectiveThemeMode == ThemeMode.Dark
            ? AssetLoader.Open(new Uri($"{baseUri}/AppInfoBanner-Dark.png"))
            : AssetLoader.Open(new Uri($"{baseUri}/AppInfoBanner-Light.png"));
        this.BannerImage = new Bitmap(bannerImageStream);
    }


    /// <inheritdoc/>
    public override IList<IImage> Badges { get; }


    /// <inheritdoc/>
    public override IImage? BannerImage { get; }


    /// <inheritdoc/>
    public override Uri GitHubProjectUri => new("https://github.com/carina-studio/PixelViewer");


    /// <inheritdoc/>
    public override Uri WebsiteUri => new("https://carinastudio.net/PixelViewer/");
}