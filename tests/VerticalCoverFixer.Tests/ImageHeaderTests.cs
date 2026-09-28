using VerticalCoverFixer.Core;

namespace VerticalCoverFixer.Tests;

public class ImageHeaderTests
{
    [Fact]
    public void ReadsPng()
    {
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }
            .Concat(BigEndian(600)).Concat(BigEndian(900)).Concat(new byte[10]).ToArray();

        AssertInfo(png, ImageFormat.Png, 600, 900);
    }

    [Fact]
    public void ReadsGif()
    {
        var gif = "GIF89a"u8.ToArray().Concat(new byte[] { 0x2C, 0x01, 0xC2, 0x01, 0, 0, 0, 0 }).ToArray();

        AssertInfo(gif, ImageFormat.Gif, 300, 450);
    }

    [Fact]
    public void ReadsBottomUpAndTopDownBmp()
    {
        AssertInfo(Bmp(460, 215), ImageFormat.Bmp, 460, 215);
        AssertInfo(Bmp(460, -215), ImageFormat.Bmp, 460, 215);
    }

    [Fact]
    public void ReadsJpegPastExifSegment()
    {
        var jpeg = new List<byte> { 0xFF, 0xD8 };
        jpeg.AddRange(new byte[] { 0xFF, 0xE1, 0x00, 0x10 }); // APP1, 14 bytes of payload
        jpeg.AddRange(new byte[14]);
        jpeg.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x04, 0, 0 }); // Huffman table: not a frame header
        jpeg.AddRange(new byte[] { 0xFF, 0xC2, 0x00, 0x11, 0x08, 0x03, 0x84, 0x02, 0x58 }); // progressive, 600x900
        jpeg.AddRange(new byte[12]);

        AssertInfo(jpeg.ToArray(), ImageFormat.Jpeg, 600, 900);
    }

    [Fact]
    public void JpegWithoutFrameHeaderIsUnreadable()
    {
        Assert.Null(ImageHeader.TryRead(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9 }));
    }

    [Fact]
    public void ReadsWebPVariants()
    {
        var vp8x = WebPHeader("VP8X");
        vp8x[24] = (600 - 1) & 0xFF; vp8x[25] = (600 - 1) >> 8;
        vp8x[27] = (900 - 1) & 0xFF; vp8x[28] = (900 - 1) >> 8;
        AssertInfo(vp8x, ImageFormat.WebP, 600, 900);

        var vp8 = WebPHeader("VP8 ");
        vp8[23] = 0x9D; vp8[24] = 0x01; vp8[25] = 0x2A;
        vp8[26] = 600 & 0xFF; vp8[27] = 600 >> 8;
        vp8[28] = 900 & 0xFF; vp8[29] = 900 >> 8;
        AssertInfo(vp8, ImageFormat.WebP, 600, 900);

        // Lossless: 14-bit width-1 and height-1 packed together after the 0x2F signature.
        var vp8l = WebPHeader("VP8L");
        uint bits = (600 - 1) | ((900 - 1) << 14);
        vp8l[20] = 0x2F;
        vp8l[21] = (byte)bits; vp8l[22] = (byte)(bits >> 8); vp8l[23] = (byte)(bits >> 16); vp8l[24] = (byte)(bits >> 24);
        AssertInfo(vp8l, ImageFormat.WebP, 600, 900);
    }

    [Fact]
    public void UnknownOrTruncatedDataIsUnreadable()
    {
        Assert.Null(ImageHeader.TryRead(Array.Empty<byte>()));
        Assert.Null(ImageHeader.TryRead("<html>not an image</html>"u8.ToArray()));
        Assert.Null(ImageHeader.TryRead(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }));
    }

    [Fact]
    public void MissingFileIsUnreadable()
    {
        Assert.Null(ImageHeader.TryRead(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")));
    }

    private static void AssertInfo(byte[] data, ImageFormat format, int width, int height)
    {
        var info = ImageHeader.TryRead(data);
        Assert.NotNull(info);
        Assert.Equal(format, info.Value.Format);
        Assert.Equal(width, info.Value.Width);
        Assert.Equal(height, info.Value.Height);
    }

    private static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] Bmp(int width, int height)
    {
        var data = new byte[26];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.GetBytes(width).CopyTo(data, 18);
        BitConverter.GetBytes(height).CopyTo(data, 22);
        return data;
    }

    private static byte[] WebPHeader(string chunk)
    {
        var data = new byte[30];
        "RIFF"u8.ToArray().CopyTo(data, 0);
        "WEBP"u8.ToArray().CopyTo(data, 8);
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(data, 12);
        return data;
    }
}
