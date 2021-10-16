using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        static readonly AvaloniaProperty<int> FrameCountProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, int>(nameof(FrameCount), 1);
        static readonly AvaloniaProperty<int> InitialFrameNumberProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, int>(nameof(InitialFrameNumber), 1);


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
        public int FrameCount
        {
            get => this.GetValue<int>(FrameCountProperty);
            set => this.SetValue<int>(FrameCountProperty, value);
        }


        // Generate result.
        protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) => Task.FromResult((object?)(int)frameNumberUpDown.Value);


        // Initial frame number.
        public int InitialFrameNumber
        {
            get => this.GetValue<int>(InitialFrameNumberProperty);
            set => this.SetValue<int>(InitialFrameNumberProperty, value);
        }


        // Key up
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!e.Handled && e.Key == Key.Enter)
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
