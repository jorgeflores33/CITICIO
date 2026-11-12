using System.Data;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

/// <summary>Cobros por uso de la plataforma; jamás marca un cobro pagado desde el panel de empresa.</summary>
[Authorize]
[ApiController]
[Route("api/empresa/cobros")]
public sealed class EmpresaCobrosController(CurrentUserService current, CiticioDb db,
    IAntiforgery antiforgery, Citicio.Services.StripeCheckoutService stripe) : ControllerBase
{
    private async Task<(string Banco,string Titular,string Clabe)?> CuentaReceptoraAsync()
    {
        var fila = await db.FirstAsync(@"SELECT banco_personal,titular_personal,clabe_personal
              FROM dbo.citicio_configuracion_suscripcion WHERE id_configuracion=1");
        if (fila is null) return null;
        var banco = Convert.ToString(fila.GetValueOrDefault("banco_personal")) ?? "";
        var titular = Convert.ToString(fila.GetValueOrDefault("titular_personal")) ?? "";
        var clabe = Convert.ToString(fila.GetValueOrDefault("clabe_personal")) ?? "";
        return !string.IsNullOrWhiteSpace(banco) && !string.IsNullOrWhiteSpace(titular) &&
               System.Text.RegularExpressions.Regex.IsMatch(clabe, @"\A[0-9]{18}\z")
            ? (banco,titular,clabe) : null;
    }

    [HttpGet("metodos")]
    public async Task<IActionResult> Metodos()
    {
        var user = await current.GetAsync(User);
        if (user is null || user.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        // Estado de cada solicitud pertenece a la empresa de la sesión, no a IDs del navegador.
        List<Dictionary<string,object?>> solicitudes;
        try
        {
            solicitudes = await db.QueryAsync(@"
              SELECT s.id_cobro_empresa,s.estado,s.fecha_solicitud_utc,s.fecha_revision_utc
              FROM dbo.solicitudes_pago_citicio s
              JOIN dbo.cobros_empresas c ON c.id_cobro_empresa=s.id_cobro_empresa
              WHERE c.id_empresa=@empresa", CommandType.Text,
              CiticioDb.Param("@empresa", user.IdEmpresa));
        }
        catch (SqlException ex) when (ex.Number == 208)
        { return StatusCode(503, new { error = "Falta la tabla solicitudes_pago_citicio en agencia_citas; verifica la instalación de la base completa." }); }
        var cuenta = await CuentaReceptoraAsync();
        return Ok(new
        {
            transferenciaHabilitada = cuenta.HasValue,
            banco = cuenta?.Banco,
            titular = cuenta?.Titular,
            clabe = cuenta?.Clabe,
            tarjetaHabilitada = stripe.Disponible,
            urlPago = "/EmpresaSuscripcion",
            solicitudes
        });
    }

    [HttpPost("transferencia")]
    public async Task<IActionResult> ReportarTransferencia([FromBody] SolicitudTransferenciaRequest input)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        var user = await current.GetAsync(User);
        if (user is null || user.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        if (!((await CuentaReceptoraAsync()).HasValue))
            return StatusCode(503, new { error = "CITICIO aún no configuró su cuenta bancaria de cobro. No realices una transferencia." });
        if (input.IdCobroEmpresa <= 0 || string.IsNullOrWhiteSpace(input.Referencia) || input.Referencia.Trim().Length is < 6 or > 120)
            return BadRequest(new { error = "Selecciona un cobro y captura la referencia de 6 a 120 caracteres." });
        try
        {
            var result = await db.FirstAsync("dbo.sp_citicio_declarar_transferencia", CommandType.StoredProcedure,
                CiticioDb.Param("@id_usuario_actor", user.Id),
                CiticioDb.Param("@id_cobro_empresa", input.IdCobroEmpresa),
                CiticioDb.Param("@referencia", input.Referencia.Trim()));
            return Ok(new { ok = true, resultado = result, mensaje = "Referencia enviada a revisión. El pago aún NO se considera confirmado." });
        }
        catch (SqlException ex) when (ex.Number >= 59201 && ex.Number <= 59204)
        { return BadRequest(new { error = ex.Message }); }
    }
}
