using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CarinaStudio;
using CarinaStudio.Controls;
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
    partial class FrameNumberSelectionDialog : CarinaStudio.AppSuite.Controls.InputDialog
    {
        // Static fields.
        static readonly AvaloniaProperty<long> FrameCountProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, long>(nameof(FrameCount), 1);
        static readonly AvaloniaProperty<long> InitialFrameNumberProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, long>(nameof(InitialFrameNumber), 1);


        // Fields.
        readonly IntegerTextBox frameNumberTextBox;


        // Constructor.
        public FrameNumberSelectionDialog()
        {
            InitializeComponent();
            this.frameNumberTextBox = this.FindControl<IntegerTextBox>(nameof(frameNumberTextBox)).AsNonNull().Also(it =>
            {
                it.GetObservable(IntegerTextBox.ValueProperty).Subscribe(new Observer<long?>(_ => this.InvalidateInput()));
            });
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
        protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) => Task.FromResult((object?)(int)frameNumberTextBox.Value.GetValueOrDefault());


        // Initial frame number.
        public long InitialFrameNumber
        {
            get => this.GetValue<long>(InitialFrameNumberProperty);
            set => this.SetValue<long>(InitialFrameNumberProperty, value);
        }


        /// <inheritdoc/>
        protected override void OnEnterKeyClickedOnInputControl(IControl control)
        {
            base.OnEnterKeyClickedOnInputControl(control);
            this.GenerateResultCommand.TryExecute();
        }


        // Window opened.
        protected override void OnOpened(EventArgs e)
        {
            this.SynchronizationContext.Post(() => this.frameNumberTextBox.Focus());
            base.OnOpened(e);
        }


        // Validate input
        protected override bool OnValidateInput() =>
            base.OnValidateInput() && this.frameNumberTextBox.Value.HasValue;
    }
}
