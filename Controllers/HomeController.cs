using Microsoft.AspNetCore.Mvc;

namespace BodegaGiovanna.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index() => View();
        public IActionResult Ventas() => View();
        public IActionResult Inventario() => View();
        public IActionResult Compras() => View();
        public IActionResult Clientes() => View();
        public IActionResult Reportes() => View();
    }
}
