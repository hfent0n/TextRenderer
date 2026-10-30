using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace TextRenderer.Rendering;

/// <summary>
/// Minimal PNG writer: 8-bit RGB (the canvas is opaque), Sub filter on every row,
/// a single zlib-compressed IDAT chunk.
/// </summary>
public static class PngEncoder
{
    private const int BytesPerPixel = 3;
    private const byte ColourTypeRgb = 2;
    private const byte FilterSub = 1;

    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public const string ContentType = "image/png";

    public static byte[] Encode(Canvas canvas)
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), canvas.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), canvas.Height);
        header[8] = 8;              // bit depth
        header[9] = ColourTypeRgb;
        // 10..12: compression 0 (deflate), filter method 0, interlace 0 (none)
        WriteChunk(output, "IHDR", header);

        WriteChunk(output, "IDAT", Compress(FilterRows(canvas)));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    /// <summary>
    /// Converts RGBA pixels to filtered RGB scanlines: each row is a filter-type byte followed by,
    /// for Sub, each byte minus the same channel of the pixel to its left (mod 256).
    /// </summary>
    private static byte[] FilterRows(Canvas canvas)
    {
        var stride = canvas.Width * BytesPerPixel;
        var filtered = new byte[canvas.Height * (stride + 1)];
        var src = canvas.Pixels;

        for (var y = 0; y < canvas.Height; y++)
        {
            var rowStart = y * (stride + 1);
            filtered[rowStart] = FilterSub;

            for (var x = 0; x < canvas.Width; x++)
            for (var c = 0; c < BytesPerPixel; c++)
            {
                var value = src[(y * canvas.Width + x) * 4 + c];
                var left = x > 0 ? src[(y * canvas.Width + x - 1) * 4 + c] : (byte)0;
                filtered[rowStart + 1 + x * BytesPerPixel + c] = (byte)(value - left);
            }
        }

        return filtered;
    }

    private static byte[] Compress(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(data);
        return compressed.ToArray();
    }

    /// <summary>Length (big-endian), type, data, then CRC-32 of type + data.</summary>
    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        var typeBytes = Encoding.ASCII.GetBytes(type);

        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        output.Write(typeBytes);
        output.Write(data);

        var crc = new Crc32();
        crc.Append(typeBytes);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc.GetCurrentHashAsUInt32());
        output.Write(buffer);
    }
}
