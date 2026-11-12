namespace Citicio.Models;

internal sealed record LoginIdentity;
internal sealed record LoginDto(string Correo, string Contrasena);
internal sealed record RegisterDto(string Nombre, string ApellidoPaterno, string ApellidoMaterno,
    string Telefono, string Correo, DateOnly FechaNacimiento, string Contrasena,
    bool AceptaWhatsapp, string? TelefonoWhatsapp);
internal sealed record NuevaCitaDto(int Sucursal, int Servicio, int Empleado, DateOnly Fecha, TimeOnly Hora, string? Observaciones);
internal sealed record NuevaResenaDto(int Cita, byte Calificacion, string? Comentario);
internal sealed record PerfilDto(string Nombre, string ApellidoPaterno, string ApellidoMaterno,
    string Telefono, bool AceptaWhatsapp, string? TelefonoWhatsapp);

