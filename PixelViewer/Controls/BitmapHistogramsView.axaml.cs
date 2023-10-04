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
    /// Property of <see cref="IsRedHistogramVisible"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsRedHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsRedHistogramVisible), false);
    /// <summary>
    /// Property of <see cref="LuminanceHistogramBrush"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LuminanceHistogramBrushProperty = AvaloniaProperty.Register<BitmapHistogramsView, IBrush?>(nameof(LuminanceHistogramBrush));
    /// <summary>
    /// Property of <see cref="RedHistogramBrush"/>.
    /// </summary>
    public static readonly StyledProperty<IBrush?> RedHistogramBrushProperty = AvaloniaProperty.Register<BitmapHistogramsView, IBrush?>(nameof(RedHistogramBrush));


    // Static fields.
    static readonly StyledProperty<IImage?> BlueHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(BlueHistogramImage));
    static readonly StyledProperty<double> BlueHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(BlueHistogramScaleY), 0);
    static readonly StyledProperty<IImage?> GreenHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(GreenHistogramImage));
    static readonly StyledProperty<double> GreenHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(GreenHistogramScaleY), 0);
    static readonly StyledProperty<IImage?> LuminanceHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(LuminanceHistogramImage));
    static readonly StyledProperty<double> LuminanceHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(LuminanceHistogramScaleY), 0);
    static readonly StyledProperty<double> MeanOfBlueOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfBlueOffset), double.NaN);
    static readonly StyledProperty<double> MeanOfGreenOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfGreenOffset), double.NaN);
    static readonly StyledProperty<double> MeanOfLuminanceOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfLuminanceOffset), double.NaN);
    static readonly StyledProperty<double> MeanOfRedOffsetProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(MeanOfRedOffset), double.NaN);
    static readonly StyledProperty<IImage?> RedHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(RedHistogramImage));
    static readonly StyledProperty<double> RedHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(RedHistogramScaleY), 0);


    // Fields.
    int maxBlueValue;
    int maxGreenValue;
    int maxLuminanceValue;
    int maxRedValue;
    readonly ScheduledAction updateHistogramImagesAction;
    readonly ScheduledAction updateHistogramScalesAction;
    readonly ScheduledAction updateMeanOfColorsAction;


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
        this.updateMeanOfColorsAction = new(() =>
        {
            var width = this.Bounds.Width;
            if (width <= 0)
                return;
            if (this.DataContext is BitmapHistograms histograms)
            {
                this.SetValue(MeanOfBlueOffsetProperty, width * histograms.MeanOfBlue / histograms.ColorCount);
                this.SetValue(MeanOfGreenOffsetProperty, width * histograms.MeanOfGreen / histograms.ColorCount);
                this.SetValue(MeanOfLuminanceOffsetProperty, width * histograms.MeanOfLuminance / histograms.ColorCount);
                this.SetValue(MeanOfRedOffsetProperty, width * histograms.MeanOfRed / histograms.ColorCount);
            }
            else
            {
                this.SetValue(MeanOfBlueOffsetProperty, double.NaN);
                this.SetValue(MeanOfGreenOffsetProperty, double.NaN);
                this.SetValue(MeanOfLuminanceOffsetProperty, double.NaN);
                this.SetValue(MeanOfRedOffsetProperty, double.NaN);
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
    /// Get or set whether histogram of red channel is visible or not.
    /// </summary>
    public bool IsRedHistogramVisible
    {
        get => this.GetValue(IsRedHistogramVisibleProperty);
        set => this.SetValue(IsRedHistogramVisibleProperty, value);
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


    // Pixel offset of mean of blue.
    double MeanOfBlueOffset => this.GetValue(MeanOfBlueOffsetProperty);
    
    
    // Pixel offset of mean of green.
    double MeanOfGreenOffset => this.GetValue(MeanOfGreenOffsetProperty);
    
    
    // Pixel offset of mean of luminance.
    double MeanOfLuminanceOffset => this.GetValue(MeanOfLuminanceOffsetProperty);
    
    
    // Pixel offset of mean of red.
    double MeanOfRedOffset => this.GetValue(MeanOfRedOffsetProperty);


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
        else if (property == DataContextProperty)
        {
            (change.OldValue as BitmapHistograms)?.Let(this.DetachFromBitmapHistograms);
            (change.NewValue as BitmapHistograms)?.Let(this.AttachToBitmapHistograms);
            this.updateMeanOfColorsAction.Schedule();
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
        this.updateMeanOfColorsAction.Schedule();
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
}
