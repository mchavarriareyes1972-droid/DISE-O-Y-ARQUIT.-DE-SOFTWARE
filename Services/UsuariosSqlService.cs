using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// Persistencia del inicio de sesion y de la gestion de usuarios en SQL Server.
public class UsuariosSqlService : IUsuariosService
{
    private readonly ConexionBD baseDatos;
    private readonly PasswordHasher<Usuario> hasher = new();
    public string Instancia { get; } = Guid.NewGuid().ToString("N");
    public UsuariosSqlService(ConexionBD baseDatos) { this.baseDatos = baseDatos; }

    private const string ConsultaUsuario = """
        SELECT u.IdUsuario, u.Nombre, u.Login, r.Nombre AS Rol, u.Activo,
               u.PasswordHash, u.Version, u.Intentos, u.BloqueadoHasta
        FROM Usuarios u WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN Roles r ON r.IdRol = u.IdRol
        """;

    private static Usuario Leer(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), Nombre = r.GetString(1), Login = r.GetString(2), Rol = r.GetString(3),
        Activo = r.GetBoolean(4), PasswordHash = r.GetString(5), Version = r.GetInt32(6),
        Intentos = r.GetInt32(7), BloqueadoHasta = r.IsDBNull(8) ? null : r.GetDateTimeOffset(8)
    };
    private static UsuarioResumen Resumen(Usuario u) => new()
        { Id = u.Id, Nombre = u.Nombre, Login = u.Login, Rol = u.Rol, Activo = u.Activo };
    private static (string Nombre, string Login, byte IdRol) Validar(UsuarioSolicitud s)
    {
        string nombre = (s.Nombre ?? "").Trim(), login = (s.Login ?? "").Trim().ToLowerInvariant();
        if (nombre.Length == 0 || nombre.Length > 100) throw new ArgumentException("Ingresa un nombre de hasta 100 caracteres.");
        if (!Regex.IsMatch(login, @"^[a-z0-9_.-]{3,30}$")) throw new ArgumentException("El usuario debe tener 3 a 30 letras, numeros, puntos o guiones.");
        byte rol = s.Rol == Roles.Administrador ? (byte)1 : s.Rol == Roles.Cajero ? (byte)2 : throw new ArgumentException("Selecciona Administrador o Cajero.");
        return (nombre, login, rol);
    }
    private static void ValidarPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 100)
            throw new ArgumentException("La contrasena debe tener entre 8 y 100 caracteres.");
    }
    private static void Datos(SqlCommand cmd, string nombre, string login, byte rol)
    {
        cmd.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = nombre;
        cmd.Parameters.Add("@login", SqlDbType.VarChar, 30).Value = login;
        cmd.Parameters.Add("@rol", SqlDbType.TinyInt).Value = rol;
    }

    public List<UsuarioResumen> ConsultarUsuarios()
    {
        using var conexion = baseDatos.CrearConexion(); conexion.Open();
        using var cmd = new SqlCommand("SELECT u.IdUsuario, u.Nombre, u.Login, r.Nombre, u.Activo FROM Usuarios u INNER JOIN Roles r ON r.IdRol=u.IdRol ORDER BY u.IdUsuario", conexion);
        using var lector = cmd.ExecuteReader();
        List<UsuarioResumen> lista = new();
        while (lector.Read()) lista.Add(new() { Id=lector.GetInt32(0), Nombre=lector.GetString(1), Login=lector.GetString(2), Rol=lector.GetString(3), Activo=lector.GetBoolean(4) });
        return lista;
    }

    public UsuarioResumen RegistrarUsuario(RegistroUsuarioSolicitud solicitud)
    {
        var datos = Validar(solicitud); ValidarPassword(solicitud.Password);
        Usuario usuario = new() { Nombre=datos.Nombre, Login=datos.Login, Rol=solicitud.Rol };
        string hash = hasher.HashPassword(usuario, solicitud.Password);
        using var conexion = baseDatos.CrearConexion(); conexion.Open();
        using var cmd = new SqlCommand("INSERT INTO Usuarios (Nombre, Login, PasswordHash, IdRol) VALUES (@nombre,@login,@hash,@rol); SELECT CONVERT(int,SCOPE_IDENTITY());", conexion);
        Datos(cmd, datos.Nombre, datos.Login, datos.IdRol);
        cmd.Parameters.Add("@hash", SqlDbType.NVarChar, 512).Value = hash;
        try { usuario.Id = (int)cmd.ExecuteScalar()!; }
        catch (SqlException e) when (e.Number is 2601 or 2627) { throw new ArgumentException("Ese nombre de usuario ya esta registrado."); }
        return Resumen(usuario);
    }

    public UsuarioResumen EditarUsuario(int id, EdicionUsuarioSolicitud solicitud)
    {
        var datos = Validar(solicitud);
        bool activo = solicitud.Activo ?? throw new ArgumentException("Indica el estado del usuario.");
        if (!string.IsNullOrEmpty(solicitud.Password)) ValidarPassword(solicitud.Password);
        using var conexion = baseDatos.CrearConexion(); conexion.Open();
        using var transaccion = conexion.BeginTransaction(IsolationLevel.Serializable);
        // El bloqueo tambien protege al ultimo administrador ante ediciones simultaneas.
        List<Usuario> lista = new();
        using (var consultar = new SqlCommand(ConsultaUsuario, conexion, transaccion))
        using (var lector = consultar.ExecuteReader()) while (lector.Read()) lista.Add(Leer(lector));
        Usuario usuario = lista.FirstOrDefault(u => u.Id == id) ?? throw new ArgumentException("El usuario no existe.");
        if (lista.Any(u => u.Id != id && string.Equals(u.Login, datos.Login, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ese nombre de usuario ya esta registrado.");
        if (usuario.Activo && usuario.Rol == Roles.Administrador && (!activo || solicitud.Rol != Roles.Administrador)
            && lista.Count(u => u.Activo && u.Rol == Roles.Administrador) == 1)
            throw new ArgumentException("Debe quedar al menos un administrador activo.");
        string hash = string.IsNullOrEmpty(solicitud.Password) ? usuario.PasswordHash : hasher.HashPassword(usuario, solicitud.Password);
        using var cmd = new SqlCommand("UPDATE Usuarios SET Nombre=@nombre,Login=@login,PasswordHash=@hash,IdRol=@rol,Activo=@activo,Version=Version+1,Intentos=0,BloqueadoHasta=NULL WHERE IdUsuario=@id", conexion, transaccion);
        Datos(cmd, datos.Nombre, datos.Login, datos.IdRol);
        cmd.Parameters.Add("@hash", SqlDbType.NVarChar, 512).Value = hash;
        cmd.Parameters.Add("@activo", SqlDbType.Bit).Value = activo;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        try { cmd.ExecuteNonQuery(); }
        catch (SqlException e) when (e.Number is 2601 or 2627) { throw new ArgumentException("Ese nombre de usuario ya esta registrado."); }
        transaccion.Commit();
        usuario.Nombre=datos.Nombre; usuario.Login=datos.Login; usuario.Rol=solicitud.Rol; usuario.Activo=activo;
        return Resumen(usuario);
    }

    public (UsuarioResumen Usuario, int Version)? Autenticar(string login, string password)
    {
        login = (login ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(login, @"^[a-z0-9_.-]{3,30}$") || string.IsNullOrEmpty(password) || password.Length > 100) return null;
        using var conexion = baseDatos.CrearConexion(); conexion.Open();
        using var transaccion = conexion.BeginTransaction(IsolationLevel.Serializable);
        Usuario? usuario = null;
        using (var consultar = new SqlCommand(ConsultaUsuario + " WHERE u.Login=@login", conexion, transaccion))
        {
            consultar.Parameters.Add("@login", SqlDbType.VarChar, 30).Value = login;
            using var lector = consultar.ExecuteReader();
            if (lector.Read()) usuario=Leer(lector);
        }
        if (usuario == null || !usuario.Activo || (usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta.Value > DateTimeOffset.UtcNow)) return null;
        PasswordVerificationResult resultado;
        try { resultado = hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password); }
        catch (FormatException) { resultado = PasswordVerificationResult.Failed; }
        bool correcto = resultado != PasswordVerificationResult.Failed;
        if (correcto)
        {
            usuario.Intentos=0; usuario.BloqueadoHasta=null;
            if (resultado == PasswordVerificationResult.SuccessRehashNeeded) usuario.PasswordHash=hasher.HashPassword(usuario,password);
        }
        else if (++usuario.Intentos >= 5) { usuario.Intentos=0; usuario.BloqueadoHasta=DateTimeOffset.UtcNow.AddMinutes(5); }
        using var actualizar = new SqlCommand("UPDATE Usuarios SET Intentos=@intentos,BloqueadoHasta=@hasta,PasswordHash=@hash WHERE IdUsuario=@id", conexion, transaccion);
        actualizar.Parameters.Add("@intentos",SqlDbType.Int).Value=usuario.Intentos;
        actualizar.Parameters.Add("@hasta",SqlDbType.DateTimeOffset).Value=(object?)usuario.BloqueadoHasta ?? DBNull.Value;
        actualizar.Parameters.Add("@hash",SqlDbType.NVarChar,512).Value=usuario.PasswordHash;
        actualizar.Parameters.Add("@id",SqlDbType.Int).Value=usuario.Id;
        actualizar.ExecuteNonQuery(); transaccion.Commit();
        return correcto ? (Resumen(usuario),usuario.Version) : null;
    }

    public bool ValidarSesion(int id, int version)
    {
        using var conexion = baseDatos.CrearConexion(); conexion.Open();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM Usuarios WHERE IdUsuario=@id AND Version=@version AND Activo=1",conexion);
        cmd.Parameters.Add("@id",SqlDbType.Int).Value=id;
        cmd.Parameters.Add("@version",SqlDbType.Int).Value=version;
        return (int)cmd.ExecuteScalar()! == 1;
    }
}
