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

public static class ClienteEndpoints
{
    public static IEndpointRouteBuilder MapClienteEndpoints(this IEndpointRouteBuilder app)
    {
        var clients = app.MapGroup("/api/cliente").RequireAuthorization("Cliente");
        clients.MapGet("/citas", async Task<IResult> (ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) =>
            Results.Ok(await db.Sp("dbo.sp_citas_panel_usuario", ct, P("@id_usuario", AuthId(user)))));
        clients.MapGet("/citas/estados-pago", async Task<IResult> (ClaimsPrincipal user, SqlStore store) => {
            var citas=await store.CallAsync("dbo.sp_citas_panel_usuario",SqlStore.P(
                ("id_usuario",AuthId(user))));
            var pagadas=new List<int>();
            foreach(var c in citas) {
                var id=c.I("id_cita");
                var lectura=(await store.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
                    ("accion","checkout_cliente"),("id_usuario_actor",AuthId(user)),("id1",id)))).FirstOrDefault();
                if(lectura is not null && lectura.M("total")>0m && lectura.M("pagado")>=lectura.M("total"))
                    pagadas.Add(id);
            }
            return Results.Ok(pagadas);
        });
        clients.MapPost("/citas", async Task<IResult> (NuevaCitaDto input, ClaimsPrincipal user, ClienteSqlStore db, IConfiguration cfg, CancellationToken ct) => {
            if (input.Sucursal <= 0 || input.Servicio <= 0 || input.Empleado <= 0 || input.Observaciones?.Length > 1000)
                return Results.BadRequest(new { error = "Datos de cita no válidos." });
            var initial = input.Fecha.ToDateTime(input.Hora);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(cfg["Citicio:TimeZone"] ?? "America/Mexico_City");
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            if (initial <= localNow || initial > localNow.AddDays(60))
                return Results.BadRequest(new { error = "Selecciona una fecha futura, dentro de los próximos 60 días." });
            await using var cn = await db.Open(ct);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            if(!await SuscripcionAccesoService.PermitidoAsync(cn,tx,input.Sucursal,input.Servicio,ct))
                return Results.Conflict(new { error = "El negocio no tiene una mensualidad de CITICIO pagada y vigente para aceptar citas." });
            var service = await db.Sp("dbo.sp_citicio_lecturas", ct, cn, tx,
                P("@accion", "servicio_horario"), P("@id1", input.Sucursal), P("@id2", input.Servicio));
            if (service.Count == 0) return Results.BadRequest(new { error = "El servicio no está disponible en esa sucursal." });
            var duration = Val.Int(service[0], "duracion_minutos");
            var final = initial.AddMinutes(duration);
            if (final.Date != initial.Date) return Results.BadRequest(new { error = "La cita debe terminar el mismo día." });
            var available = await db.Sp("dbo.sp_citas_consultar_disponibilidad", ct, cn, tx,
                P("@id_sucursal", input.Sucursal), P("@id_servicio", input.Servicio),
                DateParam("@fecha", input.Fecha), TimeParam("@hora_inicio", input.Hora));
            if (!available.Any(e => Val.Int(e, "id_empleado") == input.Empleado))
                return Results.Conflict(new { error = "Ese profesional ya no está disponible. Elige otro horario." });
            var saved = await db.Sp("dbo.sp_citas", ct, cn, tx,
                P("@accion", "insertar"), P("@id_cliente", ClientId(user)),
                P("@id_sucursal", input.Sucursal), P("@id_empleado", input.Empleado),
                P("@fecha", initial), P("@hora_inicio", initial), P("@hora_fin", final),
                P("@observaciones", EmptyAsNull(input.Observaciones, 1000)));
            var idCita = Val.Int(saved[0], "id_cita");
            // El precio contratado sale de SQL Server, jamás del cuerpo HTTP.
            await db.Sp("dbo.sp_detalles_citas", ct, cn, tx,
                P("@accion", "insertar"), P("@id_cita", idCita), P("@id_servicio", input.Servicio),
                P("@precio_contratado", Val.Dec(service[0], "precio")));
            await tx.CommitAsync(ct);
            return Results.Ok(new { mensaje = "Cita agendada correctamente", idCita });
        }).RequireRateLimiting("citas");
        clients.MapPost("/citas/{id:int}/cancelar", async Task<IResult> (int id, ClaimsPrincipal user, ClienteSqlStore db, IConfiguration cfg, CancellationToken ct) => {
            await using var cn = await db.Open(ct);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var own = await db.Sp("dbo.sp_citicio_lecturas", ct, cn, tx,
                P("@accion", "cita_para_estado"), P("@id_usuario_actor", AuthId(user)), P("@id1", id));
            if (own.Count == 0) return Results.NotFound(new { error = "Cita no encontrada." });
            var state = Val.Str(own[0], "nombre_estado");
            if (state != "cita_pendiente" && state != "cita_confirmada")
                return Results.Conflict(new { error = "No se puede cancelar una cita en ese estado." });
            if (Val.Date(own[0], "hora_inicio") <= TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(cfg["Citicio:TimeZone"] ?? "America/Mexico_City")))
                return Results.Conflict(new { error = "La cita ya comenzó o ha finalizado." });
            var stateId = await db.Sp("dbo.sp_citicio_lecturas", ct, cn, tx, P("@accion", "estado_por_nombre"), P("@texto", "cita_cancelada"));
            if (stateId.Count == 0) return Results.Problem("Falta el estado cita_cancelada.", statusCode: 503);
            await db.Sp("dbo.sp_citas", ct, cn, tx, P("@accion", "cambiar_estado"), P("@id_cita", id), P("@id_estado", Val.Int(stateId[0], "id_estado")));
            await tx.CommitAsync(ct);
            return Results.Ok(new { mensaje = "Cita cancelada" });
        });
        // Pago con tarjeta de todas las citas -> cuenta única CITICIO Citas (sin Connect).
        clients.MapPost("/citas/{id:int}/checkout", async Task<IResult> (
            int id,ClaimsPrincipal user,StripeCheckoutService stripe) => {
            try {
                var link=await stripe.CrearCheckoutAsync(id,ClientId(user),AuthId(user),origenSpa:true);
                return Results.Ok(new { url=link });
            } catch(InvalidOperationException ex) {
                return Results.BadRequest(new { error=ex.Message });
            }
        }).RequireRateLimiting("citas");
        clients.MapGet("/citas/{id:int}/destino-spei", async Task<IResult> (
            int id, ClaimsPrincipal user, SqlStore store) => {
            var cuentas=await store.CallAsync("dbo.sp_citicio_sucursal_destino_cliente",SqlStore.P(
                ("id_usuario_cliente",AuthId(user)),("id_cita",id)));
            var cuenta=cuentas.FirstOrDefault();
            if(cuenta is null)return Results.NotFound(new {error="No hay una cuenta receptora asociada a esta cita."});
            var datos=(await store.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
                ("accion","checkout_cliente"),("id_usuario_actor",AuthId(user)),("id1",id)))).FirstOrDefault();
            if(datos is null)return Results.NotFound(new {error="Cita no encontrada."});
            var saldo=Math.Max(0,datos.M("total")-datos.M("pagado"));
            return Results.Ok(new { idSucursal=cuenta.I("id_sucursal"),
                banco=cuenta.S("nombre_banco"),titular=cuenta.S("titular"),
                clabe=cuenta.S("clabe"),saldo });
        });
        clients.MapPost("/citas/{id:int}/reportar-spei", async Task<IResult> (
            int id, ReporteSpeiClienteDto input, ClaimsPrincipal user, SqlStore store) => {
            var rastreo = (input.ClaveRastreo ?? string.Empty).Trim().ToUpperInvariant();
            if(input.IdSucursal<=0 || input.Monto<=0 || input.Monto>9999999 ||
                input.FechaOperacion.Date>MexicoClock.Today || input.FechaOperacion.Date<MexicoClock.Today.AddDays(-180) ||
                !System.Text.RegularExpressions.Regex.IsMatch(input.BancoEmisor??"",@"^[0-9]{3}$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(rastreo,@"^[A-Za-z0-9\-]{6,100}$"))
                return Results.BadRequest(new {error="Revisa fecha, sucursal, importe, banco y clave de rastreo."});
            try {
                await store.CallAsync("dbo.sp_spei_reportar_pago",SqlStore.P(
                    ("id_usuario_cliente",AuthId(user)),("id_cita",id),
                    ("id_cuenta_bancaria",input.IdSucursal),
                    ("fecha_operacion",input.FechaOperacion.Date),
                    ("codigo_banco_emisor",input.BancoEmisor),
                    ("clave_rastreo",rastreo),
                    ("monto",input.Monto),("nota_cliente",null)));
                return Results.Ok(new {mensaje="Transferencia reportada. La sucursal debe comprobar el depósito antes de confirmar el pago."});
            }catch(SqlException ex) when(ex.Number>=62001 && ex.Number<=62012) {
                return Results.BadRequest(new {error="No se pudo registrar el reporte. Verifica el saldo y que no exista otra transferencia pendiente."});
            }
        }).RequireRateLimiting("citas");
        clients.MapGet("/pagos", async Task<IResult> (ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) =>
            Results.Ok(await db.Sp("dbo.sp_panel_pagos_usuario", ct, P("@id_usuario", AuthId(user)))));
        clients.MapGet("/resenas", async Task<IResult> (ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) =>
            Results.Ok(await db.Sp("dbo.sp_panel_resenas_usuario", ct, P("@id_usuario", AuthId(user)))));
        clients.MapGet("/favoritos", async Task<IResult> (ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) =>
            Results.Ok(await db.Sp("dbo.sp_favoritos", ct, P("@accion", "listar_por_cliente"), P("@id_cliente", ClientId(user)))));
        clients.MapPost("/favoritos/{id:int}", async Task<IResult> (int id, ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) => {
            if (id <= 0) return Results.BadRequest();
            await db.Sp("dbo.sp_favoritos", ct, P("@accion", "agregar"), P("@id_cliente", ClientId(user)), P("@id_servicio", id));
            return Results.Ok(new { mensaje = "Añadido a favoritos" });
        });
        clients.MapDelete("/favoritos/{id:int}", async Task<IResult> (int id, ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) => {
            await db.Sp("dbo.sp_favoritos", ct, P("@accion", "quitar"), P("@id_cliente", ClientId(user)), P("@id_servicio", id));
            return Results.Ok(new { mensaje = "Eliminado de favoritos" });
        });
        clients.MapGet("/perfil", async Task<IResult> (ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) => {
            var rows = await db.Sp("dbo.sp_clientes", ct, P("@accion", "obtener"), P("@id_cliente", ClientId(user)));
            return rows.Count == 0 ? (IResult)Results.NotFound() : Results.Ok(rows[0]);
        });
        clients.MapPut("/perfil", async Task<IResult> (PerfilDto input, ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) => {
            if (!ValidPerson(input.Nombre, 100) || !ValidPerson(input.ApellidoPaterno, 100) || !ValidPerson(input.ApellidoMaterno, 100)
                || !ValidPhone(input.Telefono) || (input.AceptaWhatsapp && !ValidWhatsApp(input.TelefonoWhatsapp)))
                return Results.BadRequest(new { error = "Datos de perfil inválidos." });
            var current = await db.Sp("dbo.sp_clientes", ct, P("@accion", "obtener"), P("@id_cliente", ClientId(user)));
            if (current.Count == 0) return Results.NotFound();
            await db.Sp("dbo.sp_clientes", ct,
                P("@accion", "actualizar"), P("@id_cliente", ClientId(user)), P("@nombre", input.Nombre.Trim()),
                P("@apellido_paterno", input.ApellidoPaterno.Trim()), P("@apellido_materno", input.ApellidoMaterno.Trim()),
                P("@telefono", input.Telefono.Trim()), P("@correo", Val.Str(current[0], "correo")),
                P("@fecha_nacimiento", Val.Date(current[0], "fecha_nacimiento")),
                P("@telefono_whatsapp", input.AceptaWhatsapp ? input.TelefonoWhatsapp?.Trim() : null),
                P("@acepta_whatsapp", input.AceptaWhatsapp));
            return Results.Ok(new { mensaje = "Perfil actualizado" });
        });
        clients.MapPost("/resenas", async Task<IResult> (NuevaResenaDto input, ClaimsPrincipal user, ClienteSqlStore db, CancellationToken ct) => {
            if (input.Cita <= 0 || input.Calificacion < 1 || input.Calificacion > 5 || input.Comentario?.Length > 1000)
                return Results.BadRequest(new { error = "Calificación de 1 a 5 y comentario máximo de 1000 caracteres." });
            var owner = await db.Sp("dbo.sp_citicio_lecturas", ct,
                P("@accion", "cita_completada_cliente"), P("@id_usuario_actor", AuthId(user)), P("@id1", input.Cita));
            if (owner.Count == 0) return Results.Forbid();
            await db.Sp("dbo.sp_resenas", ct, P("@accion", "insertar"), P("@id_cita", input.Cita),
                P("@calificacion_empresa", input.Calificacion), P("@calificacion_sucursal", input.Calificacion),
                P("@comentario", EmptyAsNull(input.Comentario, 1000)));
            return Results.Ok(new { mensaje = "Gracias por tu reseña" });
        });


        return app;
    }
}

public sealed record ReporteSpeiClienteDto(int IdSucursal, DateTime FechaOperacion,
    string BancoEmisor, string ClaveRastreo, decimal Monto);
