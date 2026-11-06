using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

internal static class ProveedoresValidacion
{
    public static Proveedor Validar(ProveedorSolicitud solicitud)
    {
        string Texto(string? valor, int maximo, string campo, bool obligatorio = false)
        {
            string texto = (valor ?? "").Trim();
            if ((obligatorio && texto.Length == 0) || texto.Length > maximo)
                throw new ArgumentException($"Revisa el campo {campo} (máximo {maximo} caracteres).");
            return texto;
        }
        string ruc = Texto(solicitud.Ruc, 11, "RUC", true);
        if (!Regex.IsMatch(ruc, @"^[0-9]{11}$")) throw new ArgumentException("El RUC debe tener 11 dígitos.");
        string correo = Texto(solicitud.Correo, 100, "correo");
        if (correo.Length > 0 && !new EmailAddressAttribute().IsValid(correo)) throw new ArgumentException("El correo no es válido.");
        return new()
        {
            Ruc = ruc, RazonSocial = Texto(solicitud.RazonSocial, 100, "razón social", true),
            Contacto = Texto(solicitud.Contacto, 100, "contacto"), Telefono = Texto(solicitud.Telefono, 20, "teléfono"),
            Correo = correo, Direccion = Texto(solicitud.Direccion, 150, "dirección")
        };
    }
}
