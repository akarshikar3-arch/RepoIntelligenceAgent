using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace RepoIntel.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Path}", ctx.Request.Path);
            if (ctx.Response.HasStarted) throw;

            ctx.Response.Clear();
            ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            ctx.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Title = "Unexpected error",
                Status = ctx.Response.StatusCode,
                Detail = ex.Message,
                Instance = ctx.Request.Path
            };
            await ctx.Response.WriteAsJsonAsync(problem);
        }
    }
}
