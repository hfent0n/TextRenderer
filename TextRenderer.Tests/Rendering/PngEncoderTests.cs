using System.Buffers.Binary;
using System.Drawing;
using System.IO.Compression;
using System.Text;
using TextRenderer.Rendering;

namespace TextRenderer.Tests.Rendering;

public class PngEncoderTests
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private sealed record Chunk(string Type, byte[] Data, uint Crc);

    /// <summary>Splits a PNG into chunks. Independent of the encoder.</summary>
    private static List<Chunk> ReadChunks(byte[] png)
    {
        var chunks = new List<Chunk>();
        var pos = Signature.Length;
        while (pos < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, length).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + length));
            chunks.Add(new Chunk(type, data, crc));
            pos += 12 + length;
        }
        return chunks;
    }

    /// <summary>Bit-by-bit CRC-32 (PNG / zlib polynomial), written independently of System.IO.Hashing.</summary>
    private static uint ReferenceCrc(string type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in Encoding.ASCII.GetBytes(type).Concat(data))
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>Decompresses IDAT and undoes the per-row filter, returning RGB bytes.</summary>
    private static (byte[] Rgb, byte[] FilterTypes) Decode(byte[] png, int width, int height)
    {
        var idat = ReadChunks(png).Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
        using var zlib = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var bytes = raw.ToArray();

        var stride = width * 3;
        Assert.Equal(height * (stride + 1), bytes.Length);

        var rgb = new byte[height * stride];
        var filters = new byte[height];
        for (var y = 0; y < height; y++)
        {
            filters[y] = bytes[y * (stride + 1)];
            for (var i = 0; i < stride; i++)
            {
                var value = bytes[y * (stride + 1) + 1 + i];
                var left = i >= 3 ? rgb[y * stride + i - 3] : (byte)0;
                rgb[y * stride + i] = filters[y] == 1 ? (byte)(value + left) : value;
            }
        }
        return (rgb, filters);
    }

    private static Canvas Sample()
    {
        var canvas = new Canvas(3, 2, Color.FromArgb(250, 240, 230));
        canvas.Blend(1, 0, 1, Color.FromArgb(10, 20, 30));
        canvas.Blend(0, 1, 1, Color.FromArgb(40, 50, 60));
        canvas.Blend(2, 1, 0.5f, Color.Black);
        return canvas;
    }

    [Fact]
    public void Starts_with_the_png_signature()
    {
        var png = new PngEncoder().Encode(Sample());

        Assert.Equal(Signature, png[..8]);
    }

    [Fact]
    public void Writes_ihdr_idat_iend_in_order()
    {
        var types = ReadChunks(new PngEncoder().Encode(Sample())).Select(c => c.Type);

        Assert.Equal(["IHDR", "IDAT", "IEND"], types);
    }

    [Fact]
    public void Ihdr_describes_an_8_bit_rgb_non_interlaced_image()
    {
        var ihdr = ReadChunks(new PngEncoder().Encode(Sample()))[0].Data;

        Assert.Equal(13, ihdr.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(ihdr));       // width
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4)));  // height
        Assert.Equal(new byte[] { 8, 2, 0, 0, 0 }, ihdr[8..]);             // depth, RGB, deflate, filter 0, no interlace
    }

    [Fact]
    public void Ends_with_the_standard_iend_chunk()
    {
        var png = new PngEncoder().Encode(Sample());

        // Length 0, "IEND", CRC AE 42 60 82: identical in every valid PNG.
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }, png[^12..]);
    }

    [Fact]
    public void Every_chunk_crc_matches_an_independent_crc()
    {
        foreach (var chunk in ReadChunks(new PngEncoder().Encode(Sample())))
            Assert.Equal(ReferenceCrc(chunk.Type, chunk.Data), chunk.Crc);
    }

    [Fact]
    public void Round_trips_pixels_through_zlib_and_the_sub_filter()
    {
        var canvas = Sample();

        var (rgb, filters) = Decode(new PngEncoder().Encode(canvas), 3, 2);

        Assert.All(filters, f => Assert.Equal(1, f));  // Sub on every row
        var expected = canvas.Pixels.Where((_, i) => i % 4 != 3).ToArray();
        Assert.Equal(expected, rgb);
    }

    [Fact]
    public void Round_trips_a_real_render()
    {
        var pipeline = new RenderPipeline(new SixLaborsFontSource(TestPaths.Font("DejaVuSerif.ttf")));
        var canvas = pipeline.Render("Wave @ 12%", new RenderOptions { FontSize = 24 });

        var (rgb, _) = Decode(new PngEncoder().Encode(canvas), canvas.Width, canvas.Height);

        Assert.Equal(canvas.Pixels.Where((_, i) => i % 4 != 3).ToArray(), rgb);
    }

    [Fact]
    public void Sub_filter_wraps_negative_differences()
    {
        // Bright then dark: 10 − 250 must wrap to 16 (mod 256), not saturate to 0.
        var canvas = new Canvas(2, 1, Color.FromArgb(250, 250, 250));
        canvas.Blend(1, 0, 1, Color.FromArgb(10, 10, 10));

        var (rgb, _) = Decode(new PngEncoder().Encode(canvas), 2, 1);

        Assert.Equal(new byte[] { 250, 250, 250, 10, 10, 10 }, rgb);
    }

    [Fact]
    public void Flat_image_compresses_to_a_tiny_file()
    {
        var png = new PngEncoder().Encode(new Canvas(1000, 1000, Color.White));

        // 3 MB of raw RGB; with Sub every row is the first pixel followed by zeros.
        // CompressionLevel.Fastest gets about 100× (~31 KB); require at least 50×.
        Assert.True(png.Length < 3_000_000 / 50, $"{png.Length} bytes");
    }

    [Fact]
    public void Single_pixel_canvas_is_valid()
    {
        var (rgb, _) = Decode(new PngEncoder().Encode(new Canvas(1, 1, Color.Red)), 1, 1);

        Assert.Equal(new byte[] { 255, 0, 0 }, rgb);
    }

    [Fact]
    public void Reports_png_content_type()
    {
        Assert.Equal("image/png", new PngEncoder().ContentType);
    }
}
