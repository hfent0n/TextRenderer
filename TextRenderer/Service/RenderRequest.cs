namespace TextRenderer.Service;

/// <summary>
/// JSON body of <c>POST /render</c>. Every field is nullable so a missing field can be reported
/// as a validation error instead of failing deserialization.
/// </summary>
public sealed record RenderRequest
{
    public string? Text { get; init; }
    public string? Font { get; init; }
    public float? FontSize { get; init; }
    public int? Padding { get; init; }
    public string? Foreground { get; init; }
    public string? Background { get; init; }
}
