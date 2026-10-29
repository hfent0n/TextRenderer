using System.Text;

namespace TextRenderer.Rendering;

/// <summary>Binary PPM (P6): an ASCII header followed by raw RGB bytes. Alpha is dropped.</summary>
public sealed class PpmEncoder : IImageEncoder
{
    public string ContentType => "image/x-portable-pixmap";

    public byte[] Encode(Canvas canvas)
    {
        var header = Encoding.ASCII.GetBytes($"P6\n{canvas.Width} {canvas.Height}\n255\n");
        var output = new byte[header.Length + canvas.Width * canvas.Height * 3];
        header.CopyTo(output, 0);

        var src = canvas.Pixels;
        for (int s = 0, d = header.Length; s < src.Length; s += 4, d += 3)
        {
            output[d] = src[s];
            output[d + 1] = src[s + 1];
            output[d + 2] = src[s + 2];
        }

        return output;
    }
}
