using Citicio.Auth;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citicio.Controllers;

[Authorize(AuthenticationSchemes = "Citicio.Empresa")]
[Route("empresa/panel")]
public sealed class EmpresaViewsController(CurrentUserService current) : Controller
{
    [HttpGet("")]
    [HttpGet("{rol}")]
    public async Task<IActionResult> Panel(string? rol = null)
    {
        var user = await current.GetAsync(User);
        if (user is null) return Forbid();
        // Un usuario NO puede abrir por URL la vista de otro rol.
        if (!string.IsNullOrWhiteSpace(rol) && !string.Equals(rol, user.Rol, StringComparison.Ordinal))
            return Forbid();
        var title = user.Rol switch
        {
            EmpresaRoles.AdminEmpresa => "Administración de empresa",
            EmpresaRoles.EncargadoSucursal => "Encargado de sucursal",
            _ => "Panel del empleado"
        };
        return View("~/Views/Empresa/Panel.cshtml", new EmpresaPanelViewModel(title, user.Rol, user.Nombre, user.Empresa, user.Sucursal));
    }
}
