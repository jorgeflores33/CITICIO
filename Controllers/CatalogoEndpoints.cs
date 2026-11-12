using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using static Citicio.Services.EndpointHelpers;
using static Citicio.Auth.SessionHelpers;

namespace Citicio.Controllers;

public static class CatalogoEndpoints
{
    public static IEndpointRouteBuilder MapCatalogoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", (IConfiguration cfg) => Results.Ok(new {
            api = "CITICIO Clientes", estado = "en_linea",
            sqlConfigurado = !string.IsNullOrWhiteSpace(cfg.GetConnectionString("AgenciaCitas"))
        }));
        app.MapGet("/api/catalogo", async Task<IResult> (string? texto, int? giro, int? estado, int? municipio, ClienteSqlStore db, CancellationToken ct) => {
            if (texto?.Length > 150) return Results.BadRequest(new { error = "Búsqueda demasiado larga." });
            var rows=await db.Sp("dbo.sp_citicio_catalogo_publicado", ct,
                P("@texto", null), P("@id_giro", giro),
                P("@id_entidad_federativa", estado), P("@id_municipio", municipio));
            return Results.Ok(rows.Where(x=>BusquedaFlexible.Coincide(texto,
                Convert.ToString(x.GetValueOrDefault("empresa")), Convert.ToString(x.GetValueOrDefault("servicio")),
                Convert.ToString(x.GetValueOrDefault("sucursal")), Convert.ToString(x.GetValueOrDefault("giro"))))
                .Take(200).ToList());
        });
        app.MapGet("/api/top10", async Task<IResult> (int? municipio, SqlStore store, CancellationToken ct) => {
            await using var cn=await store.OpenAsync(ct);
            var rows=await RankingEmpresasService.ObtenerAsync(cn,ct,municipio);
            return Results.Ok(rows.Select(x=>x.Cells).ToList());
        });
        app.MapGet("/api/giros", async Task<IResult> (ClienteSqlStore db, CancellationToken ct) =>
            Results.Ok(await db.Sp("dbo.sp_citicio_lecturas", ct, P("@accion", "giros_publicos"))));
        app.MapGet("/api/estados", async Task<IResult> (ClienteSqlStore db, CancellationToken ct) =>
            Results.Ok(await db.Sp("dbo.sp_geo_estados", ct)));
        app.MapGet("/api/municipios/{id:int}", async Task<IResult> (int id, ClienteSqlStore db, CancellationToken ct) =>
            Results.Ok(await db.Sp("dbo.sp_geo_municipios", ct, P("@id_entidad_federativa", id))));
        app.MapGet("/api/disponibilidad", async Task<IResult> (int sucursal, int servicio, DateOnly fecha, TimeOnly hora, ClienteSqlStore db, CancellationToken ct) => {
            if (sucursal < 1 || servicio < 1 || fecha < DateOnly.FromDateTime(MexicoClock.Today)) return Results.BadRequest(new { error = "Fecha o servicio inválido." });
            await using var cn=await db.Open(ct);
            if(!await SuscripcionAccesoService.PermitidoAsync(cn,null,sucursal,servicio,ct))
                return Results.Ok(Array.Empty<object>());
            var rows = await db.Sp("dbo.sp_citas_consultar_disponibilidad", ct, cn, null,
                P("@id_sucursal", sucursal), P("@id_servicio", servicio),
                DateParam("@fecha", fecha), TimeParam("@hora_inicio", hora));
            return Results.Ok(rows);
        });


        return app;
    }
}
