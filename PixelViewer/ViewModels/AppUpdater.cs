using CarinaStudio;
using CarinaStudio.AppSuite.ViewModels;
using System;

namespace Carina.PixelViewer.ViewModels;

/// <summary>
/// Application update view-model.
/// </summary>
public class AppUpdater : ApplicationUpdater
{
    /// <inheritdoc/>
    protected override bool OnCheckAutoUpdateSupport(Version version)
    {
        if (Platform.IsMacOS && version.Major >= 4)
            return false;
        return base.OnCheckAutoUpdateSupport(version);
    }
}