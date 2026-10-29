using Microsoft.Extensions.Options;
using TextRenderer.Rendering;
using TextRenderer.Service;

var builder = WebApplication.CreateBuilder(args);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddOptions<RenderLimits>()
    .Bind(builder.Configuration.GetSection(RenderLimits.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RenderLimits>, RenderLimitsValidation>();

builder.Services.AddSingleton(sp => FontRegistry.Load(sp.GetRequiredService<IOptions<RenderLimits>>().Value.FontDirectory));
builder.Services.AddSingleton(sp => new RenderRequestValidator(
    sp.GetRequiredService<FontRegistry>(), sp.GetRequiredService<IOptions<RenderLimits>>().Value));

builder.Services.AddHealthChecks().AddCheck<FontsHealthCheck>("fonts");

var app = builder.Build();

// Fail fast: validate limits and load every font before accepting requests (PLAN_PART_2 decisions 10 and 15).
app.Services.GetRequiredService<FontRegistry>();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapRenderEndpoints();
app.MapHealthChecks("/health");

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
