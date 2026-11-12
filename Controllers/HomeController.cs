using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using Citicio.Services;

namespace Citicio.Controllers;

// La portada abre UNA conexion por solicitud y reutiliza esa conexion
// para las lecturas. No inicia tres sesiones SQL simultaneas.
// La gestion de sesiones, cobros, citas y roles no se modifica.
public sealed class HomeController(SqlStore db, ILogger<HomeController> log) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? buscar = null, int? giro = null)
    {
        var vm = new HomeVM { Busqueda = buscar, GiroId = giro };
        await CargarAsync(vm, cargarCatalogo: true, cargarTop: true,
            cargarGiros: true, ct: HttpContext.RequestAborted);
        return View(vm);
    }

    // Una sola página de empresas y servicios: sin ranking inferior ni datos de ejemplo.
    // Los filtros operan sobre el catálogo publicado por SQL Server.
    [HttpGet]
    public async Task<IActionResult> Empresas(
        string? buscar = null, string? categoria = null, string? ciudad = null,
        int? sucursal = null, int? precioMax = null, int? calificacionMin = null,
        string? orden = null, int? giro = null)
    {
        if (buscar?.Length > 150 || categoria?.Length > 80 || ciudad?.Length > 120 ||
            (sucursal is < 1) || (giro is < 1) ||
            (precioMax is < 0 or > 1000000) || (calificacionMin is < 1 or > 5))
            return BadRequest("Alguno de los filtros del catálogo no es válido.");

        var vm = new EmpresasServiciosVM {
            Buscar = buscar?.Trim(), Categoria = categoria?.Trim(), Ciudad = ciudad?.Trim(),
            Sucursal = sucursal, PrecioMax = precioMax, CalificacionMin = calificacionMin,
            Orden = orden is "precio" or "calificacion" or "nombre" ? orden : "relevancia"
        };
        try
        {
            await using var cn = await db.OpenAsync(HttpContext.RequestAborted);
            var publicados = await SqlStore.ReadAsync(cn,null,"dbo.sp_citicio_catalogo_publicado",
                SqlStore.P(("texto",null),("id_giro",giro)),HttpContext.RequestAborted);

            // Opciones de filtro basadas en empresas y sucursales que realmente publican servicios.
            vm.Ciudades = publicados.Select(x=>x.S("municipio")).Where(x=>!string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();
            vm.Sucursales = publicados.Where(x=>x.I("id_sucursal")>0)
                .GroupBy(x=>x.I("id_sucursal"))
                .Select(g=>new SucursalFiltroVM {Id=g.Key, Nombre=g.First().S("sucursal"),Empresa=g.First().S("empresa")})
                .OrderBy(x=>x.Empresa).ThenBy(x=>x.Nombre).ToList();

            var filtrados = publicados.Where(x =>
                BusquedaFlexible.Coincide(vm.Buscar,x.S("empresa"),x.S("servicio"),x.S("sucursal"),x.S("descripcion"),x.S("giro")) &&
                BusquedaFlexible.Coincide(vm.Categoria,x.S("giro"),x.S("servicio"),x.S("empresa"),x.S("descripcion")) &&
                (string.IsNullOrWhiteSpace(vm.Ciudad) || BusquedaFlexible.Normalizar(x.S("municipio")) == BusquedaFlexible.Normalizar(vm.Ciudad)) &&
                (vm.Sucursal is null || x.I("id_sucursal") == vm.Sucursal) &&
                (vm.PrecioMax is null || x.M("precio") <= vm.PrecioMax.Value))
                .Where(x=>x.I("id_empresa")>0 && x.I("id_servicio")>0 && x.I("id_sucursal")>0)
                .ToList();

            // Reseñas verificadas y textos reales de la empresa, con consultas parametrizadas.
            // Si esta lectura auxiliar falla, se muestra el catálogo sin inventar calificaciones.
            var metadatos = new Dictionary<int,(string Descripcion,decimal? Promedio,int Resenas)>();
            if (filtrados.Count > 0)
            {
                try
                {
                    var ids=filtrados.Select(x=>x.I("id_empresa")).Distinct().Take(200).ToArray();
                    var pares=ids.Select((id,i)=>( $"empresa{i}",(object?)id )).ToArray();
                    var nombres=string.Join(",",Enumerable.Range(0,ids.Length).Select(i=>$"@empresa{i}"));
                    var detalle = await SqlStore.ReadTextAsync(cn,null,$@"
                        SELECT e.id_empresa, e.descripcion,
                            (SELECT COUNT_BIG(1)
                               FROM dbo.resenas r
                               JOIN dbo.citas ci ON ci.id_cita=r.id_cita
                               JOIN dbo.sucursales s ON s.id_sucursal=ci.id_sucursal
                               JOIN dbo.estados est ON est.id_estado=ci.id_estado
                              WHERE s.id_empresa=e.id_empresa AND est.nombre_estado='cita_completada') AS resenas,
                            (SELECT AVG(CONVERT(decimal(9,2),r.calificacion_empresa))
                               FROM dbo.resenas r
                               JOIN dbo.citas ci ON ci.id_cita=r.id_cita
                               JOIN dbo.sucursales s ON s.id_sucursal=ci.id_sucursal
                               JOIN dbo.estados est ON est.id_estado=ci.id_estado
                              WHERE s.id_empresa=e.id_empresa AND est.nombre_estado='cita_completada') AS promedio
                        FROM dbo.empresas e WHERE e.id_empresa IN ({nombres});",
                        SqlStore.P(pares),HttpContext.RequestAborted);
                    foreach(var item in detalle)
                        metadatos[item.I("id_empresa")] =
                            (item.S("descripcion"),item.Cells.TryGetValue("promedio",out var prom) && prom is not null ? item.M("promedio") : null,
                             item.I("resenas"));
                }
                catch(SqlException ex) {log.LogWarning("No se pudieron cargar calificaciones del catálogo: SQL {Codigo}",ex.Number);}
            }

            var empresas = filtrados.GroupBy(x=>x.I("id_empresa")).Select(g=>
            {
                var first=g.First();
                metadatos.TryGetValue(g.Key,out var meta);
                return new EmpresaTarjetaVM {
                    Id=g.Key, Nombre=first.S("empresa"), Giro=first.S("giro"),
                    Ciudad=first.S("municipio"), Descripcion=meta.Descripcion ?? "",
                    TotalSucursales=publicados.Where(x=>x.I("id_empresa")==g.Key)
                        .Select(x=>x.I("id_sucursal")).Distinct().Count(),
                    Calificacion=meta.Promedio, TotalResenas=meta.Resenas,
                    PrecioDesde=g.Min(x=>x.M("precio")),
                    Servicios=g.GroupBy(x=>new {Id=x.I("id_servicio"),Sucursal=x.I("id_sucursal")})
                        .Select(x=>x.First()).OrderBy(x=>x.M("precio"))
                        .Select(x=>new ServicioTarjetaVM {Id=x.I("id_servicio"),SucursalId=x.I("id_sucursal"),
                            Nombre=x.S("servicio"),Sucursal=x.S("sucursal"),Precio=x.M("precio"),
                            DuracionMinutos=x.I("duracion_minutos")}).ToList()
                };
            });

            if(vm.CalificacionMin is > 0)
                empresas=empresas.Where(x=>x.Calificacion.HasValue && x.Calificacion.Value >= vm.CalificacionMin.Value);
            empresas = vm.Orden switch {
                "nombre"=>empresas.OrderBy(x=>x.Nombre),
                "precio"=>empresas.OrderBy(x=>x.PrecioDesde),
                "calificacion"=>empresas.OrderByDescending(x=>x.Calificacion ?? -1m).ThenByDescending(x=>x.TotalResenas),
                _=>empresas
            };
            vm.Empresas = empresas.Take(60).ToList();
            vm.TotalServicios = vm.Empresas.Sum(e=>e.Servicios.Count);
        }
        catch(SqlException ex)
        {
            log.LogError("No se pudo cargar el catálogo de empresas, SQL {Numero}",ex.Number);
            vm.ErrorSql=true;
        }
        return View("Empresas",vm);
    }

    [HttpGet]
    public async Task<IActionResult> Top10()
    {
        var vm = new HomeVM();
        await CargarAsync(vm, cargarCatalogo: false, cargarTop: true,
            cargarGiros: false, ct: HttpContext.RequestAborted);
        return View(vm);
    }

    [HttpGet]
    public IActionResult Error() => View();

    private void MostrarAvisoSql() => ViewBag.Error =
        "No fue posible cargar toda la informacion del catalogo desde SQL Server. " +
        "Comprueba que el servidor y la base de datos esten disponibles.";

    private async Task CargarAsync(HomeVM vm, bool cargarCatalogo,
        bool cargarTop, bool cargarGiros, CancellationToken ct)
    {
        try
        {
            // Una sola apertura: si SQL no admite la sesion, no disparamos
            // otras dos conexiones que terminarian en el mismo timeout.
            await using var conexion = await db.OpenAsync(ct);

            if (cargarCatalogo)
            {
                // Consultar el catálogo PUBLICADO sin el texto para que la búsqueda
                // pueda reconocer sinónimos y errores de escritura antes de filtrar.
                var r = await ConsultarAsync(conexion, "dbo.sp_citicio_catalogo_publicado",
                    SqlStore.P(("texto", null), ("id_giro", vm.GiroId)), ct);
                vm.Catalogo = r.Rows.Where(x => BusquedaFlexible.Coincide(vm.Busqueda,
                    x.S("empresa"), x.S("servicio"), x.S("sucursal"), x.S("descripcion"), x.S("giro")))
                    .Take(100).ToList();
                if (!r.Ok) MostrarAvisoSql();
                if (!r.Continuar) return;
            }

            if (cargarTop)
            {
                try { vm.Top = await RankingEmpresasService.ObtenerAsync(conexion, ct); }
                catch (SqlException ex) {
                    log.LogWarning(ex, "No se pudo obtener el ranking de empresas; se utiliza el ranking de sucursales original.");
                    var r=await ConsultarAsync(conexion,"dbo.sp_citicio_top10_publicado",null,ct);
                    // Agrupación por nombre, conservando la mejor sucursal de cada empresa.
                    vm.Top=r.Rows.GroupBy(x=>x.I("id_empresa")>0 ? $"id:{x.I("id_empresa")}" : x.S("empresa"),StringComparer.OrdinalIgnoreCase)
                        .Select(g=>g.First()).Take(10).ToList();
                    if(!r.Ok) MostrarAvisoSql();
                }
            }

            if (cargarGiros)
            {
                var r = await ConsultarAsync(conexion, "dbo.sp_citicio_lecturas",
                    SqlStore.P(("accion", "giros_publicos")), ct);
                vm.Giros = r.Rows;
                if (!r.Ok) MostrarAvisoSql();
            }
        }
        catch (SqlException ex)
        {
            // El stack de OpenAsync significa fallo de CONEXION,
            // no prueba un defecto en los procedimientos almacenados.
            log.LogWarning("No se pudo establecer la conexion SQL para la portada. Codigo={Codigo}; Detalle={Detalle}",
                ex.Number, ex.Message);
            MostrarAvisoSql();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "No fue posible abrir la conexion SQL de la portada");
            MostrarAvisoSql();
        }
    }

    // Un procedimiento que falla por nombre/parametros no bloquea los demas.
    // Si caduca el tiempo de espera o se pierde la conexion, detenemos el lote.
    private async Task<(List<DbRow> Rows, bool Ok, bool Continuar)> ConsultarAsync(
        SqlConnection conexion, string procedimiento,
        Dictionary<string, object?>? parametros, CancellationToken ct)
    {
        try
        {
            var rows = await SqlStore.ReadAsync(conexion, null, procedimiento,
                parametros, ct);
            return (rows, true, true);
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            log.LogWarning("Tiempo de espera durante {Procedimiento}. " +
                "Comprueba carga, bloqueos y rendimiento de SQL Server.", procedimiento);
            return (new List<DbRow>(), false, false);
        }
        catch (SqlException ex)
        {
            log.LogWarning("SQL devolvio codigo {Codigo} al ejecutar {Procedimiento}: {Detalle}",
                ex.Number, procedimiento, ex.Message);
            return (new List<DbRow>(), false,
                conexion.State == ConnectionState.Open);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Error inesperado en {Procedimiento}", procedimiento);
            return (new List<DbRow>(), false, false);
        }
    }
}
