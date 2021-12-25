using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using CarinaStudio;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Controls
{
    /// <summary>
    /// Dialog to select frame number.
    /// </summary>
    partial class FrameNumberSelectionDialog : InputDialog
    {
        // Static fields.
        static readonly AvaloniaProperty<long> FrameCountProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, long>(nameof(FrameCount), 1);
        static readonly AvaloniaProperty<long> InitialFrameNumberProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, long>(nameof(InitialFrameNumber), 1);


        // Fields.
        readonly NumericUpDown frameNumberUpDown;


        // Constructor.
        public FrameNumberSelectionDialog()
        {
            InitializeComponent();
            this.frameNumberUpDown = this.FindControl<NumericUpDown>(nameof(frameNumberUpDown)).AsNonNull();
        }


        // Initialize.
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);


        // Number of frames.
        public long FrameCount
        {
            get => this.GetValue<long>(FrameCountProperty);
            set => this.SetValue<long>(FrameCountProperty, value);
        }


        // Generate result.
        protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) => Task.FromResult((object?)(int)frameNumberUpDown.Value);


        // Initial frame number.
        public long InitialFrameNumber
        {
            get => this.GetValue<long>(InitialFrameNumberProperty);
            set => this.SetValue<long>(InitialFrameNumberProperty, value);
        }


        protected override void OnEnterKeyClickedOnInputControl(IControl control)
        {
            base.OnEnterKeyClickedOnInputControl(control);
            this.GenerateResultCommand.TryExecute();
        }


        // Window opened.
        protected override void OnOpened(EventArgs e)
        {
            this.SynchronizationContext.Post(() => this.frameNumberUpDown.FindDescendantOfType<TextBox>()?.Focus());
            base.OnOpened(e);
        }
    }
}
