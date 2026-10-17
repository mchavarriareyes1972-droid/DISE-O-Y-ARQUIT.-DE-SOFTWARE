using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Controllers
{
    [Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HomeController : Controller
    {
        public IActionResult Index() => View();
        public IActionResult Ventas() => View();
        public IActionResult Inventario() => View();
        [Authorize(Roles = Roles.Administrador)] public IActionResult Compras() => View();
        [Authorize(Roles = Roles.Administrador)] public IActionResult Proveedores() => View();
        [Authorize(Roles = Roles.Administrador)] public IActionResult Usuarios() => View();
        public IActionResult Clientes() => View();
        public IActionResult Reportes() => View();
        [AllowAnonymous] public IActionResult Error() => View(new Tienda_Giovanna.Models.ErrorViewModel
        {
            RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}

