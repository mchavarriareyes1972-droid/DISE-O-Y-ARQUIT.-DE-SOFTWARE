using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tienda_Giovanna.Models;
using Tienda_Giovanna.Services;

namespace Tienda_Giovanna.Controllers;

[Authorize(Roles = Roles.Administrador), ApiController, Route("api/usuarios")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UsuariosController : ControllerBase
{
    private readonly UsuariosService usuarios;
    public UsuariosController(UsuariosService usuarios) { this.usuarios = usuarios; }
    [HttpGet] public IActionResult Lista() => Ok(usuarios.ConsultarUsuarios());
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Registrar(RegistroUsuarioSolicitud solicitud)
    {
        try { return Ok(usuarios.RegistrarUsuario(solicitud)); }
        catch (ArgumentException error) { return BadRequest(new { mensaje = error.Message }); }
    }
    [HttpPost("{id:int}/editar"), ValidateAntiForgeryToken]
    public IActionResult Editar(int id, EdicionUsuarioSolicitud solicitud)
    {
        try { return Ok(usuarios.EditarUsuario(id, solicitud)); }
        catch (ArgumentException error) { return BadRequest(new { mensaje = error.Message }); }
    }
}
