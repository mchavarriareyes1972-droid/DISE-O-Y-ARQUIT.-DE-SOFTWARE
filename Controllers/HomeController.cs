using Microsoft.AspNetCore.Mvc;

namespace Tienda_Giovanna.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index() => View();
        public IActionResult Ventas() => View();
        public IActionResult Inventario() => View();
        public IActionResult Compras() => View();
        public IActionResult Clientes() => View();
        public IActionResult Reportes() => View();
        public IActionResult Error() => View(new Tienda_Giovanna.Models.ErrorViewModel
        {
            RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}

