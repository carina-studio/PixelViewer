using CarinaStudio;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Carina.PixelViewer.Media;

/// <summary>
/// ISO base media file reader.
/// </summary>
class IsoBaseMediaFileReader
{
    // Fields.
    readonly byte[] buffer = new byte[1024];
    byte[]? currentBoxData;
    long currentBoxDataPosition;
    long currentBoxDataSize;
    uint currentBoxType;
    readonly long endStreamPosition;
    bool eof;
    readonly Stream stream;


    /// <summary>
    /// Initialize new <see cref="IsoBaseMediaFileReader"/> instance.
    /// </summary>
    /// <param name="stream">Stream to read data.</param>
    public IsoBaseMediaFileReader(Stream stream) : this(stream, -1)
    { }
    

    // Constructor.
    IsoBaseMediaFileReader(Stream stream, long endStreamPosition)
    {
        this.endStreamPosition = endStreamPosition;
        this.stream = stream;
    }
    

    /// <summary>
    /// Get type identifier of current box.
    /// </summary>
    public uint CurrentBoxType { get => this.currentBoxType; }


    /// <summary>
    /// Get data of current box.
    /// </summary>
    /// <returns>Data of current box.</returns>
    public ReadOnlySpan<byte> GetCurrentBoxData()
    {
        if (this.eof)
            throw new InvalidOperationException();
        if (this.currentBoxData != null)
            return new ReadOnlySpan<byte>(this.currentBoxData);
        if (this.currentBoxDataSize < 0) // expand to end of stream
        {
            var data = new List<byte>();
            var readCount = this.stream.Read(this.buffer, 0, this.buffer.Length);
            while (readCount > 0)
            {
                data.AddRange(this.buffer);
                readCount = this.stream.Read(this.buffer, 0, this.buffer.Length);
            }
            this.currentBoxData = data.ToArray();
        }
        else
        {
            var data = new byte[this.currentBoxDataSize];
            var remaining = this.currentBoxDataSize;
            while (remaining > 0)
            {
                var readCount = this.stream.Read(this.buffer, 0, (int)(Math.Min(this.buffer.Length, remaining)));
                if (readCount == 0)
                {
                    this.eof = true;
                    throw new EndOfStreamException();
                }
                Array.Copy(this.buffer, 0L, data, data.LongLength - remaining, readCount);
                if (readCount >= remaining)
                    break;
                remaining -= readCount;
            }
            this.currentBoxData = data;
        }
        return new ReadOnlySpan<byte>(this.currentBoxData);
    }


    /// <summary>
    /// Get <see cref="IsoBaseMediaFileReader"/> to read data of current box.
    /// </summary>
    /// <param name="offset">Offset to start reading boxes in current data of box.</param>
    /// <returns><see cref="IsoBaseMediaFileReader"/>.</returns>
    public IsoBaseMediaFileReader GetCurrentBoxDataReader(int offset = 0)
    {
        if (this.eof)
            throw new InvalidOperationException();
        if (offset < 0)
            throw new ArgumentOutOfRangeException();
        if (this.currentBoxData != null)
            return new IsoBaseMediaFileReader(new MemoryStream(this.currentBoxData).Also(it => it.Position = offset));
        if (this.stream.CanSeek)
        {
            if (offset > 0)
                this.stream.Seek(offset, SeekOrigin.Current);
            return new IsoBaseMediaFileReader(this.stream, this.currentBoxDataPosition + this.currentBoxDataSize);
        }
        this.GetCurrentBoxData();
        return new IsoBaseMediaFileReader(new MemoryStream(this.currentBoxData.AsNonNull()).Also(it => it.Position = offset));
    }
    

    /// <summary>
    /// Move to next box and read.
    /// </summary>
    /// <returns>True if moving to next box successfully, False otherwise.</returns>
    public bool Read()
    {
        // check state
        if (this.eof)
            return false;
        
        // move to next box
        if (this.currentBoxType > 0)
        {
            this.currentBoxType = 0;
            if (this.stream.CanSeek)
            {
                this.currentBoxData = null;
                this.stream.Position = this.currentBoxDataPosition + this.currentBoxDataSize;
                if (this.endStreamPosition >= 0 && this.stream.Position >= this.endStreamPosition)
                {
                    this.eof = true;
                    return false;
                }
            }
            else if (this.currentBoxData == null)
            {
                var remaining = this.currentBoxDataSize;
                while (remaining > 0)
                {
                    var readCount = this.stream.Read(this.buffer, 0, (int)(Math.Min(this.buffer.Length, remaining)));
                    if (readCount == 0)
                    {
                        this.eof = true;
                        return false;
                    }
                    if (readCount >= remaining)
                        break;
                    remaining -= readCount;
                }
            }
            else
                this.currentBoxData = null;
        }
        
        // read box header
        if (this.stream.Read(this.buffer, 0, 8) < 8)
        {
            this.eof = true;
            return false;
        }
        this.currentBoxDataSize = BinaryPrimitives.ReadUInt32BigEndian(this.buffer.AsSpan());
        this.currentBoxType = BinaryPrimitives.ReadUInt32BigEndian(this.buffer.AsSpan(4));
        if (this.currentBoxDataSize == 1) // expand to end of stream
        {
            if (this.stream.CanSeek)
            {
                var endPosition = this.stream.Length;
                if (this.endStreamPosition >= 0)
                    endPosition = Math.Min(this.endStreamPosition, endPosition);
                this.currentBoxDataSize = (endPosition - this.stream.Position);
                if (this.currentBoxDataSize < 0)
                {
                    this.currentBoxType = 0;
                    this.eof = true;
                    return false;
                }
            }
            else
                this.currentBoxDataSize = -1;
        }
        else if (this.currentBoxDataSize < 8)
        {
            this.currentBoxType = 0;
            this.eof = true;
            return false;
        }
        else
            this.currentBoxDataSize -= 8;
        if (this.stream.CanSeek)
            this.currentBoxDataPosition = this.stream.Position;
        
        // complete
        return true;
    }
}