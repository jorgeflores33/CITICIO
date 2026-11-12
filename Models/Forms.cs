using System.ComponentModel.DataAnnotations;
using Citicio.Data;
using Citicio.Services;

namespace Citicio.Models;
public sealed class LoginForm {
    [Required,EmailAddress] public string Correo {get;set;}="";
    [Required] public string Contrasena {get;set;}="";
}
public sealed class RegistroForm {
    [Required,StringLength(100)] public string Nombre {get;set;}="";
    [Required,StringLength(100)] public string Paterno {get;set;}="";
    [Required,StringLength(100)] public string Materno {get;set;}="";
    [Required,Phone,StringLength(15)] public string Telefono {get;set;}="";
    [Required,EmailAddress,StringLength(254)] public string Correo {get;set;}="";
    [Required,DataType(DataType.Date)] public DateTime FechaNacimiento {get;set;}=DateTime.Today.AddYears(-20);
    [StringLength(16)] public string? Whatsapp {get;set;}
    [RegularExpression(@"^\+[1-9][0-9]{0,3}$")] public string LadaPais {get;set;}="+52";
    public bool AceptaWhatsapp {get;set;}
    [Required,ClaveSegura,DataType(DataType.Password)] public string Contrasena {get;set;}="";
    [Compare(nameof(Contrasena)),DataType(DataType.Password)] public string Confirmar {get;set;}="";
}
public sealed class RegistroEmpresaForm {
    [Required,StringLength(150)] public string NombreEmpresa {get;set;}="";
    [StringLength(1000)] public string? Descripcion {get;set;}
    public IFormFile? LogoArchivo {get;set;}
    public IFormFile? PortadaArchivo {get;set;}
    public int? GiroId {get;set;}
    [Required,StringLength(100)] public string Nombre {get;set;}="";
    [Required,StringLength(100)] public string Paterno {get;set;}="";
    [StringLength(100)] public string? Materno {get;set;}
    [Required,RegularExpression(@"^[0-9]{10,15}$",ErrorMessage="Usa entre 10 y 15 números.")] public string Telefono {get;set;}="";
    [Required,EmailAddress,StringLength(254)] public string Correo {get;set;}="";
    [Range(1,int.MaxValue,ErrorMessage="Selecciona una colonia del catálogo nacional.")] public int ColoniaId {get;set;}
    [Required,StringLength(200)] public string Calle {get;set;}="";
    [Required,StringLength(20)] public string NumeroExterior {get;set;}="";
    [StringLength(20)] public string? NumeroInterior {get;set;}
    [Required,ClaveSegura,DataType(DataType.Password)] public string Contrasena {get;set;}="";
    [Required,Compare(nameof(Contrasena)),DataType(DataType.Password)] public string Confirmar {get;set;}="";
    public bool AceptaTerminos {get;set;}
}
public sealed class ReservaForm {
    [Range(1,int.MaxValue)] public int SucursalId {get;set;}
    [Range(1,int.MaxValue)] public int ServicioId {get;set;}
    [Range(1,int.MaxValue)] public int EmpleadoId {get;set;}
    [Required,DataType(DataType.Date)] public DateTime Fecha {get;set;}=DateTime.Today.AddDays(1);
    [Required] public TimeSpan Hora {get;set;}=new(10,0,0);
    [StringLength(1000)] public string? Observaciones {get;set;}
}
public sealed class SucursalForm {
    public int EmpresaId {get;set;}
    [Required,StringLength(150)] public string Nombre {get;set;}="";
    [Range(1,int.MaxValue)] public int ColoniaId {get;set;}
    [Required,StringLength(200)] public string Calle {get;set;}="";
    [Required,StringLength(20)] public string NumeroExterior {get;set;}="";
    [StringLength(20)] public string? NumeroInterior {get;set;}
    [StringLength(30)] public string? TelefonoSucursal {get;set;}
    [StringLength(150)] public string? EntreCalle1 {get;set;}
    [StringLength(150)] public string? EntreCalle2 {get;set;}
    public IFormFile? ImagenArchivo {get;set;}
    [Required,StringLength(100)] public string BancoSucursal {get;set;}="";
    [Required,StringLength(200)] public string TitularSucursal {get;set;}="";
    [Required,RegularExpression("^[0-9]{18}$",ErrorMessage="CLABE de 18 dígitos obligatoria.")]
    public string ClabeSucursal {get;set;}="";
}
public sealed class ServicioForm {
    public int Id {get;set;}
    public int EmpresaId {get;set;}
    [Required,StringLength(150)] public string Nombre {get;set;}="";
    [StringLength(500)] public string? Descripcion {get;set;}
    [Range(typeof(decimal),"0","10000000")] public decimal Precio {get;set;}
    public IFormFile? ImagenArchivo {get;set;}
}
public sealed class EmpleadoForm {
    public int EmpresaId {get;set;}
    [Required,StringLength(100)] public string Nombre {get;set;}="";
    [Required,StringLength(100)] public string Paterno {get;set;}="";
    public string? Materno {get;set;}
    [Required,Phone] public string Telefono {get;set;}="";
    [Required,EmailAddress] public string Correo {get;set;}="";
    public int? SucursalId {get;set;}
    [Required] public string Rol {get;set;}="empleado";
    [Required,ClaveSegura] public string ContrasenaTemporal {get;set;}="";
}
public sealed class AsociarForm {
    [Range(1,int.MaxValue)] public int SucursalId {get;set;}
    [Range(1,int.MaxValue)] public int ServicioId {get;set;}
    [Range(1,720)] public int Duracion {get;set;}=30;
}
public sealed class ReseñaForm {
    public int CitaId {get;set;}
    [Range(1,5)] public int CalificacionEmpresa {get;set;}=5;
    [Range(1,5)] public int CalificacionSucursal {get;set;}=5;
    [StringLength(1000)] public string? Comentario {get;set;}
}
public sealed class HomeVM {
    public List<DbRow> Catalogo {get;set;}=[];
    public List<DbRow> Top {get;set;}=[];
    public List<DbRow> Giros {get;set;}=[];
    public string? Busqueda {get;set;}
    public int? GiroId {get;set;}
}
public sealed class PanelVM {
    public string Titulo {get;set;}="";
    public string Subtitulo {get;set;}="";
    public List<DbRow> Rows {get;set;}=[];
    public string[] Columns {get;set;}=[];
    public Dictionary<string,string> Captions {get;set;}=new();
}

public sealed class HorarioSucursalForm {
    [Range(1,int.MaxValue)] public int SucursalId {get;set;}
    [Range(1,7)] public int Dia {get;set;}=1;
    [Required] public TimeSpan Apertura {get;set;}=new(9,0,0);
    [Required] public TimeSpan Cierre {get;set;}=new(18,0,0);
}
public sealed class HorarioEmpleadoForm {
    [Range(1,int.MaxValue)] public int EmpleadoId {get;set;}
    [Range(1,int.MaxValue)] public int SucursalId {get;set;}
    [Range(1,7)] public int Dia {get;set;}=1;
    [Required] public TimeSpan Entrada {get;set;}=new(9,0,0);
    [Required] public TimeSpan Salida {get;set;}=new(18,0,0);
}
public sealed class EmpleadoServicioForm {
    [Range(1,int.MaxValue)] public int EmpleadoId {get;set;}
    [Range(1,int.MaxValue)] public int ServicioId {get;set;}
}


public sealed class CobroEmpresaForm {
    [Range(1,int.MaxValue)] public int EmpresaId {get;set;}
    [Range(typeof(decimal),"0.01","10000000")] public decimal Monto {get;set;}
    [Required] public DateTime Inicio {get;set;}=new(DateTime.Today.Year,DateTime.Today.Month,1);
    [Required] public DateTime Fin {get;set;}=DateTime.Today;
    [Required] public DateTime Vencimiento {get;set;}=DateTime.Today.AddDays(15);
}

public sealed class GiroEmpresaForm {
    [Range(1,int.MaxValue)] public int EmpresaId {get;set;}
    [Range(1,int.MaxValue)] public int GiroId {get;set;}
}


// Alta y edicion del catalogo global de giros (solo admin_citicio).
public sealed class GiroCatalogoForm
{
    public int IdGiro { get; set; }
    [Required(ErrorMessage = "Escribe el nombre del giro.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres.")]
    public string NombreGiro { get; set; } = "";
    [StringLength(500, ErrorMessage = "La descripción no puede superar 500 caracteres.")]
    public string? Descripcion { get; set; }
}
