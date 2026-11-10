using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CarinaStudio;
using System;

namespace Carina.PixelViewer.Controls;

/// <summary>
/// <see cref="Slider"/> which fills its track from <see cref="FillOrigin"/> to the current value, rather than from the minimum value.
/// </summary>
/// <remarks>The track is drawn by the control itself only in horizontal orientation. A vertical slider keeps the track of its template.</remarks>
class CenterFilledSlider : Slider
{
	/// <summary>
	/// Property of <see cref="FillOrigin"/>.
	/// </summary>
	public static readonly StyledProperty<double> FillOriginProperty = AvaloniaProperty.Register<CenterFilledSlider, double>(nameof(FillOrigin));
	/// <summary>
	/// Property of <see cref="TrackBrush"/>.
	/// </summary>
	public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<CenterFilledSlider, IBrush?>(nameof(TrackBrush));
	/// <summary>
	/// Property of <see cref="TrackFillBrush"/>.
	/// </summary>
	public static readonly StyledProperty<IBrush?> TrackFillBrushProperty = AvaloniaProperty.Register<CenterFilledSlider, IBrush?>(nameof(TrackFillBrush));
	/// <summary>
	/// Property of <see cref="TrackThickness"/>.
	/// </summary>
	public static readonly StyledProperty<double> TrackThicknessProperty = AvaloniaProperty.Register<CenterFilledSlider, double>(nameof(TrackThickness), double.NaN);


	// Constants.
	const double DefaultTrackThickness = 4;


	// Fields.
	Track? track;
	Control? trackBackground;
	IDisposable? trackBoundsObserverToken;


	// Static initializer.
	static CenterFilledSlider()
	{
		AffectsRender<CenterFilledSlider>(
			FillOriginProperty,
			IsDirectionReversedProperty,
			MaximumProperty,
			MinimumProperty,
			OrientationProperty,
			TrackBrushProperty,
			TrackFillBrushProperty,
			TrackThicknessProperty,
			ValueProperty);
	}


	// Convert value to the offset of the center of thumb on the track.
	double ConvertValueToTrackOffset(double value, double thumbLength, double movableLength)
	{
		// get ratio in range
		var range = this.Maximum - this.Minimum;
		var ratio = range > 0 ? Math.Clamp((value - this.Minimum) / range, 0, 1) : 0;
		if (this.IsDirectionReversed)
			ratio = 1 - ratio;

		// convert to offset
		return thumbLength / 2 + movableLength * ratio;
	}


	/// <summary>
	/// Get or set the value which the fill of track starts from.
	/// </summary>
	public double FillOrigin
	{
		get => this.GetValue(FillOriginProperty);
		set => this.SetValue(FillOriginProperty, value);
	}


	// Find brush in resources.
	IBrush? FindBrushResource(string key) =>
		this.TryFindResource(key, this.ActualThemeVariant, out var res) ? res as IBrush : null;


	/// <inheritdoc/>
	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		// detach from previous template
		this.trackBoundsObserverToken?.Dispose();
		this.trackBoundsObserverToken = null;

		// apply template
		base.OnApplyTemplate(e);

		// find parts
		this.track = e.NameScope.Find<Track>("PART_Track");
		this.trackBackground = e.NameScope.Find<Control>("TrackBackground");
		this.trackBoundsObserverToken = this.track?.GetObservable(BoundsProperty).Subscribe(_ => this.InvalidateVisual());
		this.UpdateTemplateTrackOpacity();
	}


	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		var property = change.Property;
		if (property == ThemeVariantScope.ActualThemeVariantProperty || property == IsEffectivelyEnabledProperty)
			this.InvalidateVisual();
		else if (property == OrientationProperty)
			this.UpdateTemplateTrackOpacity();
	}


	/// <inheritdoc/>
	public override void Render(DrawingContext context)
	{
		// render base
		base.Render(context);

		// check state
		var track = this.track;
		var thumb = track?.Thumb;
		if (track is null || thumb is null || this.Orientation != Orientation.Horizontal)
			return;
		var trackPosition = track.TranslatePoint(new Point(0, 0), this);
		if (trackPosition is null)
			return;

		// get brushes and thickness
		var isEnabled = this.IsEffectivelyEnabled;
		var trackBrush = isEnabled
			? this.TrackBrush ?? this.FindBrushResource("SliderTrackFill")
			: this.FindBrushResource("SliderTrackFillDisabled");
		var fillBrush = isEnabled
			? this.TrackFillBrush ?? this.FindBrushResource("SliderTrackValueFill")
			: this.FindBrushResource("SliderTrackValueFillDisabled");
		var thickness = this.TrackThickness;
		if (!double.IsFinite(thickness) || thickness <= 0)
		{
			thickness = this.TryFindResource("SliderTrackThemeHeight", this.ActualThemeVariant, out var res) && res is double resThickness
				? resThickness
				: DefaultTrackThickness;
		}
		var radius = thickness / 2;

		// calculate positions
		var trackBounds = track.Bounds;
		var thumbLength = thumb.Bounds.Width;
		var movableLength = Math.Max(0, trackBounds.Width - thumbLength);
		var left = trackPosition.Value.X;
		var top = trackPosition.Value.Y + (trackBounds.Height - thickness) / 2;
		var originX = left + this.ConvertValueToTrackOffset(this.FillOrigin, thumbLength, movableLength);
		var valueX = left + this.ConvertValueToTrackOffset(this.Value, thumbLength, movableLength);

		// draw track
		if (trackBrush is not null)
			context.DrawRectangle(trackBrush, null, new RoundedRect(new Rect(left, top, trackBounds.Width, thickness), radius));

		// draw fill
		var fillWidth = Math.Abs(valueX - originX);
		if (fillBrush is not null && fillWidth > 0)
			context.DrawRectangle(fillBrush, null, new RoundedRect(new Rect(Math.Min(originX, valueX), top, fillWidth, thickness), radius));
	}


	/// <inheritdoc/>
	protected override Type StyleKeyOverride => typeof(Slider);


	/// <summary>
	/// Get or set brush of the track while the slider is enabled. Set to <c>Null</c> to use the brush of the theme.
	/// </summary>
	public IBrush? TrackBrush
	{
		get => this.GetValue(TrackBrushProperty);
		set => this.SetValue(TrackBrushProperty, value);
	}


	/// <summary>
	/// Get or set brush of the filled part of track while the slider is enabled. Set to <c>Null</c> to use the brush of the theme.
	/// </summary>
	public IBrush? TrackFillBrush
	{
		get => this.GetValue(TrackFillBrushProperty);
		set => this.SetValue(TrackFillBrushProperty, value);
	}


	/// <summary>
	/// Get or set thickness of the track. Set to <see cref="double.NaN"/> to use the thickness of the theme.
	/// </summary>
	public double TrackThickness
	{
		get => this.GetValue(TrackThicknessProperty);
		set => this.SetValue(TrackThicknessProperty, value);
	}


	// Hide the track drawn by template in horizontal orientation, and show it otherwise.
	void UpdateTemplateTrackOpacity()
	{
		var opacity = this.Orientation == Orientation.Horizontal ? 0.0 : 1.0;
		this.trackBackground?.SetValue(OpacityProperty, opacity);
		this.track?.DecreaseButton?.SetValue(OpacityProperty, opacity);
		this.track?.IncreaseButton?.SetValue(OpacityProperty, opacity);
	}
}
