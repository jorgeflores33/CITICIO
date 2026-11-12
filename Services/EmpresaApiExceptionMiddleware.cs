using Microsoft.Data.SqlClient;

namespace Citicio.Services;

/// <summary>Devuelve errores de API en JSON, sin exponer credenciales ni SQL interno.</summary>
public sealed class EmpresaApiExceptionMiddleware(RequestDelegate next, ILogger<EmpresaApiExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (SqlException ex) when (ctx.Request.Path.StartsWithSegments("/api/empresa"))
        {
            logger.LogError(ex, "Error de SQL Server en panel empresarial");
            if (ctx.Response.HasStarted) throw;
            ctx.Response.StatusCode = ex.Number switch
            {
                2601 or 2627 => StatusCodes.Status409Conflict,
                547 => StatusCodes.Status409Conflict,
                >= 59510 and <= 59513 => StatusCodes.Status409Conflict,
                >= 59500 and <= 59549 => StatusCodes.Status400BadRequest,
                2812 => StatusCodes.Status503ServiceUnavailable,
                >= 54000 and <= 54999 => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status503ServiceUnavailable
            };
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Number switch
            {
                2601 or 2627 => "El registro ya existe.",
                547 => "El registro está relacionado con otros datos.",
                >= 59500 and <= 59549 => ex.Message,
                2812 => "Falta un procedimiento almacenado de CITICIO en SQL Server.",
                >= 54000 and <= 54999 => ex.Message,
                _ => "No se pudo conectar o consultar SQL Server. Revisa la configuración."
            }});
        }
    }
}
