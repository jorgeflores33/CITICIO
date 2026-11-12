using Citicio.Auth;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citicio.Controllers;
[Authorize(Roles="admin_empresa")]
public sealed class CuentaStripeController(StripeConnectService stripe,ILogger<CuentaStripeController> log):Controller
{
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Vincular(int id)
    {
        try{return Redirect(await stripe.CrearOnboardingAsync(User.UserId(),id));}
        catch(Exception ex){log.LogWarning("No se pudo vincular Stripe: {Tipo}",ex.GetType().Name);
            TempData["Error"]="No se pudo vincular Stripe; revisa la configuración o la cuenta bancaria de la sucursal.";
            return RedirectToAction("Cuentas","Spei");}
    }
    [HttpGet]
    public async Task<IActionResult> Regresar(int id)
    {
        try{
            var enabled=await stripe.VerificarAsync(User.UserId(),id);
            TempData[enabled?"Ok":"Error"]=enabled
                ? "Stripe verificó que la sucursal puede aceptar pagos con tarjeta."
                : "La cuenta sigue pendiente de verificación. Revisa los requisitos en Stripe.";
        }catch(Exception ex){log.LogWarning("No se verificó Stripe: {Tipo}",ex.GetType().Name);
            TempData["Error"]="No fue posible consultar el estado de Stripe.";}
        return RedirectToAction("Cuentas","Spei");
    }
}
