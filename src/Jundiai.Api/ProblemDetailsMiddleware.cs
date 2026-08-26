using System.Text.Json;

namespace Jundiai.Api;

public static class JundiaiProblemDetailsExtensions
{
    public static IApplicationBuilder UseJundiaiProblemDetails(this IApplicationBuilder app) =>
        app.UseMiddleware<JundiaiProblemDetailsMiddleware>();
}

public sealed class JundiaiProblemDetailsMiddleware(RequestDelegate next, ILogger<JundiaiProblemDetailsMiddleware> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (KeyNotFoundException)
        {
            await Write(context, StatusCodes.Status404NotFound, "Recurso não encontrado", "O recurso solicitado não foi localizado.");
        }
        catch (ArgumentException ex)
        {
            await Write(context, StatusCodes.Status400BadRequest, "Entrada inválida", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await Write(context, StatusCodes.Status409Conflict, "Regra de negócio", ex.Message);
        }
        catch (Exception ex)
        {
            var correlationId = context.TraceIdentifier;
            logger.LogError(ex, "Unhandled API error. CorrelationId={CorrelationId} Path={Path}", correlationId, context.Request.Path);
            await Write(context, StatusCodes.Status500InternalServerError, "Falha interna", $"Ocorreu uma falha inesperada. Referência: {correlationId}", correlationId);
        }
    }

    private static async Task Write(HttpContext context, int status, string title, string detail, string? correlationId = null)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var body = new
        {
            type = $"https://jundiai-healthos.local/problems/{status}",
            title,
            status,
            detail,
            correlationId = correlationId ?? context.TraceIdentifier
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
