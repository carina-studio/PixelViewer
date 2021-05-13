using System;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer
{
	/// <summary>
	/// Extensions for <see cref="Memory{T}"/>.
	/// </summary>
	static class MemoryExtensions
	{
		/// <summary>
		/// Pin given <see cref="Memory{T}"/> and access in native way.
		/// </summary>
		/// <typeparam name="T">Type of element in <see cref="Memory{T}"/>.</typeparam>
		/// <param name="memory"><see cref="Memory{T}"/>.</param>
		/// <param name="accessor">Accessor.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void UnsafeAccess<T>(this Memory<T> memory, Action<IntPtr> accessor)
		{
			using var handle = memory.Pin();
			unsafe
			{
				accessor(new IntPtr(handle.Pointer));
			}
		}


		/// <summary>
		/// Pin given <see cref="Memory{T}"/> and access in native way.
		/// </summary>
		/// <typeparam name="T">Type of element in <see cref="Memory{T}"/>.</typeparam>
		/// <typeparam name="R">Type of returned value.</typeparam>
		/// <param name="memory"><see cref="Memory{T}"/>.</param>
		/// <param name="accessor">Accessor.</param>
		/// <returns>Returned value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static R UnsafeAccess<T, R>(this Memory<T> memory, Func<IntPtr, R> accessor)
		{
			using var handle = memory.Pin();
			unsafe
			{
				return accessor(new IntPtr(handle.Pointer));
			}
		}
	}
}
