using Avalonia;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Carina.PixelViewer.Media;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.Controls;
using CarinaStudio.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Carina.PixelViewer.Controls
{
    /// <summary>
    /// Viewer of <see cref="BitmapHistograms"/>.
    /// </summary>
    partial class BitmapHistogramsView : UserControl<IAppSuiteApplication>
    {
        /// <summary>
        /// Property of <see cref="IsBlueHistogramVisible"/>.
        /// </summary>
        public static readonly AvaloniaProperty<bool> IsBlueHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsBlueHistogramVisible), true);
        /// <summary>
        /// Property of <see cref="IsGreenHistogramVisible"/>.
        /// </summary>
        public static readonly AvaloniaProperty<bool> IsGreenHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsGreenHistogramVisible), true);
        /// <summary>
        /// Property of <see cref="IsLuminanceHistogramVisible"/>.
        /// </summary>
        public static readonly AvaloniaProperty<bool> IsLuminanceHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsLuminanceHistogramVisible), false);
        /// <summary>
        /// Property of <see cref="IsRedHistogramVisible"/>.
        /// </summary>
        public static readonly AvaloniaProperty<bool> IsRedHistogramVisibleProperty = AvaloniaProperty.Register<BitmapHistogramsView, bool>(nameof(IsRedHistogramVisible), true);


        // Static fields.
        static readonly AvaloniaProperty<IImage?> BlueHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(BlueHistogramImage));
        static readonly AvaloniaProperty<double> BlueHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(BlueHistogramScaleY), 0);
        static readonly AvaloniaProperty<IImage?> GreenHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(GreenHistogramImage));
        static readonly AvaloniaProperty<double> GreenHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(GreenHistogramScaleY), 0);
        static readonly AvaloniaProperty<IImage?> LuminanceHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(LuminanceHistogramImage));
        static readonly AvaloniaProperty<double> LuminanceHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(LuminanceHistogramScaleY), 0);
        static readonly AvaloniaProperty<IImage?> RedHistogramImageProperty = AvaloniaProperty.Register<BitmapHistogramsView, IImage?>(nameof(RedHistogramImage));
        static readonly AvaloniaProperty<double> RedHistogramScaleYProperty = AvaloniaProperty.Register<BitmapHistogramsView, double>(nameof(RedHistogramScaleY), 0);


        // Fields.
        int maxBlueValue;
        int maxGreenValue;
        int maxLuminanceValue;
        int maxRedValue;
        readonly ScheduledAction updateHistogramScalesAction;


        /// <summary>
        /// Initialize new <see cref="BitmapHistogramsView"/> instance.
        /// </summary>
        public BitmapHistogramsView()
        {
            // initialize
            InitializeComponent();

            // create actions
            this.updateHistogramScalesAction = new ScheduledAction(() =>
            {
                // check state
                if (this.DataContext is not BitmapHistograms histograms)
                    return;

                // check visibility
                var maxValue = (double)histograms.Maximum;

                // update scales
                if (this.IsRedHistogramVisible)
                    this.SetValue<double>(RedHistogramScaleYProperty, this.maxRedValue / maxValue);
                if (this.IsGreenHistogramVisible)
                    this.SetValue<double>(GreenHistogramScaleYProperty, this.maxGreenValue / maxValue);
                if (this.IsBlueHistogramVisible)
                    this.SetValue<double>(BlueHistogramScaleYProperty, this.maxBlueValue / maxValue);
                if (this.IsLuminanceHistogramVisible)
                    this.SetValue<double>(LuminanceHistogramScaleYProperty, this.maxLuminanceValue / maxValue);
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
            this.SetValue<IImage?>(RedHistogramImageProperty, this.GenerateHistogramImage(histograms.Red, this.maxRedValue, Colors.Red));
            this.SetValue<IImage?>(GreenHistogramImageProperty, this.GenerateHistogramImage(histograms.Green, this.maxGreenValue, Colors.Green));
            this.SetValue<IImage?>(BlueHistogramImageProperty, this.GenerateHistogramImage(histograms.Blue, this.maxBlueValue, Colors.Blue));
            this.SetValue<IImage?>(LuminanceHistogramImageProperty, this.GenerateHistogramImage(histograms.Luminance, this.maxLuminanceValue, Colors.White));

            // update display scales
            this.updateHistogramScalesAction.Execute();
        }


        // Image of blue histogram.
        IImage? BlueHistogramImage { get => this.GetValue<IImage?>(BlueHistogramImageProperty); }


        // Display scale of blue histogram.
        double BlueHistogramScaleY { get => this.GetValue<double>(BlueHistogramScaleYProperty); }


        // Detach from histograms.
        void DetachFromBitmapHistograms(BitmapHistograms histograms)
        {
            // clear images
            this.SetValue<IImage?>(RedHistogramImageProperty, null);
            this.SetValue<IImage?>(GreenHistogramImageProperty, null);
            this.SetValue<IImage?>(BlueHistogramImageProperty, null);
            this.SetValue<IImage?>(LuminanceHistogramImageProperty, null);
        }


        // Generate image for histogram.
        IImage GenerateHistogramImage(IList<int> histogram, int max, Color color)
        {
            var dataCount = histogram.Count;
            var pathBuilder = new StringBuilder($"M 0,{dataCount} L {dataCount - 1},{dataCount}");
            for (var i = dataCount - 1; i >= 0; --i)
                pathBuilder.AppendFormat(" L {0},{1}", i, dataCount - (histogram[i] / (double)max * dataCount));
            pathBuilder.Append(" Z");
            return new DrawingImage()
            {
                Drawing = new GeometryDrawing()
                {
                    Brush = new SolidColorBrush(Color.FromArgb(127, color.R, color.G, color.B)),
                    Geometry = PathGeometry.Parse(pathBuilder.ToString()),
                },
            };
        }


        // Image of green histogram.
        IImage? GreenHistogramImage { get => this.GetValue<IImage?>(GreenHistogramImageProperty); }


        // Display scale of green histogram.
        double GreenHistogramScaleY { get => this.GetValue<double>(GreenHistogramScaleYProperty); }


        // Initialize.
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


        /// <summary>
        /// Get or set whether histogram of blue channel is visible or not.
        /// </summary>
        public bool IsBlueHistogramVisible
        {
            get => this.GetValue<bool>(IsBlueHistogramVisibleProperty);
            set => this.SetValue<bool>(IsBlueHistogramVisibleProperty, value);
        }


        /// <summary>
        /// Get or set whether histogram of green channel is visible or not.
        /// </summary>
        public bool IsGreenHistogramVisible
        {
            get => this.GetValue<bool>(IsGreenHistogramVisibleProperty);
            set => this.SetValue<bool>(IsGreenHistogramVisibleProperty, value);
        }


        /// <summary>
        /// Get or set whether histogram of luminance is visible or not.
        /// </summary>
        public bool IsLuminanceHistogramVisible
        {
            get => this.GetValue<bool>(IsLuminanceHistogramVisibleProperty);
            set => this.SetValue<bool>(IsLuminanceHistogramVisibleProperty, value);
        }


        /// <summary>
        /// Get or set whether histogram of red channel is visible or not.
        /// </summary>
        public bool IsRedHistogramVisible
        {
            get => this.GetValue<bool>(IsRedHistogramVisibleProperty);
            set => this.SetValue<bool>(IsRedHistogramVisibleProperty, value);
        }


        // Image of luminance histogram.
        IImage? LuminanceHistogramImage { get => this.GetValue<IImage?>(LuminanceHistogramImageProperty); }


        // Display scale of luminance histogram.
        double LuminanceHistogramScaleY { get => this.GetValue<double>(LuminanceHistogramScaleYProperty); }


        /// <inheritdoc/>
        protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
        {
            base.OnPropertyChanged(change);
            var property = change.Property;
            if (property == DataContextProperty)
            {
                (change.OldValue.Value as BitmapHistograms)?.Let(it => this.DetachFromBitmapHistograms(it));
                (change.NewValue.Value as BitmapHistograms)?.Let(it => this.AttachToBitmapHistograms(it));
            }
            else if (property == IsBlueHistogramVisibleProperty 
                || property == IsGreenHistogramVisibleProperty
                || property == IsLuminanceHistogramVisibleProperty
                || property == IsRedHistogramVisibleProperty)
            {
                this.updateHistogramScalesAction.Schedule();
            }
        }


        // Image of red histogram.
        IImage? RedHistogramImage { get => this.GetValue<IImage?>(RedHistogramImageProperty); }


        // Display scale of red histogram.
        double RedHistogramScaleY { get => this.GetValue<double>(RedHistogramScaleYProperty); }
    }
}
