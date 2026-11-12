using System.Data;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

public sealed class RegistroPublicoService(IConfiguration configuration, CiticioDb db)
{
    // Usa la misma conexión que los demás módulos integrados.
    // Compatibilidad con configuraciones antiguas de CITICIO.
    private string ConnectionString => configuration.GetConnectionString("AgenciaCitas")
        ?? configuration.GetConnectionString("CiticioConnection")
        ?? throw new InvalidOperationException("Configura ConnectionStrings:AgenciaCitas.");

    private static string Hash(string password) =>
        new PasswordHasher<object>().HashPassword(new object(), password);

    public async Task<Dictionary<string, object?>?> RegistrarEmpresaAsync(
        RegistroEmpresaPublicoRequest input, byte[]? logo, string? mime)
    {
        var binary = new SqlParameter("@logo", SqlDbType.VarBinary, -1)
            { Value = logo is null ? DBNull.Value : logo };
        return await db.FirstAsync("dbo.sp_citicio_registro_empresa_web", CommandType.StoredProcedure,
            CiticioDb.Param("@nombre_empresa", input.NombreEmpresa!.Trim()),
            CiticioDb.Param("@descripcion", input.Descripcion?.Trim()),
            CiticioDb.Param("@id_giro", input.IdGiro),
            CiticioDb.Param("@id_entidad_federativa", input.IdEstado),
            CiticioDb.Param("@id_municipio", input.IdMunicipio),
            CiticioDb.Param("@id_colonia", input.IdColonia),
            CiticioDb.Param("@codigo_postal", string.IsNullOrWhiteSpace(input.CodigoPostal) ? null : input.CodigoPostal),
            CiticioDb.Param("@calle", input.Calle!.Trim()),
            CiticioDb.Param("@numero_exterior", input.NumeroExterior!.Trim()),
            CiticioDb.Param("@numero_interior", input.NumeroInterior?.Trim()),
            CiticioDb.Param("@nombre_dueno", input.NombreDueno!.Trim()),
            CiticioDb.Param("@apellido_paterno", input.ApellidoPaterno!.Trim()),
            CiticioDb.Param("@apellido_materno", input.ApellidoMaterno?.Trim()),
            CiticioDb.Param("@telefono", input.Telefono!.Trim()),
            CiticioDb.Param("@correo", input.Correo!.Trim().ToLowerInvariant()),
            CiticioDb.Param("@contrasena_hash", Hash(input.Contrasena!)),
            binary, CiticioDb.Param("@logo_mime", mime));
    }

    public async Task<int> RegistrarClienteAsync(RegistroClientePublicoRequest input)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Conserva el alta de cliente de la versión anterior, dentro de transacción.
            const string sql = @"
DECLARE @rol INT = (SELECT id_rol FROM dbo.roles WHERE nombre_rol='cliente');
IF @rol IS NULL THROW 58001, 'Falta el rol cliente en dbo.roles.', 1;
INSERT INTO dbo.clientes(nombre, apellido_paterno, apellido_materno, telefono, correo, fecha_nacimiento, telefono_whatsapp, acepta_whatsapp)
VALUES(@nombre, @paterno, @materno, @telefono, @correo, @nacimiento, @whatsapp, @acepta_whatsapp);
DECLARE @cliente INT = CONVERT(INT, SCOPE_IDENTITY());
INSERT INTO dbo.usuarios(id_cliente, id_empleado, id_rol, nombre_usuario, correo_acceso, contrasena_hash)
VALUES(@cliente, NULL, @rol, CONCAT('cliente_', @cliente), @correo, @hash);
SELECT @cliente;";
            await using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddRange([
                CiticioDb.Param("@nombre", input.Nombre!.Trim()),
                CiticioDb.Param("@paterno", input.ApellidoPaterno!.Trim()),
                CiticioDb.Param("@materno", input.ApellidoMaterno!.Trim()),
                CiticioDb.Param("@telefono", input.Telefono!.Trim()),
                CiticioDb.Param("@correo", input.Correo!.Trim().ToLowerInvariant()),
                CiticioDb.Param("@nacimiento", input.FechaNacimiento!.Value.Date),
                CiticioDb.Param("@hash", Hash(input.Contrasena!)),
                CiticioDb.Param("@whatsapp",input.AceptaWhatsapp?input.TelefonoWhatsapp?.Trim():null),
                CiticioDb.Param("@acepta_whatsapp",input.AceptaWhatsapp)
            ]);
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            await transaction.CommitAsync();
            return id;
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { }
            throw;
        }
    }
}
