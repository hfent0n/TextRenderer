using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TextRenderer.Rendering;

namespace TextRenderer.Service;

public static partial class RenderEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // A typo like "forground" is an error, not silently ignored (PLAN_PART_2 decision 16).
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static void MapRenderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/render", RenderAsync)
            .WithName("Render")
            .WithSummary("Render a single line of text in a registered font.")
            .Accepts<RenderRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: PngEncoder.ContentType)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/fonts", (FontRegistry fonts) => fonts.Fonts)
            .WithName("ListFonts")
            .WithSummary("List the fonts that can be used in POST /render.");
    }

    private static async Task<IResult> RenderAsync(
        HttpRequest http, RenderRequestValidator validator, RenderGate gate, RenderTelemetry telemetry,
        IOptions<RenderLimits> limits, CancellationToken cancellationToken)
    {
        IResult Problem(int status, string title, string detail)
        {
            telemetry.Rejected(status, 1);
            return Results.Problem(statusCode: status, title: title, detail: detail);
        }

        IResult Invalid(IEnumerable<KeyValuePair<string, string[]>> errors)
        {
            telemetry.Rejected(StatusCodes.Status400BadRequest, errors.Sum(e => e.Value.Length));
            return Results.ValidationProblem(errors);
        }

        if (!http.HasJsonContentType())
            return Problem(StatusCodes.Status415UnsupportedMediaType, "Unsupported media type",
                "POST /render requires Content-Type: application/json.");

        var body = await ReadBodyAsync(http, limits.Value.MaxRequestBodyBytes, cancellationToken);
        if (body is null)
            return Problem(StatusCodes.Status413PayloadTooLarge, "Request body too large",
                $"The request body must be at most {limits.Value.MaxRequestBodyBytes} bytes.");

        RenderRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<RenderRequest>(body, Json);
        }
        catch (JsonException ex)
        {
            return Invalid(DescribeJsonError(ex));
        }

        if (request is null)
            return Invalid(new Dictionary<string, string[]> { ["body"] = ["Request body must be a JSON object."] });

        var result = validator.Validate(request);
        if (!result.IsValid)
            return Invalid(result.Errors);

        var job = result.Job!;

        // Only render + encode needs a slot (decision 18). cancellationToken is RequestAborted,
        // so a client that disconnects while queued leaves the queue.
        var queueStart = Stopwatch.GetTimestamp();
        using var lease = await gate.AcquireAsync(cancellationToken);
        var queued = Stopwatch.GetElapsedTime(queueStart);
        if (!lease.IsAcquired)
        {
            telemetry.Overloaded(job, queued);
            var retryAfter = limits.Value.RetryAfterSeconds;
            http.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                detail: $"The service is busy. Retry after {retryAfter} second{(retryAfter == 1 ? "" : "s")}.");
        }

        // The timeout starts once the slot is held: time spent queued doesn't count (decision 19).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.Value.RenderTimeout);
        var renderStart = Stopwatch.GetTimestamp();
        try
        {
            var canvas = RenderPipeline.Draw(job.Layout, job.Options, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            var image = PngEncoder.Encode(canvas);
            telemetry.Rendered(job, canvas.Width, canvas.Height, queued, Stopwatch.GetElapsedTime(renderStart));
            return Results.File(image, PngEncoder.ContentType);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            telemetry.TimedOut(job, queued, Stopwatch.GetElapsedTime(renderStart));
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Render timed out",
                detail: $"Rendering took longer than the {limits.Value.RenderTimeout.TotalSeconds:0.###} second limit. " +
                        "Use shorter text or a smaller font size.");
        }
    }

    /// <summary>Reads the body, or returns null if it exceeds <paramref name="maxBytes"/> (declared or actual).</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest http, long maxBytes, CancellationToken cancellationToken)
    {
        if (http.ContentLength > maxBytes)
            return null;

        // Count while reading too: chunked requests have no Content-Length, and it can lie.
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await http.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Turns a deserialization failure into a field error without leaking .NET type names:
    /// unknown property → that property; wrong type → that field; anything else → "body".
    /// </summary>
    private static Dictionary<string, string[]> DescribeJsonError(JsonException ex)
    {
        var unknown = UnmappedProperty().Match(ex.Message);
        if (unknown.Success)
            return new() { [unknown.Groups[1].Value] = [$"Unknown property '{unknown.Groups[1].Value}'."] };

        var field = ex.Path is { Length: > 2 } path && path.StartsWith("$.") ? path[2..] : null;
        if (field is not null)
            return new() { [field] = [$"Invalid value for '{field}'."] };

        return new() { ["body"] = [$"Request body is not valid JSON (line {ex.LineNumber + 1}, position {ex.BytePositionInLine + 1})."] };
    }

    [GeneratedRegex("The JSON property '([^']*)' could not be mapped")]
    private static partial Regex UnmappedProperty();
}

/// <summary>Rejects nonsensical limits at startup (PLAN_PART_2 decision 15).</summary>
public sealed class RenderLimitsValidation : IValidateOptions<RenderLimits>
{
    public ValidateOptionsResult Validate(string? name, RenderLimits options)
    {
        var errors = options.Validate();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
