using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Data.Converters;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Controls;

/// <summary>
/// Dialog to show information of color space.
/// </summary>
partial class ColorSpaceInfoDialog : InputDialog
{
    // Static fields.
    static readonly AvaloniaProperty<Media.ColorSpace> ColorSpaceProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, Media.ColorSpace>(nameof(ColorSpace), Media.ColorSpace.Default);
    static readonly AvaloniaProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, bool>(nameof(IsReadOnly), false);
    static readonly AvaloniaProperty<Media.ColorSpace> ReferenceColorSpaceProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, Media.ColorSpace>("ReferenceColorSpace", Media.ColorSpace.Default);
    static readonly AvaloniaProperty<IList<Media.ColorSpace>> ReferenceColorSpacesProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, IList<Media.ColorSpace>>("ReferenceColorSpaces", new Media.ColorSpace[0]);


    // Fields.
    readonly TextBox bluePrimaryTextBox;
    readonly CieChromaticityDiagram chromaticityDiagram;
    readonly CieChromaticityGamut colorSpaceChromaticityGamut = new();
    readonly TextBox greenPrimaryTextBox;
    readonly TextBox redPrimaryTextBox;
    readonly CieChromaticityGamut refColorSpaceChromaticityGamut = new();
    readonly TextBox nameTextBox;


    // Constructor.
    public ColorSpaceInfoDialog()
    {
        // setup views
        AvaloniaXamlLoader.Load(this);
        this.bluePrimaryTextBox = this.FindControl<TextBox>(nameof(bluePrimaryTextBox)).AsNonNull();
        this.chromaticityDiagram = this.FindControl<CieChromaticityDiagram>(nameof(chromaticityDiagram)).AsNonNull().Also(it =>
        {
            this.colorSpaceChromaticityGamut.BorderPen = new Pen()
            {
                Brush = Brushes.White,
                LineJoin = PenLineJoin.Bevel,
                Thickness = 2,
            };
            this.refColorSpaceChromaticityGamut.BorderPen = new Pen()
            {
                Brush = Brushes.White,
                DashStyle = DashStyle.Dash,
            };
            it.ChromaticityGamuts.Add(this.refColorSpaceChromaticityGamut);
            it.ChromaticityGamuts.Add(this.colorSpaceChromaticityGamut);
        });
        this.greenPrimaryTextBox = this.FindControl<TextBox>(nameof(greenPrimaryTextBox)).AsNonNull();
        this.redPrimaryTextBox = this.FindControl<TextBox>(nameof(redPrimaryTextBox)).AsNonNull();
        this.nameTextBox = this.FindControl<TextBox>(nameof(nameTextBox)).AsNonNull().Also(it =>
        {
            it.GetObservable(TextBox.TextProperty).Subscribe(_ => this.InvalidateInput());
        });

        // attach to property
        this.GetObservable(ReferenceColorSpaceProperty).Subscribe(colorSpace =>
        {
            this.refColorSpaceChromaticityGamut.ColorSpace = colorSpace;
        });
    }


    // Color space to be shown.
    public Media.ColorSpace ColorSpace
    {
        get => this.GetValue<Media.ColorSpace>(ColorSpaceProperty);
        set => this.SetValue<Media.ColorSpace>(ColorSpaceProperty, value);
    }


    // Generate result.
    protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken)
    {
        var colorSpace = this.ColorSpace;
        if (!this.IsReadOnly)
            colorSpace.CustomName = this.nameTextBox.Text;
        return Task.FromResult((object?)colorSpace);
    }
    

    // Show in read-only mode or not.
    public bool IsReadOnly
    {
        get => this.GetValue<bool>(IsReadOnlyProperty);
        set => this.SetValue<bool>(IsReadOnlyProperty, value);
    }


    // Called when list of all color space changed.
    void OnAllColorSpacesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        this.SetValue<IList<Media.ColorSpace>>(ReferenceColorSpacesProperty, Media.ColorSpace.AllColorSpaces.Where(it => !it.Equals(this.ColorSpace)).ToArray());


    // Called when closed.
    protected override void OnClosed(EventArgs e)
    {
        (Media.ColorSpace.AllColorSpaces as INotifyCollectionChanged)?.Let(it =>
            it.CollectionChanged -= this.OnAllColorSpacesChanged);
        base.OnClosed(e);
    }


    // Key clicked on input control.
    protected override void OnEnterKeyClickedOnInputControl(IControl control)
    {
        base.OnEnterKeyClickedOnInputControl(control);
        if (!this.IsReadOnly)
            this.GenerateResultCommand.TryExecute();
    }


    // Window opened.
    protected override void OnOpened(EventArgs e)
    {
        // show color space info
        var colorSpace = this.ColorSpace;
        this.nameTextBox.Text = Data.Converters.ColorSpaceToStringConverter.Default.Convert<string>(colorSpace);
        this.colorSpaceChromaticityGamut.ColorSpace = colorSpace;
        var (rX, rY, rZ) = colorSpace.ToXyz(1, 0, 0);
        var (gX, gY, gZ) = colorSpace.ToXyz(0, 1, 0);
        var (bX, bY, bZ) = colorSpace.ToXyz(0, 0, 1);
        this.redPrimaryTextBox.Text = $"{rX:F4}, {rY:F4}, {rZ:F4}";
        this.greenPrimaryTextBox.Text = $"{gX:F4}, {gY:F4}, {gZ:F4}";
        this.bluePrimaryTextBox.Text = $"{bX:F4}, {bY:F4}, {bZ:F4}";

        // attach to color spaces
        (Media.ColorSpace.AllColorSpaces as INotifyCollectionChanged)?.Let(it =>
            it.CollectionChanged += this.OnAllColorSpacesChanged);
        this.SetValue<IList<Media.ColorSpace>>(ReferenceColorSpacesProperty, Media.ColorSpace.AllColorSpaces.Where(it => !it.Equals(colorSpace)).ToArray());
        if (this.GetValue<Media.ColorSpace>(ReferenceColorSpaceProperty).Equals(colorSpace))
            this.SetValue<Media.ColorSpace>(ReferenceColorSpaceProperty, colorSpace.Equals(Media.ColorSpace.Srgb) ? Media.ColorSpace.Display_P3 : Media.ColorSpace.Srgb);

        // call base
        base.OnOpened(e);

        // focus to control or close if it shows built-in color space without read-only set
        if (colorSpace.IsBuiltIn && !this.IsReadOnly)
            this.SynchronizationContext.Post(this.Close);
        else
            this.SynchronizationContext.Post(() => this.nameTextBox.Focus());
    }


    // Validate input
    protected override bool OnValidateInput() =>
        base.OnValidateInput() && (!this.IsReadOnly || !string.IsNullOrWhiteSpace(this.nameTextBox.Text));
}
