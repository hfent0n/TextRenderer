namespace TextRenderer.Rendering;

public interface IImageEncoder
{
    string ContentType { get; }

    byte[] Encode(Canvas canvas);
}
