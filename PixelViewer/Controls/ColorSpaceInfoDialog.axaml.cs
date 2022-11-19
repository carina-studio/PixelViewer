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
    static readonly StyledProperty<Media.ColorSpace> ColorSpaceProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, Media.ColorSpace>(nameof(ColorSpace), Media.ColorSpace.Default);
    static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, bool>(nameof(IsReadOnly), false);
    static readonly StyledProperty<Media.ColorSpace?> ReferenceColorSpaceProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, Media.ColorSpace?>("ReferenceColorSpace", null);
    static readonly StyledProperty<IList<Media.ColorSpace>> ReferenceColorSpacesProperty = AvaloniaProperty.Register<ColorSpaceInfoDialog, IList<Media.ColorSpace>>("ReferenceColorSpaces", Array.Empty<Media.ColorSpace>());


    // Fields.
    readonly TextBox bluePrimaryTextBox;
    readonly CieChromaticityDiagram chromaticityDiagram;
    readonly CieChromaticityGamut colorSpaceChromaticityGamut = new();
    readonly Pen colorSpaceTransferFuncStroke = new()
    {
        Brush = Brushes.Red,
        Thickness = 2,
    };
    readonly CieChromaticity colorSpaceWhitePointChromaticity = new();
    readonly ComboBox diagramTypeComboBox;
    readonly TextBox greenPrimaryTextBox;
    readonly TextBlock linearizationDescriptionTextBlock;
    readonly TextBox redPrimaryTextBox;
    readonly CieChromaticityGamut refColorSpaceChromaticityGamut = new();
    readonly Pen refColorSpaceTransferFuncStroke;
    readonly CieChromaticity refColorSpaceWhitePointChromaticity = new();
    readonly TextBox nameTextBox;
    readonly NormalizedTransferFunctionsDiagram toLinearTransferFuncDiagram;
    readonly NormalizedTransferFunctionsDiagram toNonLinearTransferFuncDiagram;
    readonly TextBlock whitePointDescriptionTextBlock;
    readonly TextBox whitePointTextBox;


    // Constructor.
    public ColorSpaceInfoDialog()
    {
        // prepare resources
        this.refColorSpaceTransferFuncStroke = new Pen().Also(it =>
        {
            it.Bind(Pen.BrushProperty, this.GetResourceObservable("SystemControlForegroundBaseHighBrush"));
            it.DashStyle = DashStyle.Dash;
            it.Thickness = 2;
        });

        // setup views
        AvaloniaXamlLoader.Load(this);
        this.bluePrimaryTextBox = this.FindControl<TextBox>(nameof(bluePrimaryTextBox)).AsNonNull();
        this.chromaticityDiagram = this.FindControl<CieChromaticityDiagram>(nameof(chromaticityDiagram)).AsNonNull().Also(it =>
        {
            this.colorSpaceChromaticityGamut.BorderPen = new Pen()
            {
                Brush = Brushes.Red,
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
                Thickness = 1,
            };
            this.colorSpaceWhitePointChromaticity.BorderPen = new Pen()
            {
                Brush = Brushes.Red,
                Thickness = 1,
            };
            this.refColorSpaceChromaticityGamut.BorderPen = new Pen()
            {
                Brush = Brushes.White,
                DashStyle = DashStyle.Dash,
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
                Thickness = 1,
            };
            it.Chromaticities.Add(this.refColorSpaceWhitePointChromaticity);
            it.Chromaticities.Add(this.colorSpaceWhitePointChromaticity);
            it.ChromaticityGamuts.Add(this.refColorSpaceChromaticityGamut);
            it.ChromaticityGamuts.Add(this.colorSpaceChromaticityGamut);
        });
        this.diagramTypeComboBox = this.FindControl<ComboBox>(nameof(diagramTypeComboBox)).AsNonNull().Also(it =>
        {
            it.GetObservable(ComboBox.SelectedIndexProperty).Subscribe(new Observer<int>(index =>
            {
                var diagramViews = new Control?[]{
                    this.chromaticityDiagram,
                    this.toLinearTransferFuncDiagram,
                    this.toNonLinearTransferFuncDiagram,
                };
                for (var i = diagramViews.Length - 1; i >= 0; --i)
                    diagramViews[i]?.Parent?.Let(it => it.IsVisible = (i == index));
            }));
        });
        this.greenPrimaryTextBox = this.FindControl<TextBox>(nameof(greenPrimaryTextBox)).AsNonNull();
        this.linearizationDescriptionTextBlock = this.Get<TextBlock>(nameof(linearizationDescriptionTextBlock));
        this.redPrimaryTextBox = this.FindControl<TextBox>(nameof(redPrimaryTextBox)).AsNonNull();
        this.nameTextBox = this.FindControl<TextBox>(nameof(nameTextBox)).AsNonNull().Also(it =>
        {
            it.GetObservable(TextBox.TextProperty).Subscribe(new Observer<string?>(_ => this.InvalidateInput()));
        });
        this.toLinearTransferFuncDiagram = this.FindControl<NormalizedTransferFunctionsDiagram>(nameof(toLinearTransferFuncDiagram)).AsNonNull().Also(it =>
        {
            it.TransferFunctions.Add(new NormalizedTransferFunction(i => i)
            {
                Stroke = new Pen().Also(pen =>
                {
                    pen.Bind(Pen.BrushProperty, this.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
                })
            });
        });
        this.toNonLinearTransferFuncDiagram = this.FindControl<NormalizedTransferFunctionsDiagram>(nameof(toNonLinearTransferFuncDiagram)).AsNonNull().Also(it =>
        {
            it.TransferFunctions.Add(new NormalizedTransferFunction(i => i)
            {
                Stroke = new Pen().Also(pen =>
                {
                    pen.Bind(Pen.BrushProperty, this.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
                })
            });
        });
        this.whitePointDescriptionTextBlock = this.FindControl<TextBlock>(nameof(whitePointDescriptionTextBlock)).AsNonNull();
        this.whitePointTextBox = this.FindControl<TextBox>(nameof(whitePointTextBox)).AsNonNull();

        // attach to property
        this.GetObservable(ReferenceColorSpaceProperty).Subscribe(new Observer<Media.ColorSpace?>(colorSpace =>
        {
            if (colorSpace != null)
            {
                // xy chromaticity
                if (colorSpace.WhitePoint.HasValue)
                {
                    this.refColorSpaceWhitePointChromaticity.BorderPen ??= new Pen()
                    {
                        Brush = Brushes.White,
                        Thickness = 1,
                    };
                    var (wpX, wpY) = Media.ColorSpace.XyzToXyChromaticity(colorSpace.WhitePoint.Value);
                    this.refColorSpaceWhitePointChromaticity.X = wpX;
                    this.refColorSpaceWhitePointChromaticity.Y = wpY;
                }
                else
                    this.refColorSpaceWhitePointChromaticity.BorderPen = null;
                this.refColorSpaceChromaticityGamut.ColorSpace = colorSpace;

                // transfer functions
                if (this.toLinearTransferFuncDiagram.TransferFunctions.Count > 2)
                    this.toLinearTransferFuncDiagram.TransferFunctions.RemoveAt(1);
                this.toLinearTransferFuncDiagram.TransferFunctions.Insert(1, new NormalizedTransferFunction(colorSpace.NumericalTransferToLinear)
                {
                    Stroke = this.refColorSpaceTransferFuncStroke
                });
                if (this.toNonLinearTransferFuncDiagram.TransferFunctions.Count > 2)
                    this.toNonLinearTransferFuncDiagram.TransferFunctions.RemoveAt(1);
                this.toNonLinearTransferFuncDiagram.TransferFunctions.Insert(1, new NormalizedTransferFunction(colorSpace.NumericalTransferFromLinear)
                {
                    Stroke = this.refColorSpaceTransferFuncStroke
                });
            }
        }));
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
        var wp = colorSpace.WhitePoint;
        this.nameTextBox.Text = Data.Converters.ColorSpaceToStringConverter.Default.Convert<string>(colorSpace);
        this.colorSpaceChromaticityGamut.ColorSpace = colorSpace;
        this.toLinearTransferFuncDiagram.TransferFunctions.Add(new NormalizedTransferFunction(colorSpace.NumericalTransferToLinear)
        {
            Stroke = this.colorSpaceTransferFuncStroke
        });
        this.toNonLinearTransferFuncDiagram.TransferFunctions.Add(new NormalizedTransferFunction(colorSpace.NumericalTransferFromLinear)
        {
            Stroke = this.colorSpaceTransferFuncStroke
        });
        var (rX, rY, rZ) = colorSpace.RgbToXyz(1.0, 0.0, 0.0);
        var (gX, gY, gZ) = colorSpace.RgbToXyz(0.0, 1.0, 0.0);
        var (bX, bY, bZ) = colorSpace.RgbToXyz(0.0, 0.0, 1.0);
        this.redPrimaryTextBox.Text = $"{rX:F5}, {rY:F5}, {rZ:F5}";
        this.greenPrimaryTextBox.Text = $"{gX:F5}, {gY:F5}, {gZ:F5}";
        this.bluePrimaryTextBox.Text = $"{bX:F5}, {bY:F5}, {bZ:F5}";
        if (wp.HasValue)
        {
            var wpXYZ = wp.Value;
            var (wpX, wpY) = Media.ColorSpace.XyzToXyChromaticity(wpXYZ);
            this.colorSpaceWhitePointChromaticity.X = wpX;
            this.colorSpaceWhitePointChromaticity.Y = wpY;
            this.whitePointTextBox.Text = $"{wpXYZ.Item1:F5}, {wpXYZ.Item2:F5}, {wpXYZ.Item3:F5}";
            this.whitePointDescriptionTextBlock.Bind(TextBlock.TextProperty, Global.Run(() =>
            {
                var cct = Media.ColorSpace.XyChromaticityToCct(wpX, wpY);
                var prefix = (IObservable<string?>?)null;
                var description = this.GetResourceObservable("String/ColorSpaceInfoDialog.WhitePoint.Description");
                if (colorSpace.IsD65WhitePoint)
                {
                    prefix = new FormattedString().Also(it =>
                    {
                        it.Arg1 = "D65";
                        it.Arg2 = cct;
                        it.Bind(FormattedString.FormatProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.WhitePoint.Description.Prefix.StandardIlluminantWithCCT"));
                    });
                }
                else if (colorSpace.IsD50WhitePoint)
                {
                    prefix = new FormattedString().Also(it =>
                    {
                        it.Arg1 = "D50";
                        it.Arg2 = cct;
                        it.Bind(FormattedString.FormatProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.WhitePoint.Description.Prefix.StandardIlluminantWithCCT"));
                    });
                }
                else
                {
                    prefix = new FormattedString().Also(it =>
                    {
                        it.Arg1 = cct;
                        it.Bind(FormattedString.FormatProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.WhitePoint.Description.Prefix"));
                    });
                }
                return new FormattedString().Also(it =>
                {
                    it.Bind(FormattedString.Arg1Property, prefix);
                    it.Bind(FormattedString.Arg2Property, description);
                    it.Bind(FormattedString.FormatProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.WhitePoint.Description.WithPrefix"));
                });
            }));
        }
        else
        {
            this.colorSpaceWhitePointChromaticity.BorderPen = null;
            this.whitePointTextBox.Bind(TextBox.TextProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.WhitePoint.Undefined"));
            this.whitePointDescriptionTextBlock.Bind(TextBlock.TextProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.WhitePoint.Description"));
        }
        if (colorSpace.IsLinear)
            this.linearizationDescriptionTextBlock.Bind(TextBlock.TextProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.LinearColorSpace"));
        else
            this.linearizationDescriptionTextBlock.Bind(TextBlock.TextProperty, this.GetResourceObservable("String/ColorSpaceInfoDialog.NonLinearColorSpace"));

        // attach to color spaces
        (Media.ColorSpace.AllColorSpaces as INotifyCollectionChanged)?.Let(it =>
            it.CollectionChanged += this.OnAllColorSpacesChanged);
        this.SetValue<IList<Media.ColorSpace>>(ReferenceColorSpacesProperty, Media.ColorSpace.AllColorSpaces.Where(it => !it.Equals(colorSpace)).ToArray());
        this.SetValue<Media.ColorSpace?>(ReferenceColorSpaceProperty, colorSpace.Equals(Media.ColorSpace.Srgb) ? Media.ColorSpace.Display_P3 : Media.ColorSpace.Srgb);

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
