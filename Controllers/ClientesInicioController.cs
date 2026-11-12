using Microsoft.AspNetCore.Mvc;
namespace Citicio.Controllers;
public sealed class ClientesInicioController : Controller
{
    [HttpGet("/clientes")]
    public IActionResult Index() => View("~/Views/Clientes/Index.cshtml");
}
