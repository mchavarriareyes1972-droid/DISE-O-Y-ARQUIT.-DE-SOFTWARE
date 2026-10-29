using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public interface IUsuariosService
{
    string Instancia { get; }
    List<UsuarioResumen> ConsultarUsuarios();
    UsuarioResumen RegistrarUsuario(RegistroUsuarioSolicitud solicitud);
    UsuarioResumen EditarUsuario(int id, EdicionUsuarioSolicitud solicitud);
    (UsuarioResumen Usuario, int Version)? Autenticar(string login, string password);
    bool ValidarSesion(int id, int version);
}
