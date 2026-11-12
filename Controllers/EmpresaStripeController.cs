using Citicio.Auth;
using Citicio.Services;
using Citicio.Data;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citicio.Controllers;

[ApiController, Route("api/empresa/stripe"), Authorize(AuthenticationSchemes="Citicio.Empresa")]
public sealed class EmpresaStripeController(CurrentUserService current, StripeConnectService stripe,
    SqlStore db, IAntiforgery antiforgery, ILogger<EmpresaStripeController> log) : ControllerBase
{
    public sealed record CuentaSucursalInput(string NombreBanco,string Titular,string Clabe);

    [HttpPost("guardar-cuenta/{id:int}")]
    public async Task<IActionResult> GuardarCuenta(int id,[FromBody] CuentaSucursalInput form)
    {
        if(!await antiforgery.IsRequestValidAsync(HttpContext))return BadRequest(new { error="Token de seguridad vencido." });
        var usuario=await current.GetAsync(User);
        if(usuario is null || usuario.Rol!=EmpresaRoles.AdminEmpresa)return Forbid();
        if(id<=0 || string.IsNullOrWhiteSpace(form.NombreBanco) || form.NombreBanco.Length>100 ||
           string.IsNullOrWhiteSpace(form.Titular) || form.Titular.Length>200 ||
           !SpeiController.ClabeValida(form.Clabe))
           return BadRequest(new { error="Banco, titular o CLABE de 18 dígitos inválidos." });
        try {
            await db.CallAsync("dbo.sp_citicio_cuenta_sucursal_guardar",SqlStore.P(
                ("id_usuario_actor",usuario.Id),("id_sucursal",id),
                ("nombre_banco",form.NombreBanco.Trim()),("titular",form.Titular.Trim()),
                ("clabe",form.Clabe)));
            return Ok(new { ok=true,mensaje="Cuenta SPEI registrada. Si ya usas Stripe Connect, actualiza por separado la cuenta de depósitos en Stripe; guardar esta CLABE no cambia los retiros de la pasarela." });
        }catch(SqlException ex){log.LogWarning("No se guardó cuenta sucursal SQL {Numero}",ex.Number);
           return BadRequest(new { error="No se pudo guardar la cuenta. Comprueba la sucursal y la CLABE." });}
    }

    [HttpPost("conectar/{id:int}")]
    public async Task<IActionResult> Conectar(int id)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext)) return BadRequest(new { error="Token de seguridad vencido." });
        var usuario=await current.GetAsync(User);
        if(usuario is null || usuario.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        try {return Ok(new { url=await stripe.CrearOnboardingAsync(usuario.Id,id,true) });}
        catch(Exception ex) {log.LogWarning("Stripe Connect: {Tipo}",ex.GetType().Name);
           return BadRequest(new { error="No fue posible vincular Stripe; verifica que la sucursal tenga CLABE y que Stripe esté configurado." });}
    }
    [HttpGet("regresar/{id:int}")]
    public async Task<IActionResult> Regresar(int id)
    {
        var usuario=await current.GetAsync(User);
        if(usuario is null || usuario.Rol != EmpresaRoles.AdminEmpresa) return Redirect("/empresa/index.html");
        try {await stripe.VerificarAsync(usuario.Id,id);}
        catch(Exception ex){log.LogWarning("Stripe: comprobación no completada: {Tipo}",ex.GetType().Name);}
        return Redirect("/empresa/index.html");
    }
}
