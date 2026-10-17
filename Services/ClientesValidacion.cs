using System.Text.RegularExpressions;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

internal static class ClientesValidacion
{
    public static Cliente Validar(ClienteSolicitud solicitud)
    {
        string Texto(string? valor, int maximo, string campo, bool obligatorio = false)
        {
            string texto = (valor ?? "").Trim();
            if ((obligatorio && texto.Length == 0) || texto.Length > maximo)
                throw new ArgumentException($"Revisa el campo {campo} (máximo {maximo} caracteres).");
            return texto;
        }
        string dni = Texto(solicitud.Documento, 8, "DNI", true);
        if (!Regex.IsMatch(dni, @"^[0-9]{8}$")) throw new ArgumentException("El DNI debe tener 8 dígitos.");
        return new()
        {
            Documento = dni, Nombre = Texto(solicitud.Nombre, 100, "nombre", true),
            Telefono = Texto(solicitud.Telefono, 20, "teléfono"), Direccion = Texto(solicitud.Direccion, 150, "dirección")
        };
    }
}
