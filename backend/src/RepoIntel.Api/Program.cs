using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using RepoIntel.Api.Middleware;
using RepoIntel.Application;
using RepoIntel.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging --------------------------------------------------------------
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---- Services -------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RepoIntel API",
        Version = "v1",
        Description = "Repository Intelligence & Code Reviewer agent backend."
    });
});

var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(o => o.AddPolicy("AgentHub", p =>
{
    p.WithOrigins(configuredOrigins)
        .SetIsOriginAllowed(origin =>
        {
            if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                return true;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;

            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
}));

builder.Services
    .AddRepoIntelApplication()
    .AddRepoIntelInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();

// ---- App ------------------------------------------------------------------
var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("AgentHub");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

app.Run();

public partial class Program;
