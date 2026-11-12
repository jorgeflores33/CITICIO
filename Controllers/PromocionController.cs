using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Citicio.Controllers;
[Authorize]
public sealed class PromocionController:Controller {
 [HttpGet] public IActionResult Index()=>RedirectToAction("Index","Suscripcion");
 [HttpGet] public IActionResult Configuracion()=>RedirectToAction("Configuracion","Suscripcion");
 [HttpGet] public IActionResult Resultado()=>RedirectToAction("Index","Suscripcion");
}
