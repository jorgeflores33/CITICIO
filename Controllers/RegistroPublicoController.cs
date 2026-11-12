using System.Text.RegularExpressions;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/publico/registro")]
public sealed class RegistroPublicoController(
    RegistroPublicoService service, IAntiforgery antiforgery,
    ILogger<RegistroPublicoController> logger) : ControllerBase
{
    private static bool Filled(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= max;
    private static bool EmailOk(string? text) => Filled(text, 254) &&
        System.Net.Mail.MailAddress.TryCreate(text, out var address) && address.Address == text;
    private static bool TelOk(string? text) => text is not null && Regex.IsMatch(text, @"\A[0-9]{10,15}\z");
    private static bool PasswordOk(string? text) => PasswordPolicy.Valida(text);

    [HttpPost("empresa")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    public async Task<IActionResult> Empresa([FromForm] RegistroEmpresaPublicoRequest input)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { error = "Sesión de formulario vencida; vuelve a cargar la página." });
        if (!Filled(input.NombreEmpresa, 150) || input.NombreEmpresa!.Trim().Length < 3 ||
            !Filled(input.NombreDueno, 100) || !Filled(input.ApellidoPaterno, 100) ||
            !TelOk(input.Telefono) || !EmailOk(input.Correo) || !PasswordOk(input.Contrasena) ||
            input.Contrasena != input.ConfirmarContrasena || !input.AceptaTerminos ||
            input.Descripcion?.Length > 1000 || input.ApellidoMaterno?.Length > 100 ||
            input.IdGiro is < 1 || input.IdEstado is null or <= 0 || input.IdMunicipio is null or <= 0 ||
            input.IdColonia is null or <= 0 || !Filled(input.Calle, 200) || !Filled(input.NumeroExterior, 20) ||
            input.NumeroInterior?.Length > 20 ||
            (!string.IsNullOrWhiteSpace(input.CodigoPostal) && !Regex.IsMatch(input.CodigoPostal, @"\A[0-9]{5}\z")))
            return BadRequest(new { error = "Revisa campos obligatorios, domicilio, contraseña, confirmación y términos." });
        try
        {
            var logo = await ImagenEmpresaService.LeerLogotipoAsync(input.Logo);
            var result = await service.RegistrarEmpresaAsync(input, logo.Bytes, logo.Mime);
            return Ok(new { ok = true, estado = "empresa_activa", rol = "admin_empresa",
                mensaje = "Empresa registrada. Puedes iniciar sesión como administrador de empresa. El acceso al sistema requiere la primera mensualidad pagada.", registro = result });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Fallo de registro empresa SQL {Numero}", ex.Number);
            return ex.Number switch
            {
                2601 or 2627 => Conflict(new { error = "Ya existe una empresa, correo, teléfono o usuario con estos datos." }),
                59004 => BadRequest(new { error = "La colonia no pertenece al estado, municipio o código postal elegido." }),
                59006 => BadRequest(new { error = "El logotipo es inválido o supera los 2 MB." }),
                2812 or 208 => StatusCode(503, new { error = "Falta ejecutar SQL/03_CITICIO_REGISTRO_WEB_IMAGENES_Y_ROLES.sql en agencia_citas." }),
                _ => StatusCode(503, new { error = "SQL Server rechazó el registro. Verifica conexión, datos y que los tres scripts estén instalados." })
            };
        }
    }

    [HttpPost("cliente")]
    public async Task<IActionResult> Cliente([FromBody] RegistroClientePublicoRequest input)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { error = "Sesión vencida; recarga la página." });
        if (!Filled(input.Nombre, 100) || !Filled(input.ApellidoPaterno, 100) ||
            !Filled(input.ApellidoMaterno, 100) || !TelOk(input.Telefono) || !EmailOk(input.Correo) ||
            !PasswordOk(input.Contrasena) ||
            (input.AceptaWhatsapp && (string.IsNullOrWhiteSpace(input.TelefonoWhatsapp) ||
             !Regex.IsMatch(input.TelefonoWhatsapp,@"^\+[1-9][0-9]{8,14}$"))) || input.FechaNacimiento is null ||
            input.FechaNacimiento.Value.Date > DateTime.UtcNow.Date || input.FechaNacimiento.Value.Year < 1900)
            return BadRequest(new { error = "Revisa los datos del cliente." });
        try
        {
            var id = await service.RegistrarClienteAsync(input);
            return Ok(new { ok = true, id_cliente = id, mensaje = "Cuenta de cliente registrada correctamente." });
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Fallo de registro cliente");
            return ex.Number is 2601 or 2627 ? Conflict(new { error = "El teléfono o correo ya está registrado." }) :
                StatusCode(503, new { error = "No fue posible guardar el cliente. Revisa SQL Server." });
        }
    }
}
