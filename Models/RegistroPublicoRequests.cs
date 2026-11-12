using Microsoft.AspNetCore.Http;

namespace Citicio.Models;

// Los nombres de propiedades son los de los campos del formulario original.
public sealed class RegistroEmpresaPublicoRequest
{
    public string? NombreEmpresa { get; set; }
    public int? IdGiro { get; set; }
    public string? Descripcion { get; set; }
    public IFormFile? Logo { get; set; } // Contenido binario, NO URL.
    public int? IdEstado { get; set; }
    public int? IdMunicipio { get; set; }
    public int? IdColonia { get; set; }
    public string? CodigoPostal { get; set; }
    public string? Calle { get; set; }
    public string? NumeroExterior { get; set; }
    public string? NumeroInterior { get; set; }
    public string? NombreDueno { get; set; }
    public string? ApellidoPaterno { get; set; }
    public string? ApellidoMaterno { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Contrasena { get; set; }
    public string? ConfirmarContrasena { get; set; }
    public bool AceptaTerminos { get; set; }
}

public sealed class RegistroClientePublicoRequest
{
    public bool AceptaWhatsapp { get; set; }
    public string? TelefonoWhatsapp { get; set; }
    public string? Nombre { get; set; }
    public string? ApellidoPaterno { get; set; }
    public string? ApellidoMaterno { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    public string? Contrasena { get; set; }
}

public sealed class PersonalNuevoRequest
{
    public List<int> IdServicios { get; set; } = new();
    public int IdSucursal { get; set; }
    public string? Rol { get; set; }
    public string? Nombre { get; set; }
    public string? ApellidoPaterno { get; set; }
    public string? ApellidoMaterno { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? NombreUsuario { get; set; }
    public string? Contrasena { get; set; }
}
