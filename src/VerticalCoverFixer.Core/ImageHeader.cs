using System;
using System.IO;

namespace VerticalCoverFixer.Core
{
    public enum ImageFormat
    {
        Jpeg,
        Png,
        Gif,
        Bmp,
        WebP,
        Ico,
    }

    public readonly struct ImageInfo
    {
        public ImageInfo(ImageFormat format, int width, int height)
        {
            Format = format;
            Width = width;
            Height = height;
        }

        public ImageFormat Format { get; }

        public int Width { get; }

        public int Height { get; }

        public CoverShape Shape => CoverShapes.Classify(Width, Height);

        public string Extension => Format switch
        {
            ImageFormat.Jpeg => ".jpg",
            ImageFormat.Png => ".png",
            ImageFormat.Gif => ".gif",
            ImageFormat.Bmp => ".bmp",
            ImageFormat.WebP => ".webp",
            _ => ".ico",
        };
    }

    /// <summary>
    /// Reads an image's format and size from its first few bytes, without decoding the picture.
    /// Checking a few thousand covers this way takes seconds and needs no image codecs.
    /// </summary>
    public static class ImageHeader
    {
        public static ImageInfo? TryRead(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    return TryRead(stream);
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static ImageInfo? TryRead(byte[] data)
        {
            using (var stream = new MemoryStream(data, writable: false))
            {
                return TryRead(stream);
            }
        }

        /// <param name="stream">Must be seekable (file and memory streams are).</param>
        public static ImageInfo? TryRead(Stream stream)
        {
            var h = new byte[30];
            int n = ReadUpTo(stream, h, h.Length);

            if (n >= 24 && h[0] == 0x89 && Ascii(h, 1, "PNG"))
            {
                return Valid(ImageFormat.Png, BigEndian32(h, 16), BigEndian32(h, 20));
            }

            if (n >= 10 && Ascii(h, 0, "GIF8"))
            {
                return Valid(ImageFormat.Gif, h[6] | h[7] << 8, h[8] | h[9] << 8);
            }

            if (n >= 26 && Ascii(h, 0, "BM"))
            {
                int height = LittleEndian32(h, 22); // negative means top-down rows, same size
                return Valid(ImageFormat.Bmp, LittleEndian32(h, 18), height == int.MinValue ? 0 : Math.Abs(height));
            }

            if (n >= 30 && Ascii(h, 0, "RIFF") && Ascii(h, 8, "WEBP"))
            {
                return WebP(h);
            }

            if (n >= 8 && h[0] == 0 && h[1] == 0 && h[2] == 1 && h[3] == 0 && (h[4] | h[5] << 8) > 0)
            {
                return Valid(ImageFormat.Ico, h[6] == 0 ? 256 : h[6], h[7] == 0 ? 256 : h[7]);
            }

            if (n >= 4 && h[0] == 0xFF && h[1] == 0xD8)
            {
                stream.Position = 2;
                return Jpeg(stream);
            }

            return null;
        }

        private static ImageInfo? WebP(byte[] h)
        {
            if (Ascii(h, 12, "VP8X"))
            {
                return Valid(ImageFormat.WebP, 1 + (h[24] | h[25] << 8 | h[26] << 16), 1 + (h[27] | h[28] << 8 | h[29] << 16));
            }

            if (Ascii(h, 12, "VP8L") && h[20] == 0x2F)
            {
                return Valid(ImageFormat.WebP,
                    1 + (h[21] | (h[22] & 0x3F) << 8),
                    1 + (h[22] >> 6 | h[23] << 2 | (h[24] & 0x0F) << 10));
            }

            if (Ascii(h, 12, "VP8 ") && h[23] == 0x9D && h[24] == 0x01 && h[25] == 0x2A)
            {
                return Valid(ImageFormat.WebP, (h[26] | h[27] << 8) & 0x3FFF, (h[28] | h[29] << 8) & 0x3FFF);
            }

            return null;
        }

        /// <summary>Walks the JPEG's segments until the frame header (SOFn), which holds the size.</summary>
        private static ImageInfo? Jpeg(Stream stream)
        {
            var buffer = new byte[5];
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    return null;
                }

                if (b != 0xFF)
                {
                    continue; // tolerate stray bytes between segments
                }

                int marker;
                do
                {
                    marker = stream.ReadByte();
                }
                while (marker == 0xFF);

                if (marker < 0 || marker == 0xD9 || marker == 0xDA)
                {
                    return null; // end of file or start of pixel data, with no frame header seen
                }

                // Standalone markers carry no length.
                if (marker == 0x00 || marker == 0x01 || marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    continue;
                }

                if (ReadUpTo(stream, buffer, 2) < 2)
                {
                    return null;
                }

                int length = buffer[0] << 8 | buffer[1];
                if (length < 2)
                {
                    return null;
                }

                // C4 (Huffman tables), C8 (reserved) and CC (arithmetic coding) share the range but aren't frame headers.
                bool isFrameHeader = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isFrameHeader)
                {
                    if (ReadUpTo(stream, buffer, 5) < 5)
                    {
                        return null;
                    }

                    // precision, height (2 bytes), width (2 bytes)
                    return Valid(ImageFormat.Jpeg, buffer[3] << 8 | buffer[4], buffer[1] << 8 | buffer[2]);
                }

                stream.Seek(length - 2, SeekOrigin.Current);
            }
        }

        private static ImageInfo? Valid(ImageFormat format, int width, int height) =>
            width > 0 && height > 0 ? new ImageInfo(format, width, height) : (ImageInfo?)null;

        private static int ReadUpTo(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static bool Ascii(byte[] data, int offset, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (data[offset + i] != text[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static int BigEndian32(byte[] d, int i) => d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3];

        private static int LittleEndian32(byte[] d, int i) => d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24;
    }
}
