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
    class FrameNumberSelectionDialog : CarinaStudio.AppSuite.Controls.InputDialog
    {
        // Static fields.
        static readonly StyledProperty<long> FrameCountProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, long>(nameof(FrameCount), 1);
        static readonly StyledProperty<long> InitialFrameNumberProperty = AvaloniaProperty.Register<FrameNumberSelectionDialog, long>(nameof(InitialFrameNumber), 1);


        // Fields.
        readonly IntegerTextBox frameNumberTextBox;


        // Constructor.
        public FrameNumberSelectionDialog()
        {
            AvaloniaXamlLoader.Load(this);
            this.frameNumberTextBox = this.Get<IntegerTextBox>(nameof(frameNumberTextBox)).Also(it =>
            {
                it.GetObservable(IntegerTextBox.ValueProperty).Subscribe(new Observer<long?>(_ => this.InvalidateInput()));
            });
        }


        // Number of frames.
        public long FrameCount
        {
            get => this.GetValue(FrameCountProperty);
            set => this.SetValue(FrameCountProperty, value);
        }


        // Generate result.
        protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) => Task.FromResult((object?)(int)frameNumberTextBox.Value.GetValueOrDefault());


        // Initial frame number.
        public long InitialFrameNumber
        {
            get => this.GetValue(InitialFrameNumberProperty);
            set => this.SetValue(InitialFrameNumberProperty, value);
        }


        /// <inheritdoc/>
        protected override void OnEnterKeyClickedOnInputControl(Control control)
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
