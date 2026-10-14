using Microsoft.AspNetCore.Identity;
using System.Text.RegularExpressions;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public class UsuariosService
{
    public string Instancia { get; } = Guid.NewGuid().ToString("N");
    private readonly object bloqueo = new();
    private readonly List<Usuario> usuarios = new();
    private readonly PasswordHasher<Usuario> hasher = new();
    public UsuariosService()
    {
        // Credenciales públicas de práctica. Se reemplazarán junto con la persistencia.
        RegistrarUsuario(new() { Nombre = "Administración", Login = "admin", Rol = Roles.Administrador, Password = "Giovanna2026!" });
        RegistrarUsuario(new() { Nombre = "Caja", Login = "cajero", Rol = Roles.Cajero, Password = "Cajero2026!" });
    }
    private static UsuarioResumen Resumen(Usuario usuario) => new()
        { Id = usuario.Id, Nombre = usuario.Nombre, Login = usuario.Login, Rol = usuario.Rol, Activo = usuario.Activo };
    private (string Nombre, string Login) Validar(UsuarioSolicitud solicitud, int? id = null)
    {
        string nombre = (solicitud.Nombre ?? "").Trim();
        string login = (solicitud.Login ?? "").Trim().ToLowerInvariant();
        if (nombre.Length == 0 || nombre.Length > 100) throw new ArgumentException("Ingresa un nombre de hasta 100 caracteres.");
        if (!Regex.IsMatch(login, @"^[a-z0-9_.-]{3,30}$")) throw new ArgumentException("El usuario debe tener 3 a 30 letras, números, puntos o guiones.");
        if (usuarios.Any(u => u.Id != id && u.Login == login)) throw new ArgumentException("Ese nombre de usuario ya está registrado.");
        if (solicitud.Rol != Roles.Administrador && solicitud.Rol != Roles.Cajero) throw new ArgumentException("Selecciona Administrador o Cajero.");
        return (nombre, login);
    }
    private static void ValidarPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 100)
            throw new ArgumentException("La contraseña debe tener entre 8 y 100 caracteres.");
    }
    public List<UsuarioResumen> ConsultarUsuarios() { lock (bloqueo) { return usuarios.Select(Resumen).ToList(); } }
    public UsuarioResumen RegistrarUsuario(RegistroUsuarioSolicitud solicitud)
    {
        lock (bloqueo)
        {
            var datos = Validar(solicitud); ValidarPassword(solicitud.Password);
            Usuario usuario = new() { Id = usuarios.Count + 1, Nombre = datos.Nombre, Login = datos.Login, Rol = solicitud.Rol };
            usuario.PasswordHash = hasher.HashPassword(usuario, solicitud.Password);
            usuarios.Add(usuario);
            return Resumen(usuario);
        }
    }
    public UsuarioResumen EditarUsuario(int id, EdicionUsuarioSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Usuario usuario = usuarios.FirstOrDefault(u => u.Id == id) ?? throw new ArgumentException("El usuario no existe.");
            var datos = Validar(solicitud, id);
            bool activo = solicitud.Activo ?? throw new ArgumentException("Indica el estado del usuario.");
            if (usuario.Activo && usuario.Rol == Roles.Administrador && (!activo || solicitud.Rol != Roles.Administrador)
                && usuarios.Count(u => u.Activo && u.Rol == Roles.Administrador) == 1)
                throw new ArgumentException("Debe quedar al menos un administrador activo.");
            string hash = usuario.PasswordHash;
            if (!string.IsNullOrEmpty(solicitud.Password)) { ValidarPassword(solicitud.Password); hash = hasher.HashPassword(usuario, solicitud.Password); }
            usuario.Nombre = datos.Nombre; usuario.Login = datos.Login; usuario.Rol = solicitud.Rol; usuario.Activo = activo;
            usuario.PasswordHash = hash; usuario.Version++; usuario.Intentos = 0; usuario.BloqueadoHasta = null;
            // Cambiar permisos o contraseña invalida las sesiones anteriores.
            return Resumen(usuario);
        }
    }
    public (UsuarioResumen Usuario, int Version)? Autenticar(string login, string password)
    {
        lock (bloqueo)
        {
            Usuario? usuario = usuarios.FirstOrDefault(u => u.Login == (login ?? "").Trim().ToLowerInvariant() && u.Activo);
            if (usuario == null || (usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta.Value > DateTimeOffset.UtcNow) || string.IsNullOrEmpty(password)) return null;
            if (hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password) == PasswordVerificationResult.Failed)
            {
                if (++usuario.Intentos >= 5) { usuario.BloqueadoHasta = DateTimeOffset.UtcNow.AddMinutes(5); usuario.Intentos = 0; }
                return null;
            }
            usuario.Intentos = 0; usuario.BloqueadoHasta = null;
            return (Resumen(usuario), usuario.Version);
        }
    }
    public bool ValidarSesion(int id, int version)
    {
        lock (bloqueo) { return usuarios.Any(u => u.Id == id && u.Activo && u.Version == version); }
    }
}
