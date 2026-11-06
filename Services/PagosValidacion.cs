using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public static class PagosValidacion
{
    public static decimal Validar(PagoSolicitud solicitud)
    {
        if (solicitud.ClienteId <= 0) throw new ArgumentException("Selecciona un cliente para registrar el abono.");
        if (solicitud.Monto <= 0 || solicitud.Monto > VentasValidacion.ImporteMaximo)
            throw new ArgumentException("El abono debe ser mayor que cero y estar dentro del límite permitido.");
        decimal monto = decimal.Round(solicitud.Monto, 2, MidpointRounding.AwayFromZero);
        if (monto <= 0) throw new ArgumentException("El abono mínimo es S/ 0.01.");
        if (solicitud.Metodo is not ("Efectivo" or "Digital"))
            throw new ArgumentException("Selecciona el método de pago.");
        return monto;
    }
}
