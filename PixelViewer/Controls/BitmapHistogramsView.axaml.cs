using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Carina.PixelViewer.Media;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Controls;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Carina.PixelViewer.Controls;

/// <summary>
/// Viewer of <see cref="BitmapHistograms"/>.
/// </summary>
class BitmapHistogramsView : UserControl<IAppSuiteApplication>
{
    /// <summary>
    /// Property of <see cref="BlueHistogramBrush"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BlueHistogramBrushProperty = AvaloniaProperty.Register<BitmapHistogramsView, IBrush?>(nameof(BlueHistogramBrush));
    /// <summary>
    /// Property of <see cref="GreenHistogramBrush"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> GreenHistogramBrushProperty = AvaloniaProperty.Register<BitmapHistogramsView, IBrush?>(nameof(GreenHistogramBrush));
    /// <summary>
    /// Property of <see cref="IsBlueHistogramVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsBlueHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsBlueHistogramVisible), false);
    /// <summary>
    /// Property of <see cref="IsGreenHistogramVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsGreenHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsGreenHistogramVisible), false);
    /// <summary>
    /// Property of <see cref="IsLuminanceHistogramVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsLuminanceHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsLuminanceHistogramVisible), false);
    /// <summary>
    /// Property of <see cref="IsMeanMarkerVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsMeanMarkerVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsMeanMarkerVisible), true);
    /// <summary>
    /// Property of <see cref="IsMedianMarkerVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsMedianMarkerVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsMedianMarkerVisible), false);
    /// <summary>
    /// Property of <see cref="IsMinMaxMarkerVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsMinMaxMarkerVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsMinMaxMarkerVisible), false);
    /// <summary>
    /// Property of <see cref="IsRedHistogramVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsRedHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsRedHistogramVisible), false);
    /// <summary>
    /// Property of <see cref="IsShadowHighlightMarkerVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsShadowHighlightMarkerVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsShadowHighlightMarkerVisible), false);
    /// <summary>
    /// Property of <see cref="LuminanceHistogramBrush"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LuminanceHistogramBrushProperty = AvaloniaProperty.Register<BitmapHistogramsView, IBrush?>(nameof(LuminanceHistogramBrush));
    /// <summary>
    /// Property of <see cref="RedHistogramBrush"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> RedHistogramBrushProperty = AvaloniaProperty.Register<BitmapHistogramsView, IBrush?>(nameof(RedHistogramBrush));


    // Constants.
    const double DefaultMarkerOffset = -999;


    // Static fields.
    static readonly StyledProperty<IImage?> BlueHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(BlueHistogramImage));
    static readonly StyledProperty<double> BlueHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(BlueHistogramScaleY), 0);
    static readonly StyledProperty<IImage?> GreenHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(GreenHistogramImage));
    static readonly StyledProperty<double> GreenHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(GreenHistogramScaleY), 0);
    static readonly StyledProperty<double> HighlightOfBlueOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(HighlightOfBlueOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> HighlightOfGreenOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(HighlightOfGreenOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> HighlightOfLuminanceOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(HighlightOfLuminanceOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> HighlightOfRedOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(HighlightOfRedOffset), DefaultMarkerOffset);
    static readonly StyledProperty<IImage?> LuminanceHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(LuminanceHistogramImage));
    static readonly StyledProperty<double> LuminanceHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(LuminanceHistogramScaleY), 0);
    static readonly StyledProperty<double> MaxOfBlueOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MaxOfBlueOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MaxOfGreenOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MaxOfGreenOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MaxOfLuminanceOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MaxOfLuminanceOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MaxOfRedOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MaxOfRedOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MeanOfBlueOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfBlueOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MeanOfGreenOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfGreenOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MeanOfLuminanceOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfLuminanceOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MeanOfRedOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfRedOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MedianOfBlueOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MedianOfBlueOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MedianOfGreenOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MedianOfGreenOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MedianOfLuminanceOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MedianOfLuminanceOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MedianOfRedOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MedianOfRedOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MinOfBlueOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MinOfBlueOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MinOfGreenOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MinOfGreenOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MinOfLuminanceOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MinOfLuminanceOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> MinOfRedOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MinOfRedOffset), DefaultMarkerOffset);
    static readonly StyledProperty<IImage?> RedHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(RedHistogramImage));
    static readonly StyledProperty<double> RedHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(RedHistogramScaleY), 0);
    static readonly StyledProperty<double> ShadowOfBlueOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(ShadowOfBlueOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> ShadowOfGreenOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(ShadowOfGreenOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> ShadowOfLuminanceOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(ShadowOfLuminanceOffset), DefaultMarkerOffset);
    static readonly StyledProperty<double> ShadowOfRedOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(ShadowOfRedOffset), DefaultMarkerOffset);


    // Fields.
    int maxBlueValue;
    int maxGreenValue;
    int maxLuminanceValue;
    int maxRedValue;
    readonly ScheduledAction updateHistogramImagesAction;
    readonly ScheduledAction updateHistogramScalesAction;
    readonly ScheduledAction updateMarkerOffsetsAction;


    /// <summary>
    /// Initialize new <see cref="BitmapHistogramsView"/> instance.
    /// </summary>
    public BitmapHistogramsView()
    {
        // initialize
        InitializeComponent();
        this.IsEnabled = false;

        // create actions
        this.updateHistogramImagesAction = new(() =>
        {
            if (this.DataContext is BitmapHistograms histograms)
            {
                this.SetValue(RedHistogramImageProperty, this.GenerateHistogramImage(histograms.Red, this.maxRedValue, this.RedHistogramBrush));
                this.SetValue(GreenHistogramImageProperty, this.GenerateHistogramImage(histograms.Green, this.maxGreenValue, this.GreenHistogramBrush));
                this.SetValue(BlueHistogramImageProperty, this.GenerateHistogramImage(histograms.Blue, this.maxBlueValue, this.BlueHistogramBrush));
                this.SetValue(LuminanceHistogramImageProperty, this.GenerateHistogramImage(histograms.Luminance, this.maxLuminanceValue, this.LuminanceHistogramBrush));
            }
            else
            {
                this.SetValue(RedHistogramImageProperty, null);
                this.SetValue(GreenHistogramImageProperty, null);
                this.SetValue(BlueHistogramImageProperty, null);
                this.SetValue(LuminanceHistogramImageProperty, null);
            }
        });
        this.updateHistogramScalesAction = new(() =>
        {
            // check state
            if (this.DataContext is not BitmapHistograms histograms)
                return;

            // check visibility
            var maxValue = Math.Min(histograms.EffectivePixelCount / 16.0, histograms.Maximum);

            // update scales
            this.SetValue(RedHistogramScaleYProperty, this.IsRedHistogramVisible ? this.maxRedValue / maxValue : 0);
            this.SetValue(GreenHistogramScaleYProperty, this.IsGreenHistogramVisible ? this.maxGreenValue / maxValue : 0);
            this.SetValue(BlueHistogramScaleYProperty, this.IsBlueHistogramVisible ? this.maxBlueValue / maxValue : 0);
            this.SetValue(LuminanceHistogramScaleYProperty, this.IsLuminanceHistogramVisible ? this.maxLuminanceValue / maxValue : 0);
        });
        this.updateMarkerOffsetsAction = new(() =>
        {
            var borderThickness = this.BorderThickness;
            var padding = this.Padding;
            var width = this.Bounds.Width - borderThickness.Left - borderThickness.Right - padding.Left - padding.Right;
            if (width <= 0)
                return;
            if (this.DataContext is BitmapHistograms histograms)
            {
                var maxColorValue = histograms.ColorCount - 1;
                this.SetValue(MeanOfBlueOffsetProperty, width * histograms.MeanOfBlue / maxColorValue);
                this.SetValue(MeanOfGreenOffsetProperty, width * histograms.MeanOfGreen / maxColorValue);
                this.SetValue(MeanOfLuminanceOffsetProperty, width * histograms.MeanOfLuminance / maxColorValue);
                this.SetValue(MeanOfRedOffsetProperty, width * histograms.MeanOfRed / maxColorValue);
                this.SetValue(MedianOfBlueOffsetProperty, width * histograms.MedianOfBlue / maxColorValue);
                this.SetValue(MedianOfGreenOffsetProperty, width * histograms.MedianOfGreen / maxColorValue);
                this.SetValue(MedianOfLuminanceOffsetProperty, width * histograms.MedianOfLuminance / maxColorValue);
                this.SetValue(MedianOfRedOffsetProperty, width * histograms.MedianOfRed / maxColorValue);
                this.SetValue(MinOfBlueOffsetProperty, width * histograms.MinOfBlue / maxColorValue);
                this.SetValue(MinOfGreenOffsetProperty, width * histograms.MinOfGreen / maxColorValue);
                this.SetValue(MinOfLuminanceOffsetProperty, width * histograms.MinOfLuminance / maxColorValue);
                this.SetValue(MinOfRedOffsetProperty, width * histograms.MinOfRed / maxColorValue);
                this.SetValue(MaxOfBlueOffsetProperty, width * histograms.MaxOfBlue / maxColorValue);
                this.SetValue(MaxOfGreenOffsetProperty, width * histograms.MaxOfGreen / maxColorValue);
                this.SetValue(MaxOfLuminanceOffsetProperty, width * histograms.MaxOfLuminance / maxColorValue);
                this.SetValue(MaxOfRedOffsetProperty, width * histograms.MaxOfRed / maxColorValue);
                this.SetValue(ShadowOfBlueOffsetProperty, width * histograms.ShadowOfBlue / maxColorValue);
                this.SetValue(ShadowOfGreenOffsetProperty, width * histograms.ShadowOfGreen / maxColorValue);
                this.SetValue(ShadowOfLuminanceOffsetProperty, width * histograms.ShadowOfLuminance / maxColorValue);
                this.SetValue(ShadowOfRedOffsetProperty, width * histograms.ShadowOfRed / maxColorValue);
                this.SetValue(HighlightOfBlueOffsetProperty, width * histograms.HighlightOfBlue / maxColorValue);
                this.SetValue(HighlightOfGreenOffsetProperty, width * histograms.HighlightOfGreen / maxColorValue);
                this.SetValue(HighlightOfLuminanceOffsetProperty, width * histograms.HighlightOfLuminance / maxColorValue);
                this.SetValue(HighlightOfRedOffsetProperty, width * histograms.HighlightOfRed / maxColorValue);
            }
            else
            {
                this.SetValue(MeanOfBlueOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MeanOfGreenOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MeanOfLuminanceOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MeanOfRedOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MedianOfBlueOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MedianOfGreenOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MedianOfLuminanceOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MedianOfRedOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MinOfBlueOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MinOfGreenOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MinOfLuminanceOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MinOfRedOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MaxOfBlueOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MaxOfGreenOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MaxOfLuminanceOffsetProperty, DefaultMarkerOffset);
                this.SetValue(MaxOfRedOffsetProperty, DefaultMarkerOffset);
                this.SetValue(ShadowOfBlueOffsetProperty, DefaultMarkerOffset);
                this.SetValue(ShadowOfGreenOffsetProperty, DefaultMarkerOffset);
                this.SetValue(ShadowOfLuminanceOffsetProperty, DefaultMarkerOffset);
                this.SetValue(ShadowOfRedOffsetProperty, DefaultMarkerOffset);
                this.SetValue(HighlightOfBlueOffsetProperty, DefaultMarkerOffset);
                this.SetValue(HighlightOfGreenOffsetProperty, DefaultMarkerOffset);
                this.SetValue(HighlightOfLuminanceOffsetProperty, DefaultMarkerOffset);
                this.SetValue(HighlightOfRedOffsetProperty, DefaultMarkerOffset);
            }
        });
    }


    // Attach to histograms.
    void AttachToBitmapHistograms(BitmapHistograms histograms)
    {
        // create images
        this.maxRedValue = histograms.Red.Max();
        this.maxGreenValue = histograms.Green.Max();
        this.maxBlueValue = histograms.Blue.Max();
        this.maxLuminanceValue = histograms.Luminance.Max();
        this.updateHistogramImagesAction.Schedule();

        // update display scales
        this.updateHistogramScalesAction.Execute();
    }


    /// <summary>
    /// Get or set brush for histogram of blue channel.
    /// </summary>
    public IBrush? BlueHistogramBrush
    {
        get => this.GetValue(BlueHistogramBrushProperty);
        set => this.SetValue(BlueHistogramBrushProperty, value);
    }


    // Image of blue histogram.
    IImage? BlueHistogramImage => this.GetValue(BlueHistogramImageProperty);


    // Display scale of blue histogram.
    double BlueHistogramScaleY => this.GetValue(BlueHistogramScaleYProperty);


    // Detach from histograms.
    void DetachFromBitmapHistograms(BitmapHistograms histograms)
    {
        // clear images
        this.updateHistogramImagesAction.Schedule();
    }


    // Generate image for histogram.
    IImage? GenerateHistogramImage(IList<int> histogram, int max, IBrush? brush)
    {
        var dataCount = histogram.Count;
        var pathBuilder = new StringBuilder($"M 0,{dataCount} L {dataCount - 1},{dataCount}");
        if (max > 0)
        {
            for (var i = dataCount - 1; i >= 0; --i)
                pathBuilder.AppendFormat(" L {0},{1}", i, dataCount - (histogram[i] / (double)max * dataCount));

        }
        pathBuilder.Append(" Z");
        try
        {
            return new DrawingImage
            {
                Drawing = new GeometryDrawing
                {
                    Brush = brush,
                    Geometry = StreamGeometry.Parse(pathBuilder.ToString()),
                },
            };
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to generate geometry of histogram. data count: {dataCount}, max: {max}, path: '{pathBuilder}'", dataCount, max, pathBuilder);
            return null;
        }
    }


    /// <summary>
    /// Get or set brush for histogram of green channel.
    /// </summary>
    public IBrush? GreenHistogramBrush
    {
        get => this.GetValue(GreenHistogramBrushProperty);
        set => this.SetValue(GreenHistogramBrushProperty, value);
    }


    // Image of green histogram.
    IImage? GreenHistogramImage => this.GetValue(GreenHistogramImageProperty);


    // Display scale of green histogram.
    double GreenHistogramScaleY => this.GetValue(GreenHistogramScaleYProperty);


    // Pixel offset of highlight of blue.
    double HighlightOfBlueOffset => this.GetValue(HighlightOfBlueOffsetProperty);


    // Pixel offset of highlight of green.
    double HighlightOfGreenOffset => this.GetValue(HighlightOfGreenOffsetProperty);


    // Pixel offset of highlight of luminance.
    double HighlightOfLuminanceOffset => this.GetValue(HighlightOfLuminanceOffsetProperty);


    // Pixel offset of highlight of red.
    double HighlightOfRedOffset => this.GetValue(HighlightOfRedOffsetProperty);


    // Initialize.
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


    /// <summary>
    /// Get or set whether histogram of blue channel is visible or not.
    /// </summary>
    public bool IsBlueHistogramVisible
    {
        get => this.GetValue(IsBlueHistogramVisibleProperty);
        set => this.SetValue(IsBlueHistogramVisibleProperty, value);
    }


    /// <summary>
    /// Get or set whether histogram of green channel is visible or not.
    /// </summary>
    public bool IsGreenHistogramVisible
    {
        get => this.GetValue(IsGreenHistogramVisibleProperty);
        set => this.SetValue(IsGreenHistogramVisibleProperty, value);
    }


    /// <summary>
    /// Get or set whether histogram of luminance is visible or not.
    /// </summary>
    public bool IsLuminanceHistogramVisible
    {
        get => this.GetValue(IsLuminanceHistogramVisibleProperty);
        set => this.SetValue(IsLuminanceHistogramVisibleProperty, value);
    }


    /// <summary>
    /// Get or set whether the mean marker is visible or not.
    /// </summary>
    public bool IsMeanMarkerVisible
    {
        get => this.GetValue(IsMeanMarkerVisibleProperty);
        set => this.SetValue(IsMeanMarkerVisibleProperty, value);
    }


    /// <summary>
    /// Get or set whether the median marker is visible or not.
    /// </summary>
    public bool IsMedianMarkerVisible
    {
        get => this.GetValue(IsMedianMarkerVisibleProperty);
        set => this.SetValue(IsMedianMarkerVisibleProperty, value);
    }


    /// <summary>
    /// Get or set whether the minimum/maximum markers are visible or not.
    /// </summary>
    public bool IsMinMaxMarkerVisible
    {
        get => this.GetValue(IsMinMaxMarkerVisibleProperty);
        set => this.SetValue(IsMinMaxMarkerVisibleProperty, value);
    }


    /// <summary>
    /// Get or set whether histogram of red channel is visible or not.
    /// </summary>
    public bool IsRedHistogramVisible
    {
        get => this.GetValue(IsRedHistogramVisibleProperty);
        set => this.SetValue(IsRedHistogramVisibleProperty, value);
    }


    /// <summary>
    /// Get or set whether the shadow/highlight markers are visible or not.
    /// </summary>
    public bool IsShadowHighlightMarkerVisible
    {
        get => this.GetValue(IsShadowHighlightMarkerVisibleProperty);
        set => this.SetValue(IsShadowHighlightMarkerVisibleProperty, value);
    }


    /// <summary>
    /// Get or set brush for histogram of luminance.
    /// </summary>
    public IBrush? LuminanceHistogramBrush
    {
        get => this.GetValue(LuminanceHistogramBrushProperty);
        set => this.SetValue(LuminanceHistogramBrushProperty, value);
    }


    // Image of luminance histogram.
    IImage? LuminanceHistogramImage => this.GetValue(LuminanceHistogramImageProperty);


    // Display scale of luminance histogram.
    double LuminanceHistogramScaleY => this.GetValue(LuminanceHistogramScaleYProperty);


    // Pixel offset of maximum of blue.
    double MaxOfBlueOffset => this.GetValue(MaxOfBlueOffsetProperty);


    // Pixel offset of maximum of green.
    double MaxOfGreenOffset => this.GetValue(MaxOfGreenOffsetProperty);


    // Pixel offset of maximum of luminance.
    double MaxOfLuminanceOffset => this.GetValue(MaxOfLuminanceOffsetProperty);


    // Pixel offset of maximum of red.
    double MaxOfRedOffset => this.GetValue(MaxOfRedOffsetProperty);


    // Pixel offset of mean of blue.
    double MeanOfBlueOffset => this.GetValue(MeanOfBlueOffsetProperty);


    // Pixel offset of mean of green.
    double MeanOfGreenOffset => this.GetValue(MeanOfGreenOffsetProperty);


    // Pixel offset of mean of luminance.
    double MeanOfLuminanceOffset => this.GetValue(MeanOfLuminanceOffsetProperty);


    // Pixel offset of mean of red.
    double MeanOfRedOffset => this.GetValue(MeanOfRedOffsetProperty);


    // Pixel offset of median of blue.
    double MedianOfBlueOffset => this.GetValue(MedianOfBlueOffsetProperty);


    // Pixel offset of median of green.
    double MedianOfGreenOffset => this.GetValue(MedianOfGreenOffsetProperty);


    // Pixel offset of median of luminance.
    double MedianOfLuminanceOffset => this.GetValue(MedianOfLuminanceOffsetProperty);


    // Pixel offset of median of red.
    double MedianOfRedOffset => this.GetValue(MedianOfRedOffsetProperty);


    // Pixel offset of minimum of blue.
    double MinOfBlueOffset => this.GetValue(MinOfBlueOffsetProperty);


    // Pixel offset of minimum of green.
    double MinOfGreenOffset => this.GetValue(MinOfGreenOffsetProperty);


    // Pixel offset of minimum of luminance.
    double MinOfLuminanceOffset => this.GetValue(MinOfLuminanceOffsetProperty);


    // Pixel offset of minimum of red.
    double MinOfRedOffset => this.GetValue(MinOfRedOffsetProperty);


    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var property = change.Property;
        if (property == BlueHistogramBrushProperty
            || property == GreenHistogramBrushProperty
            || property == LuminanceHistogramBrushProperty
            || property == RedHistogramBrushProperty)
        {
            this.updateHistogramImagesAction.Schedule();
        }
        else if (property == BorderThicknessProperty
            || property == PaddingProperty)
        {
            this.updateMarkerOffsetsAction.Schedule();
        }
        else if (property == DataContextProperty)
        {
            (change.OldValue as BitmapHistograms)?.Let(this.DetachFromBitmapHistograms);
            (change.NewValue as BitmapHistograms)?.Let(this.AttachToBitmapHistograms);
            this.updateMarkerOffsetsAction.Schedule();
        }
        else if (property == IsBlueHistogramVisibleProperty
            || property == IsGreenHistogramVisibleProperty
            || property == IsLuminanceHistogramVisibleProperty
            || property == IsRedHistogramVisibleProperty)
        {
            this.updateHistogramScalesAction.Schedule();
        }
    }


    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        this.updateMarkerOffsetsAction.Schedule();
    }


    /// <summary>
    /// Get or set brush for histogram of red channel.
    /// </summary>
    public IBrush? RedHistogramBrush
    {
        get => this.GetValue(RedHistogramBrushProperty);
        set => this.SetValue(RedHistogramBrushProperty, value);
    }


    // Image of red histogram.
    IImage? RedHistogramImage => this.GetValue(RedHistogramImageProperty);


    // Display scale of red histogram.
    double RedHistogramScaleY => this.GetValue(RedHistogramScaleYProperty);


    // Pixel offset of shadow of blue.
    double ShadowOfBlueOffset => this.GetValue(ShadowOfBlueOffsetProperty);


    // Pixel offset of shadow of green.
    double ShadowOfGreenOffset => this.GetValue(ShadowOfGreenOffsetProperty);


    // Pixel offset of shadow of luminance.
    double ShadowOfLuminanceOffset => this.GetValue(ShadowOfLuminanceOffsetProperty);


    // Pixel offset of shadow of red.
    double ShadowOfRedOffset => this.GetValue(ShadowOfRedOffsetProperty);
}
