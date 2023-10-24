using Carina.PixelViewer.Media.Profiles;
using CarinaStudio;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.FileFormatParsers
{
    /// <summary>
    /// <see cref="IFileFormatParser"/> for YUV2MPEG2.
    /// </summary>
    class Yuv4Mpeg2FileFormatParser : BaseFileFormatParser
    {
        /// <summary>
        /// Initialize new <see cref="Yuv4Mpeg2FileFormatParser"/> instance.
        /// </summary>
        public Yuv4Mpeg2FileFormatParser() : base(FileFormats.Yuv4Mpeg2)
        { }


        /// <inheritdoc/>
        protected override async Task<ImageRenderingProfile?> ParseImageRenderingProfileAsyncCore(IImageDataSource source, Stream stream, CancellationToken cancellationToken)
        {
            // parse
            var width = 0;
            var height = 0;
            var dataOffset = 0L;
            var byteOrdering = ByteOrdering.BigEndian;
            var renderer = (ImageRenderers.IImageRenderer?)null;
            var pixelStrides = new int[ImageFormat.MaxPlaneCount];
            var rowStrides = new int[ImageFormat.MaxPlaneCount];
            var parsed = await Task.Run(() =>
            {
                // check header
                var headerBuffer = new byte[10];
                if (stream.Read(headerBuffer) < headerBuffer.Length)
                    this.ThrowInvalidFormatException();
                if (headerBuffer[0] != 'Y' || headerBuffer[1] != 'U' || headerBuffer[2] != 'V' || headerBuffer[3] != '4'
                    || headerBuffer[4] != 'M' || headerBuffer[5] != 'P' || headerBuffer[6] != 'E' || headerBuffer[7] != 'G' || headerBuffer[8] != '2'
                    || headerBuffer[9] != 0x20)
                {
                    return false;
                }

                // read parameters
                var yuvFormat = "";
                while (dataOffset == 0)
                {
                    if (stream.Position >= 4096)
                        this.ThrowInvalidFormatException();
                    int name = stream.ReadByte();
                    if (name < 0)
                        this.ThrowInvalidFormatException();
                    var value = this.ReadParameterValue(stream, out var isLastParam);
                    switch (name)
                    {
                        case 'C':
                            yuvFormat = value;
                            break;
                        case 'H':
                            if (!int.TryParse(value, out height))
                                this.ThrowInvalidFormatException();
                            break;
                        case 'W':
                            if (!int.TryParse(value, out width))
                                this.ThrowInvalidFormatException();
                            break;
                    }
                    if (isLastParam)
                    {
                        dataOffset = stream.Position + 6;
                        break;
                    }
                }
                if (width <= 0 || height <= 0)
                    this.ThrowInvalidFormatException();

                // select image format
                var imageFormatName = yuvFormat switch
                {
                    "420" or "" => "I420",
                    "420p10" => "P010",
                    "420p16" => "P016",
                    "422" => "YUV422p",
                    "444" => "YUV444p",
                    _ => throw new ArgumentException($"Unknown YUV format: {yuvFormat}."),
                };

                // select byte ordering
                byteOrdering = imageFormatName switch
                {
                    "P010" or "P016" => ByteOrdering.LittleEndian,
                    _ => byteOrdering,
                };

                // find renderer
                if (!ImageRenderers.ImageRenderers.TryFindByFormatName(imageFormatName, out renderer) || renderer == null)
                    throw new ArgumentException($"Unknown image format: {imageFormatName}.");

                // get default plane options
                var imageFormat = renderer.Format;
                var defaultPlaneOptions = renderer.CreateDefaultPlaneOptions(width, height);
                for (var i = defaultPlaneOptions.Count - 1; i >= 0; --i)
                {
                    pixelStrides[i] = defaultPlaneOptions[i].PixelStride;
                    rowStrides[i] = defaultPlaneOptions[i].RowStride;
                }

                // complete
                return true;
            });

            // complete
            if (!parsed)
                return null;
            return new ImageRenderingProfile(this.FileFormat, renderer.AsNonNull()).Also(it =>
            {
                it.ByteOrdering = byteOrdering;
                it.DataOffset = dataOffset;
                it.FramePaddingSize = 6;
                it.Width = width;
                it.Height = height;
                it.PixelStrides = pixelStrides;
                it.RowStrides = rowStrides;
            });
        }


        // Read parameter.
        string ReadParameterValue(Stream stream, out bool isLastParam)
        {
            var stringBuffer = new StringBuilder();
            while (true)
            {
                int c = stream.ReadByte();
                if (c < 0)
                    this.ThrowInvalidFormatException();
                if (c == 0x20)
                {
                    isLastParam = false;
                    break;
                }
                if (c == 0x0a)
                {
                    isLastParam = true;
                    break;
                }
                if (stringBuffer.Length >= 1024)
                    this.ThrowInvalidFormatException();
                stringBuffer.Append((char)c);
            }
            return stringBuffer.ToString();
        }
    }
}
