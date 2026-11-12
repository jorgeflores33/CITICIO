using System.ComponentModel.DataAnnotations;

namespace Citicio.Models;

public sealed class EmpresaEmpleadoEditarForm
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    [Required, StringLength(100)] public string Paterno { get; set; } = "";
    [StringLength(100)] public string? Materno { get; set; }
    [Required, EmailAddress, StringLength(254)] public string Correo { get; set; } = "";
    [Required, StringLength(20)] public string Telefono { get; set; } = "";
    public int? SucursalId { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class EmpresaSucursalEditarForm
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required, StringLength(150)] public string Nombre { get; set; } = "";
    [Range(1, int.MaxValue)] public int ColoniaId { get; set; }
    [Required, StringLength(200)] public string Calle { get; set; } = "";
    [Required, StringLength(20)] public string NumeroExterior { get; set; } = "";
    [StringLength(20)] public string? NumeroInterior { get; set; }
    [StringLength(30)] public string? Telefono { get; set; }
    [StringLength(150)] public string? EntreCalle1 { get; set; }
    [StringLength(150)] public string? EntreCalle2 { get; set; }
    public bool Activa { get; set; } = true;
}

public sealed class EmpresaServiciosEmpleadoForm
{
    [Range(1, int.MaxValue)] public int EmpleadoId { get; set; }
    public List<int> Servicios { get; set; } = [];
}
