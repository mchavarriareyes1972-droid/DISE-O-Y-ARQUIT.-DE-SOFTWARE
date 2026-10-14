using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tienda_Giovanna.Models;
using Tienda_Giovanna.Services;

namespace Tienda_Giovanna.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CuentaController : Controller
{
    private readonly UsuariosService usuarios;
    public CuentaController(UsuariosService usuarios) { this.usuarios = usuarios; }
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginSolicitud { ReturnUrl = returnUrl });
    }
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginSolicitud solicitud)
    {
        if (!ModelState.IsValid) return View(solicitud);
        var sesion = usuarios.Autenticar(solicitud.Login, solicitud.Password);
        if (!sesion.HasValue)
        {
            ModelState.AddModelError("", "No se pudo iniciar sesión. Revisa tus datos; después de varios intentos fallidos espera cinco minutos.");
            return View(solicitud);
        }
        var (usuario, version) = sesion.Value;
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()), new Claim(ClaimTypes.Name, usuario.Nombre),
            new Claim(ClaimTypes.Role, usuario.Rol), new Claim("Version", version.ToString()), new Claim("Instancia", usuarios.Instancia) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false });
        return Url.IsLocalUrl(solicitud.ReturnUrl) ? LocalRedirect(solicitud.ReturnUrl!) : RedirectToAction("Index", "Home");
    }
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Salir()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
    [Authorize, HttpGet]
    public IActionResult AccesoDenegado() { Response.StatusCode = 403; return View(); }
}
