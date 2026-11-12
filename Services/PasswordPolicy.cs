using System.ComponentModel.DataAnnotations;

namespace Citicio.Services;

public static class PasswordPolicy
{
    public const string Mensaje = "Usa entre 8 y 128 caracteres, una mayúscula, una minúscula, un número y un símbolo.";
    public static bool Valida(string? clave) => clave is { Length: >= 8 and <= 128 }
        && clave.Any(char.IsUpper) && clave.Any(char.IsLower) && clave.Any(char.IsDigit)
        && clave.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))
        && !clave.Any(char.IsControl);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ClaveSeguraAttribute : ValidationAttribute
{
    public ClaveSeguraAttribute() { ErrorMessage = PasswordPolicy.Mensaje; }
    public override bool IsValid(object? value) => value is string s && PasswordPolicy.Valida(s);
}
