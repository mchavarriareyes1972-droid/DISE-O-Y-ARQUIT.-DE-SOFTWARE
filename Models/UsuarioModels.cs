using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tienda_Giovanna.Models;

public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Cajero = "Cajero";
}
public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Login { get; set; } = "";
    public string Rol { get; set; } = Roles.Cajero;
    public bool Activo { get; set; } = true;
    [JsonIgnore] public string PasswordHash { get; set; } = "";
    [JsonIgnore] public int Version { get; set; } = 1;
    [JsonIgnore] public int Intentos { get; set; }
    [JsonIgnore] public DateTimeOffset? BloqueadoHasta { get; set; }
}
public class UsuarioResumen
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Login { get; set; } = "";
    public string Rol { get; set; } = "";
    public bool Activo { get; set; }
}
public class UsuarioSolicitud
{
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    [Required, RegularExpression(@"^[a-zA-Z0-9_.-]{3,30}$", ErrorMessage = "El usuario debe tener 3 a 30 letras, números, puntos, guiones o guiones bajos.")] public string Login { get; set; } = "";
    [Required] public string Rol { get; set; } = Roles.Cajero;
}
public class RegistroUsuarioSolicitud : UsuarioSolicitud
{
    [Required, StringLength(100, MinimumLength = 8)] public string Password { get; set; } = "";
}
public class EdicionUsuarioSolicitud : UsuarioSolicitud
{
    [Required] public bool? Activo { get; set; }
    [StringLength(100, MinimumLength = 8)] public string? Password { get; set; }
}
public class LoginSolicitud
{
    [Required(ErrorMessage = "Ingresa tu usuario.")] public string Login { get; set; } = "";
    [Required(ErrorMessage = "Ingresa tu contraseña.")] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
