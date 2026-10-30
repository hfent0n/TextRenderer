using System.Net.Http.Json;
using NBomber.Contracts;
using NBomber.CSharp;

// Load tests against a running service. Not part of `dotnet test`.
//   dotnet run --project TextRenderer.LoadTests -- [baseUrl] [--quick] [--only=name]
// --quick shortens every scenario to 5 seconds, to check the harness itself.

var baseUrl = args.FirstOrDefault(a => a.StartsWith("http")) ?? "http://localhost:5133";
var quick = args.Contains("--quick");
var only = args.FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..];

object Typical() => new { text = "Order #12345 shipped", font = "dejavu-sans", fontSize = 32 };
object Medium() => new { text = "The quick brown fox jumps over the lazy dog.", font = "dejavu-serif", fontSize = 96 };
object Large() => new { text = "The quick brown fox jumps over the lazy d", font = "dejavu-serif", fontSize = 512 };
object Mixed() => Random.Shared.Next(100) switch { < 80 => Typical(), < 95 => Medium(), _ => Large() };

(string Name, int Copies, int Seconds, Func<object> Request)[] scenarios =
[
    ("typical", 10, 60, Typical),
    ("large", 4, 60, Large),
    ("mixed", 20, 60, Mixed),
    ("overload", 400, 30, Typical),
    ("soak", 20, 300, Mixed),
];

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };

Console.WriteLine("| Scenario | Copies | Seconds | OK/s | p50 ms | p95 ms | p99 ms | Max ms | OK | Failed | Status codes |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");

foreach (var (name, copies, seconds, request) in scenarios.Where(s => only is null || s.Name == only))
{
    var duration = quick ? 5 : seconds;
    var scenario = Scenario.Create(name, async _ =>
        {
            using var response = await http.PostAsJsonAsync("/render", request());
            var body = await response.Content.ReadAsByteArrayAsync();
            var status = ((int)response.StatusCode).ToString();
            return response.IsSuccessStatusCode
                ? Response.Ok(statusCode: status, sizeBytes: body.Length)
                : Response.Fail(statusCode: status);
        })
        .WithoutWarmUp()
        .WithMaxFailCount(int.MaxValue)  // NBomber otherwise stops at 5,000 failures, and 429s count as failures
        .WithLoadSimulations(Simulation.KeepConstant(copies, TimeSpan.FromSeconds(duration)));

    var stats = NBomberRunner.RegisterScenarios(scenario).WithoutReports().Run().ScenarioStats[0];

    var latency = stats.Ok.Latency;
    var codes = stats.Ok.StatusCodes.Concat(stats.Fail.StatusCodes).Select(c => $"{c.StatusCode}: {c.Count}");
    Console.WriteLine($"| {name} | {copies} | {duration} | {(double)stats.Ok.Request.Count / duration:F1} | {latency.Percent50:F1} | {latency.Percent95:F1} " +
                      $"| {latency.Percent99:F1} | {latency.MaxMs:F1} | {stats.Ok.Request.Count} | {stats.Fail.Request.Count} | {string.Join(", ", codes)} |");
}
