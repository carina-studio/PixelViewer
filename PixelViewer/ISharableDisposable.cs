using System;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer
{
	/// <summary>
	/// <see cref="IDisposable"/> object which ownership can be shared.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	interface ISharableDisposable<out T> : IDisposable where T : class, ISharableDisposable<T>
	{
		/// <summary>
		/// Share the ownership.
		/// </summary>
		/// <returns>New instance with shared ownership.</returns>
		T Share();
	}
}
