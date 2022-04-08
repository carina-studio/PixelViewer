using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Controls;

/// <summary>
/// Control to show one or more normalized transfer functions.
/// </summary>
class NormalizedTransferFunctionsDiagram : Control, IStyleable
{
    /// <summary>
    /// Property of <see cref="AxisBrush"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IBrush?> AxisBrushProperty = AvaloniaProperty.Register<NormalizedTransferFunctionsDiagram, IBrush?>(nameof(AxisBrush), null);
    /// <summary>
    /// Property of <see cref="GridBrush"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<NormalizedTransferFunctionsDiagram, IBrush?>(nameof(GridBrush), null);


    // Fields.
    readonly Dictionary<NormalizedTransferFunction, PolylineGeometry?> attachedTransferFuncs = new();
    Pen? axisPen;
    Pen? gridPen;
    readonly ObservableList<NormalizedTransferFunction> transferFuncs = new();


    // Static initializer.
    static NormalizedTransferFunctionsDiagram()
    {
        AffectsRender<NormalizedTransferFunctionsDiagram>(
            AxisBrushProperty,
            GridBrushProperty
        );
    }


    /// <summary>
    /// Initialize new <see cref="NormalizedTransferFunctionsDiagram"/> instance.
    /// </summary>
    public NormalizedTransferFunctionsDiagram()
    {
        this.GetObservable(BoundsProperty).Subscribe(_ =>
        {
            foreach (var transferFunc in this.attachedTransferFuncs.Keys.ToArray())
                this.attachedTransferFuncs[transferFunc] = null;
        });
        this.transferFuncs.CollectionChanged += this.OnTransferFunctionsChanged;
    }


    /// <summary>
    /// Get or set brush to draw axises.
    /// </summary>
    public IBrush? AxisBrush
    {
        get => this.GetValue<IBrush?>(AxisBrushProperty);
        set => this.SetValue<IBrush?>(AxisBrushProperty, value);
    }


    /// <summary>
    /// Get or set brush to draw grid.
    /// </summary>
    public IBrush? GridBrush
    {
        get => this.GetValue<IBrush?>(GridBrushProperty);
        set => this.SetValue<IBrush?>(GridBrushProperty, value);
    }


    // Called when property of transfer function changed.
    void OnTransferFunctionChanged(object? sender, AvaloniaPropertyChangedEventArgs e) =>
        this.InvalidateVisual();


    // Called when list of transfer function changed.
    void OnTransferFunctionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                {
                    var transferFuncs = e.NewItems.AsNonNull().Cast<NormalizedTransferFunction>();
                    foreach (var transferFunc in transferFuncs)
                    {
                        this.attachedTransferFuncs.Add(transferFunc, null);
                        transferFunc.PropertyChanged += this.OnTransferFunctionChanged;
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                {
                    var transferFuncs = e.OldItems.AsNonNull().Cast<NormalizedTransferFunction>();
                    foreach (var transferFunc in transferFuncs)
                    {
                        this.attachedTransferFuncs.Remove(transferFunc);
                        transferFunc.PropertyChanged -= this.OnTransferFunctionChanged;
                    }
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                {
                    var transferFuncs = e.OldItems.AsNonNull().Cast<NormalizedTransferFunction>();
                    foreach (var transferFunc in transferFuncs)
                    {
                        this.attachedTransferFuncs.Remove(transferFunc);
                        transferFunc.PropertyChanged -= this.OnTransferFunctionChanged;
                    }
                    transferFuncs = e.NewItems.AsNonNull().Cast<NormalizedTransferFunction>();
                    foreach (var transferFunc in transferFuncs)
                    {
                        this.attachedTransferFuncs.Add(transferFunc, null);
                        transferFunc.PropertyChanged += this.OnTransferFunctionChanged;
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                foreach (var transferFunc in this.attachedTransferFuncs.Keys)
                    transferFunc.PropertyChanged -= this.OnTransferFunctionChanged;
                this.attachedTransferFuncs.Clear();
                foreach (var transferFunc in this.transferFuncs)
                {
                    this.attachedTransferFuncs.Add(transferFunc, null);
                    transferFunc.PropertyChanged += this.OnTransferFunctionChanged;
                }
                break;
            default:
                return;
        }
        this.InvalidateVisual();
    }


    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        // get state
        var bounds = this.Bounds;
        var width = bounds.Width;
        var height = bounds.Height;

        // prepare pens
        if (this.axisPen == null)
        {
            var brush = this.AxisBrush;
            if (brush != null)
                this.axisPen = new Pen(brush, 2);
        }
        if (this.gridPen == null)
        {
            var brush = this.GridBrush;
            if (brush != null)
                this.gridPen = new Pen(brush);
        }

        // draw grid
        if (this.gridPen != null)
        {
            for (var y = 0.1; y < 0.95; y += 0.1)
            {
                var lineY = y * height;
                context.DrawLine(this.gridPen, new Point(0, lineY), new Point(width, lineY));
            }
            for (var x = 0.1; x < 0.95; x += 0.1)
            {
                var lineX = x * width;
                context.DrawLine(this.gridPen, new Point(lineX, 0), new Point(lineX, height));
            }
        }

        // draw transfer functions
        foreach (var transferFunc in this.attachedTransferFuncs.Keys.ToArray())
        {
            // check stroke
            if (transferFunc.Stroke == null)
                continue;

            // prepare geometry
            if (this.attachedTransferFuncs.TryGetValue(transferFunc, out var geometry) && geometry == null)
            {
                geometry = new PolylineGeometry();
                var maxValue = 512;
                for (var i = maxValue; i >= 0; --i)
                {
                    var x = (double)i / maxValue;
                    var y = transferFunc.TransferValue(x);
                    geometry.Points.Add(new Point(x * width, (1 - y) * height));
                }
                this.attachedTransferFuncs[transferFunc] = geometry;
            }

            // draw geometry
            context.DrawGeometry(null, transferFunc.Stroke, geometry);
        }
    }


    /// <summary>
    /// Get list of <see cref="NormalizedTransferFunction"/> to be shown in this diagram.
    /// </summary>
    public IList<NormalizedTransferFunction> TransferFunctions { get => this.transferFuncs; }


    // Interface implementations.
    Type IStyleable.StyleKey => typeof(NormalizedTransferFunctionsDiagram);
}


/// <summary>
/// Define a transfer function to be shown in <see cref="NormalizedTransferFunctionsDiagram"/>.
/// </summary>
class NormalizedTransferFunction : AvaloniaObject
{
    /// <summary>
    /// Property of <see cref="Stroke"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IPen?> StrokeProperty = AvaloniaProperty.Register<NormalizedTransferFunction, IPen?>(nameof(Stroke), null);


    // Fields.
    readonly Func<double, double> transferFunc;


    /// <summary>
    /// Initialize new <see cref="NormalizedTransferFunction"/> instance.
    /// </summary>
    /// <param name="transferFunc">Transfer function.</param>
    public NormalizedTransferFunction(Func<double, double> transferFunc) =>
        this.transferFunc = transferFunc;


    /// <summary>
    /// Get or set <see cref="IPen"/> to draw stroke.
    /// </summary>
    public IPen? Stroke
    {
        get => this.GetValue<IPen?>(StrokeProperty);
        set => this.SetValue<IPen?>(StrokeProperty, value);
    }


    /// <summary>
    /// Transfer a value.
    /// </summary>
    /// <param name="value">Value.</param>
    /// <returns>Transferred value.</returns>
    public double TransferValue(double value) =>
        this.transferFunc(value);
}