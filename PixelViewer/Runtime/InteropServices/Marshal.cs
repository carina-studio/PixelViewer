using System.Runtime.InteropServices;

namespace Carina.PixelViewer.Runtime.InteropServices
{
	/// <summary>
	/// Provide marshaling functions between managed and unmanaged environment.
	/// </summary>
	static unsafe class Marshal
	{
		/// <summary>
		/// Copy data between memories.
		/// </summary>
		/// <param name="src">Address of source memory.</param>
		/// <param name="dest">Address of destination memory.</param>
		/// <param name="size">Size of data to copy in bytes.</param>
		public static void Copy(void* src, void* dest, int size)
		{
			if (size <= 0 || src == dest)
				return;
			if (size == 8)
				*(long*)dest = *(long*)src;
			else if (size == 4)
				*(int*)dest = *(int*)src;
			else if (size == 2)
				*(short*)dest = *(short*)src;
			else if (size == 1)
				*(byte*)dest = *(byte*)src;
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && size > 8)
				RtlMoveMemory(dest, src, (uint)size);
			else
			{
				var blockCount = (size / 8);
				var remainingBytes = (size % 8);
				var srcBlockPtr = (long*)src;
				var destBlockPtr = (long*)dest;
				while (blockCount > 0)
				{
					*(destBlockPtr++) = *(srcBlockPtr++);
					--blockCount;
				}
				var srcBytePtr = (byte*)srcBlockPtr;
				var destBytePtr = (byte*)destBlockPtr;
				while (remainingBytes > 0)
				{
					*(destBytePtr++) = *(srcBytePtr++);
					--remainingBytes;
				}
			}
		}


		// Copy data in memory (Windows).
		[DllImport("Kernel32")]
		static extern void RtlMoveMemory(void* dest, void* src, uint size);
	}
}
