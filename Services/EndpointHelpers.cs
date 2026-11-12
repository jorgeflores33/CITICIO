using System;
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

internal static class EndpointHelpers
{
    public static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
    public static SqlParameter DateParam(string name, DateOnly date) => new(name, SqlDbType.Date) { Value = date.ToDateTime(TimeOnly.MinValue) };
    public static SqlParameter TimeParam(string name, TimeOnly time) => new(name, SqlDbType.Time) { Value = time.ToTimeSpan() };
    public static string? EmptyAsNull(string? s, int limit) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, limit)];
    public static string SafeSqlError(string s) => s.Length > 240 ? s[..240] : s;
    public static bool ValidPerson(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Trim().Length <= max;
    public static bool ValidPhone(string? s) => s is not null && s.Length <= 15 && Regex.IsMatch(s, @"^\+?\d{10,15}$");
    public static bool ValidWhatsApp(string? s) => s is not null && Regex.IsMatch(s, @"^\+[1-9]\d{8,14}$");
    public static bool ValidEmail(string? s) => s is not null && s.Length <= 254 && Regex.IsMatch(s.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
    public static bool ValidPassword(string? s) => PasswordPolicy.Valida(s);

}
