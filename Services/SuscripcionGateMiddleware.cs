using System.Security.Claims;
using Citicio.Auth;
using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

/// <summary>
/// Filtro de acceso real en servidor para las DOS cookies empresariales.
/// La administración general, los clientes, el registro y el pago permanecen accesibles.
/// Si SQL falla, nunca se permite el acceso empresarial sin verificar la mensualidad.
/// </summary>
public sealed class SuscripcionGateMiddleware(RequestDelegate next, ILogger<SuscripcionGateMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx, SqlStore db, CurrentUserService current)
    {
        var path = ctx.Request.Path;
        if (ctx.User.Identity?.IsAuthenticated != true ||
            path.StartsWithSegments("/Suscripcion") ||
            path.StartsWithSegments("/EmpresaSuscripcion") ||
            path.StartsWithSegments("/Cuenta") ||
            path.StartsWithSegments("/Home") ||
            path.StartsWithSegments("/Publico") ||
            path.StartsWithSegments("/api/publico") ||
            path.StartsWithSegments("/api/catalogo") ||
            path.StartsWithSegments("/api/health") ||
            path.StartsWithSegments("/Pago/Webhook") ||
            path.StartsWithSegments("/Pago/ConnectWebhook") ||
            path.StartsWithSegments("/api/empresa/login") ||
            path.StartsWithSegments("/api/empresa/logout") ||
            path.StartsWithSegments("/api/empresa/csrf") ||
            path.StartsWithSegments("/api/empresa/me") ||
            path.StartsWithSegments("/api/empresa/cobros") ||
            // Liquidación y atención de citas anteriores siguen habilitadas al vencer la mensualidad.
            path.StartsWithSegments("/Spei/Pendientes") ||
            path.StartsWithSegments("/Spei/Resolver") ||
            path.StartsWithSegments("/Panel/Pagos") ||
            path.StartsWithSegments("/Panel/Citas") ||
            path.StartsWithSegments("/Citas/CambiarEstado") ||
            path.StartsWithSegments("/api/empresa/citas/estado"))
        { await next(ctx); return; }

        // El administrador general configura la cuenta receptora y la tarifa en el primer acceso.
        // Un registro ya existente pasa por esta pantalla solo cuando aún no hay configuración.
        if (ctx.User.IsInRole("admin_citicio"))
        {
            if (!int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier),out var adminId))
            { ctx.Response.StatusCode=401; return; }
            try
            {
                var cfg=await db.CallAsync("dbo.sp_citicio_suscripcion_configuracion",SqlStore.P(
                    ("id_usuario_actor",adminId),("accion","obtener")),ctx.RequestAborted);
                if (cfg.Count==0) {ctx.Response.Redirect("/Suscripcion/Configuracion");return;}
            }
            catch(SqlException ex)
            {
                log.LogError("Configuración inicial no verificable SQL {Numero}",ex.Number);
                ctx.Response.StatusCode=503;
                await ctx.Response.WriteAsync("No se pudo consultar la configuración inicial. Revisa la migración SQL 11.");
                return;
            }
            await next(ctx); return;
        }
        var empresaApi=path.StartsWithSegments("/api/empresa") || path.StartsWithSegments("/empresa/panel");
        var rolMvc=ctx.User.IsInRole("admin_empresa") ||
            ctx.User.IsInRole("encargado_sucursal") || ctx.User.IsInRole("empleado");
        if (!empresaApi && !rolMvc) { await next(ctx); return; }
        try
        {
            var userId=0;
            if(empresaApi)
            {
                var user=await current.GetAsync(ctx.User);
                if(user is null){ctx.Response.StatusCode=403;return;}
                userId=user.Id;
            }
            else if (!int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier),out userId))
            {ctx.Response.StatusCode=401;return;}
            var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",userId)),ctx.RequestAborted)).FirstOrDefault();
            if (estado?.B("suscripcion_activa")==true) { await next(ctx);return; }
            if(empresaApi)
            {
                if(path.StartsWithSegments("/api/empresa"))
                {ctx.Response.StatusCode=402;await ctx.Response.WriteAsJsonAsync(new {error="La mensualidad de CITICIO está pendiente o vencida.",url="/EmpresaSuscripcion"});}
                else ctx.Response.Redirect("/EmpresaSuscripcion");
            }
            else ctx.Response.Redirect("/Suscripcion");
        }
        catch(SqlException ex)
        {
            log.LogError("Suscripción no verificable: SQL {Number}",ex.Number);
            ctx.Response.StatusCode=503;
            await ctx.Response.WriteAsync("CITICIO no puede verificar la mensualidad. Comprueba el servicio SQL o la migración de suscripciones.");
        }
    }
}
