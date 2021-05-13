using Avalonia;
using System.Threading;

namespace Carina.PixelViewer.Test
{
	/// <summary>
	/// <see cref="Application"/> instance for testing.
	/// </summary>
	class AvaloniaApp : App
	{
		// Avalonia configuration, don't remove; also used by visual designer.
		static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AvaloniaApp>()
			.UsePlatformDetect()
			.LogToTrace();


		/// <summary>
		/// Setup <see cref="AvaloniaApp"/> for testing process.
		/// </summary>
		public static void Setup()
		{
			if (AvaloniaApp.Current != null)
				return;
			BuildAvaloniaApp().SetupWithoutStarting();
			SynchronizationContext.SetSynchronizationContext(null); // Need to clear Avalonia synchronization context to use NUnit's implementation.
		}
	}
}
