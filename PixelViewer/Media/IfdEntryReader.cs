using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// Reader to read entries of IFDs in TIFF-based data.
    /// </summary>
    class IfdEntryReader
    {
        // Fields.
        uint currentEntryDataCount;
        long currentEntryDataSize;
        readonly byte[] entryBuffer = new byte[12];
        readonly Dictionary<string, int> nextIfdIndices = new();
        bool isCompleted;
        readonly Func<byte[], int, ushort> parseUInt16;
        readonly Func<byte[], int, uint> parseUInt32;
        readonly Func<byte[], int, ulong> parseUInt64;
        int remainingEntries;
        readonly Queue<Tuple<long, string>> pendingIfdsToRead = new();
        readonly Stream stream;


        /// <summary>
        /// Initialize new <see cref="IfdEntryReader"/> instance.
        /// </summary>
        /// <param name="stream">Seekable <see cref="Stream"/> to read IFD entries from.</param>
        public IfdEntryReader(Stream stream)
        {
            // check stream
            if (!stream.CanSeek)
                throw new ArgumentException();
            this.stream = stream;
            this.InitialStreamPosition = stream.Position;

            // read header
            var buffer = new byte[8];
            if (stream.Read(buffer, 0, 8) < 8)
                throw new ArgumentException("Invalid TIFF header.");
            if (buffer[0] == 'I' && buffer[1] == 'I')
            {
                this.IsLittleEndian = true;
                this.parseUInt16 = (buffer, offset) => (ushort)((buffer[offset + 1] << 8) | buffer[offset]);
                this.parseUInt32 = (buffer, offset) => (uint)((buffer[offset + 3] << 24) | (buffer[offset + 2] << 16) | (buffer[offset + 1] << 8) | buffer[offset]);
                this.parseUInt64 = (buffer, offset) => (((ulong)buffer[offset + 7] << 56) | ((ulong)buffer[offset + 6] << 48) | ((ulong)buffer[offset + 5] << 40) | ((ulong)buffer[offset + 4] << 32)
                        | ((ulong)buffer[offset + 3] << 24) | ((ulong)buffer[offset + 2] << 16) | ((ulong)buffer[offset + 1] << 8) | buffer[offset]);
            }
            else if (buffer[0] == 'M' && buffer[1] == 'M')
            {
                this.parseUInt16 = (buffer, offset) => (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
                this.parseUInt32 = (buffer, offset) => (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
                this.parseUInt64 = (buffer, offset) => (((ulong)buffer[offset] << 56) | ((ulong)buffer[offset + 1] << 48) | ((ulong)buffer[offset + 2] << 40) | ((ulong)buffer[offset + 3] << 32)
                        | ((ulong)buffer[offset + 4] << 24) | ((ulong)buffer[offset + 5] << 16) | ((ulong)buffer[offset + 6] << 8) | buffer[offset + 7]);
            }
            else
                throw new ArgumentException("Invalid TIFF header.");
            if (this.parseUInt16(buffer, 2) != 0x2a)
                throw new ArgumentException("Invalid TIFF header.");

            // get offset to first IFD
            var offset = this.parseUInt32(buffer, 4);
            if (this.InitialStreamPosition + offset >= stream.Length)
                throw new ArgumentException($"Invalid offset to first IFD: {offset}.");

            // prepare to read first IFD
            this.EnqueueIfdToRead(this.InitialStreamPosition + offset, IfdNames.Default);
        }


        /// <summary>
        /// Get ID of current entry.
        /// </summary>
        public ushort CurrentEntryId { get; private set; }


        /// <summary>
        /// Get type of data of current entry.
        /// </summary>
        public IfdEntryType CurrentEntryType { get; private set; }


        /// <summary>
        /// Get index of IFD relative to other IFDs with same name.
        /// </summary>
        public int CurrentIfdIndex { get; private set; }


        /// <summary>
        /// Get current name of IFD.
        /// </summary>
        public string? CurrentIfdName { get; private set; }


        /// <summary>
        /// Enqueue IFD to be read later.
        /// </summary>
        /// <param name="position">Position of IFD in stream.</param>
        /// <param name="ifdName">Name of IFD.</param>
        public void EnqueueIfdToRead(long position, string ifdName)
        {
            if (this.isCompleted)
                throw new InvalidOperationException();
            this.pendingIfdsToRead.Enqueue(new Tuple<long, string>(position, ifdName));
        }


        /// <summary>
        /// Get initial position of stream.
        /// </summary>
        public long InitialStreamPosition { get; }


        /// <summary>
        /// Check whether byte ordering used in IFDs is little-endian or not.
        /// </summary>
        public bool IsLittleEndian { get; }


        /// <summary>
        /// Move to next entry.
        /// </summary>
        /// <returns>True if successfully moved to next entry.</returns>
        public bool Read()
        {
            while (true)
            {
                // check state
                if (isCompleted)
                    return false;

                // move to next IFD
                if (this.remainingEntries <= 0)
                {
                    if (this.pendingIfdsToRead.Count == 0)
                    {
                        this.CurrentEntryId = 0;
                        this.CurrentEntryType = IfdEntryType.Undefined;
                        this.CurrentIfdIndex = 0;
                        this.CurrentIfdName = null;
                        this.isCompleted = true;
                        return false;
                    }
                    var (position, ifdName) = this.pendingIfdsToRead.Dequeue();
                    this.stream.Position = position;
                    if (this.stream.Read(this.entryBuffer, 0, 2) == 2)
                    {
                        try
                        {
                            this.remainingEntries = this.parseUInt16(this.entryBuffer, 0);
                            this.CurrentIfdName = ifdName;
                            if (this.nextIfdIndices.TryGetValue(ifdName, out var index))
                            {
                                this.CurrentIfdIndex = index;
                                this.nextIfdIndices[ifdName] = (index + 1);
                            }
                            else
                            {
                                this.CurrentIfdIndex = 0;
                                this.nextIfdIndices[ifdName] = 1;
                            }
                        }
                        catch (EndOfStreamException)
                        { }
                    }
                    continue;
                }

                // clear current entry
                this.CurrentEntryId = 0;
                this.CurrentEntryType = IfdEntryType.Undefined;

                // read entry
                if (this.stream.Read(this.entryBuffer, 0, 12) < 12)
                {
                    this.remainingEntries = 0;
                    continue;
                }
                else
                    --this.remainingEntries;
                this.CurrentEntryId = this.parseUInt16(this.entryBuffer, 0);
                this.CurrentEntryType = (IfdEntryType)this.parseUInt16(this.entryBuffer, 2);
                this.currentEntryDataCount = this.parseUInt32(this.entryBuffer, 4);
                this.currentEntryDataSize = this.CurrentEntryType switch
                {
                    IfdEntryType.Int16 or IfdEntryType.UInt16 => 2,
                    IfdEntryType.Int32 or IfdEntryType.UInt32 or IfdEntryType.Single => 4,
                    IfdEntryType.Rational or IfdEntryType.URational or IfdEntryType.Double => 8,
                    _ => 1,
                } * this.currentEntryDataCount;

                // get offset to next IFD
                if (this.remainingEntries <= 0)
                {
                    var buffer = new byte[4];
                    if (this.stream.Read(buffer, 0, 4) == 4)
                    {
                        var offset = this.parseUInt32(buffer, 0);
                        if (offset > 0)
                            this.EnqueueIfdToRead(this.InitialStreamPosition + offset, this.CurrentIfdName ?? "");
                    }
                }

                // complete
                return true;
            }
        }


        /// <summary>
        /// Read each entry and perform action.
        /// </summary>
        /// <param name="entryAction">Action to perform for each entry. Returning True to continue reading next entry, False to abort reading.</param>
        public void ReadEntries(Func<bool> entryAction)
        {
            while (this.Read())
            {
                if (!entryAction())
                    break;
            }
        }


        /// <summary>
        /// Try reading raw data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public bool TryGetEntryData(out byte[]? data)
        {
            // check state
            if (this.CurrentEntryId == 0)
            {
                data = null;
                return false;
            }

            // read data
            data = new byte[this.currentEntryDataSize];
            if (this.currentEntryDataSize <= 4)
            {
                Array.Copy(this.entryBuffer, 8, data, 0, (int)this.currentEntryDataSize);
                return true;
            }
            else
            {
                var position = this.stream.Position;
                this.stream.Position = this.InitialStreamPosition + this.parseUInt32(this.entryBuffer, 8);
                try
                {
                    if (this.stream.Read(data.AsSpan()) < data.Length)
                    {
                        data = null;
                        return false;
                    }
                    return true;
                }
                catch
                {
                    data = null;
                    return false;
                }
                finally
                {
                    this.stream.Position = position;
                }
            }
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public unsafe bool TryGetEntryData(out sbyte[]? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null)
            {
                data = null;
                return false;
            }
            data = new sbyte[bytes.Length];
            fixed (byte* bytesPtr = bytes)
            {
                fixed (sbyte* dataPtr = data)
                    Runtime.InteropServices.Marshal.Copy(bytesPtr, dataPtr, bytes.Length);
            }
            return true;
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public bool TryGetEntryData(out string? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null)
            {
                data = null;
                return false;
            }
            if (bytes.Length > 0 && bytes[^1] == 0)
                data = Encoding.ASCII.GetString(bytes, 0, bytes.Length - 1);
            else
                data = Encoding.ASCII.GetString(bytes);
            return true;
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public bool TryGetEntryData(out short[]? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null || bytes.Length < sizeof(short))
            {
                data = null;
                return false;
            }
            data = new short[bytes.Length >> 1];
            for (int i = data.Length - 1, offset = i << 1; i >= 0; --i, offset -= sizeof(short))
                data[i] = (short)this.parseUInt16(bytes, offset);
            return true;
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public bool TryGetEntryData(out ushort[]? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null || bytes.Length < sizeof(ushort))
            {
                data = null;
                return false;
            }
            data = new ushort[bytes.Length >> 1];
            for (int i = data.Length - 1, offset = i << 1; i >= 0; --i, offset -= sizeof(ushort))
                data[i] = this.parseUInt16(bytes, offset);
            return true;
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public bool TryGetEntryData(out int[]? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null || bytes.Length < sizeof(int))
            {
                data = null;
                return false;
            }
            data = new int[bytes.Length >> 2];
            for (int i = data.Length - 1, offset = i << 2; i >= 0; --i, offset -= sizeof(int))
                data[i] = (int)this.parseUInt32(bytes, offset);
            return true;
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public bool TryGetEntryData(out uint[]? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null || bytes.Length < sizeof(uint))
            {
                data = null;
                return false;
            }
            data = new uint[bytes.Length >> 2];
            for (int i = data.Length - 1, offset = i << 2; i >= 0; --i, offset -= sizeof(uint))
                data[i] = this.parseUInt32(bytes, offset);
            return true;
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public unsafe bool TryGetEntryData(out float[]? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null || bytes.Length < sizeof(float))
            {
                data = null;
                return false;
            }
            data = new float[bytes.Length >> 2];
            for (int i = data.Length - 1, offset = i << 2; i >= 0; --i, offset -= sizeof(float))
            {
                var uintValue = this.parseUInt32(bytes, offset);
                data[i] = *(float*)&uintValue;
            }
            return true;
        }


        /// <summary>
        /// Try reading data of entry.
        /// </summary>
        /// <param name="data">Read data.</param>
        /// <returns>True if data read successfully.</returns>
        public unsafe bool TryGetEntryData(out double[]? data)
        {
            if (!this.TryGetEntryData(out byte[]? bytes) || bytes == null || bytes.Length < sizeof(double))
            {
                data = null;
                return false;
            }
            data = new double[bytes.Length >> 3];
            for (int i = data.Length - 1, offset = i << 3; i >= 0; --i, offset -= sizeof(double))
            {
                var ulongValue = this.parseUInt64(bytes, offset);
                data[i] = *(double*)&ulongValue;
            }
            return true;
        }
    }


    /// <summary>
    /// Type of IFD entry.
    /// </summary>
    public enum IfdEntryType : ushort
    {
        /// <summary>
        /// <see cref="byte"/>.
        /// </summary>
        Byte = 1,
        /// <summary>
        /// String in ASCII ending with '\0'.
        /// </summary>
        AsciiString = 2,
        /// <summary>
        /// <see cref="ushort"/>.
        /// </summary>
        UInt16 = 3,
        /// <summary>
        /// <see cref="uint"/>.
        /// </summary>
        UInt32 = 4,
        /// <summary>
        /// Unsigned rational represented by two <see cref="uint"/>.
        /// </summary>
        URational = 5,
        /// <summary>
        /// <see cref="sbyte"/>.
        /// </summary>
        SByte = 6,
        /// <summary>
        /// Undefined.
        /// </summary>
        Undefined = 7,
        /// <summary>
        /// <see cref="short"/>.
        /// </summary>
        Int16 = 8,
        /// <summary>
        /// <see cref="int"/>.
        /// </summary>
        Int32 = 9,
        /// <summary>
        /// Rational represented by two <see cref="uint"/>.
        /// </summary>
        Rational = 10,
        /// <summary>
        /// <see cref="float"/>.
        /// </summary>
        Single = 11,
        /// <summary>
        /// <see cref="double"/>.
        /// </summary>
        Double = 12,
    }


    /// <summary>
    /// Predefined names of IFD.
    /// </summary>
    public static class IfdNames
    {
        /// <summary>
        /// Default IFD.
        /// </summary>
        public const string Default = "IFD";
        /// <summary>
        /// Exif IFD.
        /// </summary>
        public const string Exif = "Exif";
    }
}
