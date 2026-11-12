using Citicio.Auth;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citicio.Controllers;

[Authorize]
[ApiController]
[Route("api/empresa/datos")]
public sealed class EmpresaPanelController(CurrentUserService current, EmpresaPanelService panel) : ControllerBase
{
    [HttpGet("{seccion}")]
    public async Task<IActionResult> Obtener(string seccion)
    {
        var user = await current.GetAsync(User);
        if (user is null) return Forbid();
        if (!EmpresaRoles.PermiteSeccion(user.Rol, seccion))
        {
            if (seccion is "dashboard" or "citas" or "servicios" or "sucursales" or "empleados" or
                "pagos" or "resenas" or "cobros" or "horarios") return Forbid();
            return NotFound(new { error = "Sección inexistente." });
        }
        return Ok(await panel.ObtenerAsync(user, seccion));
    }
}
