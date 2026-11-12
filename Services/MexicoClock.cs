namespace Citicio.Services;
/// <summary>Fecha/hora de operación de CITICIO para el esquema SQL que guarda hora local.</summary>
public static class MexicoClock {
    private static readonly TimeZoneInfo Zone=Find();
    private static TimeZoneInfo Find() {
        foreach(var name in new[]{"Central Standard Time (Mexico)","America/Mexico_City"})
            try {return TimeZoneInfo.FindSystemTimeZoneById(name);}catch(TimeZoneNotFoundException) { }
        throw new InvalidOperationException("Zona horaria del centro de México no instalada.");
    }
    public static DateTime Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,Zone).DateTime;
    public static DateTime Today => Now.Date;
}
