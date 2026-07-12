using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Windows.Input;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// How a set of selected files should be opened.
	/// </summary>
	enum MultiFileOpenMode
	{
		/// <summary>
		/// Open each file in its own session.
		/// </summary>
		Independent,
		/// <summary>
		/// Open all files as a single frame sequence.
		/// </summary>
		Sequence,
	}


	/// <summary>
	/// Dialog to let user choose how to open multiple selected files.
	/// </summary>
	class MultiFileOpenModeDialog : InputDialog
	{
		// Fields.
		MultiFileOpenMode result = MultiFileOpenMode.Independent;


		// Constructor.
		public MultiFileOpenModeDialog()
		{
			AvaloniaXamlLoader.Load(this);
		}


		// Generate result.
		protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken) =>
			Task.FromResult((object?)this.result);


		// Open files independently.
		void OnIndependentButtonClick(object? sender, RoutedEventArgs e)
		{
			this.result = MultiFileOpenMode.Independent;
			this.GenerateResultCommand.TryExecute();
		}


		// Open files as a frame sequence.
		void OnSequenceButtonClick(object? sender, RoutedEventArgs e)
		{
			this.result = MultiFileOpenMode.Sequence;
			this.GenerateResultCommand.TryExecute();
		}
	}
}
