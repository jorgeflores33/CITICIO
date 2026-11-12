using System.ComponentModel.DataAnnotations;
using Citicio.Services;

namespace Citicio.Models;

public sealed class MiPerfilForm
{
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    [Required, StringLength(100)] public string Paterno { get; set; } = "";
    [StringLength(100)] public string? Materno { get; set; }
    [Required, RegularExpression(@"^[0-9]{10,15}$")] public string Telefono { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Correo { get; set; } = "";
    [Required] public string ContrasenaActual { get; set; } = "";
}
public sealed class HorarioDiaPanelForm
{
    public int IdDia { get; set; }
    public string Nombre { get; set; } = "";
    public bool Trabaja { get; set; }
    public string Inicio { get; set; } = "09:00";
    public string Fin { get; set; } = "18:00";
}
public sealed class HorarioPanelForm
{
    [Required] public string Tipo { get; set; } = "sucursal";
    public int IdSucursal { get; set; }
    public int? IdEmpleado { get; set; }
    public List<HorarioDiaPanelForm> Dias { get; set; } = [];
}
public sealed class RegistroEmpleadoSucursalForm
{
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    [Required, StringLength(100)] public string Paterno { get; set; } = "";
    [StringLength(100)] public string? Materno { get; set; }
    [Required, EmailAddress, StringLength(254)] public string Correo { get; set; } = "";
    [Required, RegularExpression(@"^[0-9]{10,15}$")] public string Telefono { get; set; } = "";
    [Required, ClaveSegura] public string Contrasena { get; set; } = "";
    public List<int> Servicios { get; set; } = [];
}
public sealed class MiSucursalForm
{
    [Required, StringLength(150)] public string Nombre { get; set; } = "";
    [Required, StringLength(200)] public string Calle { get; set; } = "";
    [Required, StringLength(20)] public string NumeroExterior { get; set; } = "";
    [StringLength(30)] public string? Telefono { get; set; }
    [StringLength(150)] public string? EntreCalle1 { get; set; }
    [StringLength(150)] public string? EntreCalle2 { get; set; }
}
public sealed class DuracionServicioForm
{
    [Range(1,int.MaxValue)] public int IdSucursal { get; set; }
    [Range(1,int.MaxValue)] public int IdServicio { get; set; }
    [Range(5,720)] public int Minutos { get; set; } = 30;
}

// Datos editables por un encargado: la sucursal y el rol se derivan de la sesión.
public sealed class EmpleadoSucursalEditarForm
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    [Required, StringLength(100)] public string Paterno { get; set; } = "";
    [StringLength(100)] public string? Materno { get; set; }
    [Required, EmailAddress, StringLength(254)] public string Correo { get; set; } = "";
    [Required, StringLength(20)] public string Telefono { get; set; } = "";
    public bool Activo { get; set; } = true;
}
