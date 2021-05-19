using System.Windows.Input;

namespace Carina.PixelViewer.Input
{
	/// <summary>
	/// Extensions for <see cref="ICommand"/>.
	/// </summary>
	static class CommentExtensions
	{
		/// <summary>
		/// Try execute given command.
		/// </summary>
		/// <param name="command">Command to execute.</param>
		/// <param name="parameter">Command parameter.</param>
		/// <returns>True if command has been executed, false otherwise.</returns>
		public static bool TryExecute(this ICommand command, object? parameter = null)
		{
			if (command.CanExecute(parameter))
			{
				command.Execute(parameter);
				return true;
			}
			return false;
		}
	}
}
