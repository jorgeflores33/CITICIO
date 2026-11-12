using System.Security.Claims;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;
public sealed class CuentaService(SqlStore db, IHostEnvironment entorno) {
    private readonly PasswordHasher<string> _hasher=new();
    public async Task<ClaimsPrincipal?> LoginAsync(string correo,string password) {
        var user=(await db.CallAsync("dbo.sp_usuarios",SqlStore.P(("accion","obtener_correo"),("correo_acceso",correo.Trim())))).FirstOrDefault();
        if(user==null) return null;
        PasswordVerificationResult result;
        try { result=_hasher.VerifyHashedPassword(correo,user.S("contrasena_hash"),password); }
        catch(FormatException) { return null; }
        if(result==PasswordVerificationResult.Failed) return null;
        var ctx=(await db.CallAsync("dbo.sp_login_contexto",SqlStore.P(("id_usuario",user.I("id_usuario"))))).FirstOrDefault();
        if(ctx==null) return null;
        var acceso=await EstadoAccesoAsync(ctx);
        if(!acceso.Permitido) return null;
        await db.CallAsync("dbo.sp_usuarios",SqlStore.P(("accion","actualizar_acceso"),("id_usuario",ctx.I("id_usuario"))));
        return Principal(ctx, acceso.EmpresaPendiente);
    }
    public static ClaimsPrincipal Principal(DbRow c,bool empresaPendiente=false) {
        var claims=new List<Claim> {
            new(ClaimTypes.NameIdentifier,c.I("id_usuario").ToString()),new(ClaimTypes.Name,c.S("nombre_usuario")),
            new(ClaimTypes.Email,c.S("correo_acceso")),new(ClaimTypes.Role,c.S("nombre_rol")),
            new("cliente",c.I("id_cliente").ToString()),new("empleado",c.I("id_empleado").ToString()),
            new("empresa",c.I("id_empresa").ToString()),new("sucursal",c.I("id_sucursal_principal").ToString()),
            new("empresa_pendiente",empresaPendiente ? "true" : "false")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims,"Cookies"));
    }
    // La empresa se registra ACTIVA: su publicación en el catálogo depende
    // exclusivamente de un cobro de plataforma confirmado y vigente.
    public async Task<(bool Permitido,bool EmpresaPendiente)> EstadoAccesoAsync(DbRow ctx) {
        var rol=ctx.S("nombre_rol");
        if(rol=="admin_citicio") return (ctx.I("id_cliente")==0 && ctx.I("id_empleado")==0,false);
        if(rol=="cliente") return (ctx.I("id_cliente")>0 && ctx.I("id_empleado")==0,false);
        if(ctx.I("id_empleado")==0 || ctx.I("id_empresa")==0) return (false,false);
        if(rol=="encargado_sucursal" && ctx.I("id_sucursal_principal")==0) return (false,false);

        var r=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
          ("accion","contexto_empleado"),("id1",ctx.I("id_empleado"))))).FirstOrDefault();
        if(r is null || r.S("estado_empleado")!="empleado_activo") return (false,false);

        var estado=r.S("estado_empresa");
        if(estado!="empresa_activa") return (false,false);
        if(rol=="encargado_sucursal" && r.S("estado_sucursal")!="sucursal_activa") return (false,false);
        return (true,false);
    }
    public async Task<bool> ActivoAsync(DbRow ctx) => (await EstadoAccesoAsync(ctx)).Permitido;
    public async Task RegistrarClienteAsync(RegistroForm f) {
        if(!PasswordPolicy.Valida(f.Contrasena)) throw new InvalidOperationException(PasswordPolicy.Mensaje);
        if(f.FechaNacimiento.Date>MexicoClock.Today || f.FechaNacimiento.Year<1900) throw new InvalidOperationException("La fecha de nacimiento no es válida.");
        if(f.AceptaWhatsapp && (string.IsNullOrWhiteSpace(f.Whatsapp) || !System.Text.RegularExpressions.Regex.IsMatch(f.Whatsapp,@"^\+[1-9]\d{7,14}$")))
            throw new InvalidOperationException("Para WhatsApp escribe tu número en formato internacional, por ejemplo +5219611234567.");
        await using var cn=await db.OpenAsync();
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
        try {
            var cl=(await SqlStore.ReadAsync(cn,tx,"dbo.sp_clientes",SqlStore.P(
                ("accion","insertar"),("nombre",f.Nombre.Trim()),("apellido_paterno",f.Paterno.Trim()),
                ("apellido_materno",f.Materno.Trim()),("telefono",f.Telefono.Trim()),("correo",f.Correo.Trim()),
                ("fecha_nacimiento",f.FechaNacimiento.Date),("telefono_whatsapp",f.AceptaWhatsapp?f.Whatsapp?.Trim():null),
                ("acepta_whatsapp",f.AceptaWhatsapp)))).First();
            var idRol=(await SqlStore.ReadAsync(cn,tx,"dbo.sp_citicio_lecturas",SqlStore.P(("accion","rol_cliente")))).First().I("id_rol");
            await SqlStore.ReadAsync(cn,tx,"dbo.sp_usuarios",SqlStore.P(
                ("accion","insertar"),("id_cliente",cl.I("id_cliente")),("id_rol",idRol),
                ("nombre_usuario",f.Correo.Trim()),("correo_acceso",f.Correo.Trim()),
                ("contrasena_hash",_hasher.HashPassword(f.Correo,f.Contrasena))));
            await tx.CommitAsync();
        } catch { await tx.RollbackAsync();throw; }
    }
    // Registro MVC y registro público utilizan el MISMO procedimiento de alta activa.
    // El procedimiento antiguo sp_registro_empresa_publico podía dejar al dueño pendiente
    // y no era compatible con la versión web integrada.
    public async Task<DbRow> RegistrarEmpresaAsync(RegistroEmpresaForm f) {
        if(!PasswordPolicy.Valida(f.Contrasena)) throw new InvalidOperationException(PasswordPolicy.Mensaje);
        if (!f.AceptaTerminos)
            throw new InvalidOperationException("Debes aceptar los términos de registro de CITICIO.");
        if (string.IsNullOrWhiteSpace(f.NombreEmpresa) || f.NombreEmpresa.Trim().Length < 3)
            throw new InvalidOperationException("El nombre comercial debe tener al menos 3 caracteres.");

        // No creamos registros si una imagen no cumple las reglas de la BD.
        var logo = await ImagenArchivoService.PrepararAsync(f.LogoArchivo);
        var portada = await ImagenArchivoService.PrepararAsync(f.PortadaArchivo);
        if (logo is not null && logo.Bytes.Length > ImagenArchivoService.LimiteBytes)
            throw new InvalidOperationException("El logotipo puede pesar hasta 2 MiB (2,097,152 bytes).");

        await using var cn = await db.OpenAsync();
        // Resolver estado, municipio y código postal A PARTIR de la colonia elegida.
        // Los IDs siempre salen de las relaciones reales de agencia_citas.
        var colonia = (await SqlStore.ReadTextAsync(cn, null, @"
            SELECT TOP (1) m.id_municipio, m.id_entidad_federativa, c.codigo_postal
            FROM dbo.colonias AS c
            INNER JOIN dbo.municipios AS m ON m.id_municipio = c.id_municipio
            WHERE c.id_colonia = @id_colonia",
            SqlStore.P(("id_colonia", f.ColoniaId)))).FirstOrDefault();
        if (colonia is null)
            throw new InvalidOperationException("La colonia seleccionada no existe en la base de datos.");

        // El guardado conserva sp_imagen_guardar cuando existe. Si esta instalación
        // carece de ese SP, se almacena en App_Data sin modificar la base de datos.

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try {
            var empresa = (await SqlStore.ReadAsync(cn, tx, "dbo.sp_citicio_registro_empresa_web", SqlStore.P(
                ("nombre_empresa", f.NombreEmpresa.Trim()),
                ("descripcion", f.Descripcion?.Trim()),
                ("id_giro", f.GiroId is > 0 ? f.GiroId : null),
                ("id_entidad_federativa", colonia.I("id_entidad_federativa")),
                ("id_municipio", colonia.I("id_municipio")),
                ("id_colonia", f.ColoniaId),
                ("codigo_postal", string.IsNullOrWhiteSpace(colonia.S("codigo_postal")) ? null : colonia.S("codigo_postal")),
                ("calle", f.Calle.Trim()),
                ("numero_exterior", f.NumeroExterior.Trim()),
                ("numero_interior", f.NumeroInterior?.Trim()),
                ("nombre_dueno", f.Nombre.Trim()),
                ("apellido_paterno", f.Paterno.Trim()),
                ("apellido_materno", f.Materno?.Trim()),
                ("telefono", f.Telefono.Trim()),
                ("correo", f.Correo.Trim().ToLowerInvariant()),
                ("contrasena_hash", _hasher.HashPassword(f.Correo.Trim(), f.Contrasena)),
                ("logo", logo?.Bytes),
                ("logo_mime", logo?.Mime)))).FirstOrDefault()
                ?? throw new InvalidOperationException("El procedimiento de registro no devolvió el ID de empresa.");

            if (portada is not null) {
                var empresaId = empresa.I("id_empresa");
                var propietarioId = empresa.I("id_usuario");
                if (empresaId <= 0 || propietarioId <= 0)
                    throw new InvalidOperationException("El registro no devolvió el identificador de empresa o propietario.");
                await ImagenArchivoService.GuardarAsync(cn, tx, propietarioId,
                    "portada_empresa", empresaId, portada, entorno.ContentRootPath);
                await SqlStore.ReadAsync(cn, tx, "dbo.sp_empresas", SqlStore.P(
                    ("accion", "actualizar"), ("id_empresa", empresaId),
                    ("imagen_portada_url", ImagenArchivoService.Ruta("portada_empresa", empresaId))));
            }
            await tx.CommitAsync();
            return empresa;
        }
        catch {
            // El SP usa XACT_ABORT y, en caso de error, puede deshacer también
            // la transacción exterior. No debemos ocultar el error SQL original.
            try { if (tx.Connection is not null) await tx.RollbackAsync(); }
            catch (InvalidOperationException) { }
            catch (SqlException) { }
            throw;
        }
    }

    // Errores de registro que se pueden mostrar sin exponer detalles internos de SQL.
    public static string MensajeErrorRegistro(SqlException ex) => ex.Number switch {
        2601 or 2627 => "Ya existe una empresa, correo, teléfono o usuario con esos datos.",
        2812 => "Falta instalar dbo.sp_citicio_registro_empresa_web en la base agencia_citas.",
        201 or 8144 => "La versión del procedimiento de registro no coincide con el backend. Revisa dbo.sp_citicio_registro_empresa_web.",
        59001 => "Faltan estados o roles necesarios en SQL Server para activar la empresa.",
        59002 => "SQL Server rechazó los datos del negocio o del propietario.",
        59003 => "El giro seleccionado ya no existe en el catálogo.",
        59004 => "La colonia, municipio, estado o código postal no coinciden en SQL Server.",
        59005 => "La calle y el número exterior son obligatorios.",
        59006 => "El logotipo no es válido o supera el máximo permitido por SQL Server.",
        208 => "Falta una tabla necesaria para registrar empresas; revisa la instalación SQL.",
        229 => "El usuario de SQL Server no tiene permisos para ejecutar el registro.",
        547 => "Un dato de estado, giro, colonia u otra relación no es válido en SQL Server.",
        -2 => "SQL Server tardó demasiado en responder. Comprueba la conexión y los bloqueos del servidor.",
        53 or 40 => "No fue posible conectar con SQL Server. Revisa la instancia configurada.",
        _ => $"No fue posible registrar la empresa (código SQL {ex.Number}). Revisa el registro del servidor."
    };
    public async Task<List<DbRow>> GirosAsync() {
        return await db.CallAsync("dbo.sp_giros",SqlStore.P(("accion","listar")));
    }
    public string Hash(string email,string pw)=>_hasher.HashPassword(email,pw);
    public async Task CrearAdminInicialAsync(string? email,string? password) {
        if(string.IsNullOrWhiteSpace(email)||string.IsNullOrWhiteSpace(password)||password.Length<12||!PasswordPolicy.Valida(password))
            throw new InvalidOperationException("Configura CITICIO_ADMIN_EMAIL y CITICIO_ADMIN_PASSWORD (12 o más caracteres, mayúscula, minúscula, número y símbolo) antes de usar --crear-admin.");
        await db.CallAsync("dbo.sp_admin_inicial_crear",SqlStore.P(
            ("correo",email.Trim().ToLowerInvariant()),("contrasena_hash",Hash(email,password))));
        Console.WriteLine("Cuenta inicial de administrador CITICIO creada. Conserva la contraseña en lugar seguro.");
    }
}
