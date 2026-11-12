namespace Citicio.Models;

/// <summary>Catálogo público de negocios y servicios realmente publicados en CITICIO.</summary>
public sealed class EmpresasServiciosVM
{
    public string? Buscar { get; set; }
    public string? Categoria { get; set; }
    public string? Ciudad { get; set; }
    public int? Sucursal { get; set; }
    public int? PrecioMax { get; set; }
    public int? CalificacionMin { get; set; }
    public string Orden { get; set; } = "relevancia";
    public bool ErrorSql { get; set; }
    public int TotalServicios { get; set; }
    public List<EmpresaTarjetaVM> Empresas { get; set; } = [];
    public List<string> Ciudades { get; set; } = [];
    public List<SucursalFiltroVM> Sucursales { get; set; } = [];
}

public sealed class SucursalFiltroVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Empresa { get; set; } = "";
}

public sealed class EmpresaTarjetaVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Giro { get; set; } = "";
    public string Ciudad { get; set; } = "";
    public int TotalSucursales { get; set; }
    public decimal? Calificacion { get; set; }
    public int TotalResenas { get; set; }
    public decimal PrecioDesde { get; set; }
    public List<ServicioTarjetaVM> Servicios { get; set; } = [];
}

public sealed class ServicioTarjetaVM
{
    public int Id { get; set; }
    public int SucursalId { get; set; }
    public string Nombre { get; set; } = "";
    public string Sucursal { get; set; } = "";
    public decimal Precio { get; set; }
    public int DuracionMinutos { get; set; }
}
