using System.Data;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public sealed class PagosSqlService
{
    private readonly ConexionBD conexion;
    public PagosSqlService(ConexionBD conexion) { this.conexion = conexion; }

    internal static List<Pago> Consultar(SqlConnection c, SqlTransaction? tx, int? clienteId = null, int? id = null, DateOnly? desde = null, DateOnly? hasta = null)
    {
        // Al recuperar el pago recien insertado, buscar por su clave evita
        // escanear pagos pendientes de otras transacciones.
        string filtro = id.HasValue ? "p.IdPago = @id" : "(@cliente IS NULL OR p.IdCliente = @cliente)";
        using var cmd = new SqlCommand($"""
            SELECT p.IdPago, p.IdCliente, p.ClienteNombre, p.Fecha, p.Monto, p.Metodo,
                   p.SaldoRestante, p.IdUsuario, u.Nombre
            FROM Pagos p INNER JOIN Usuarios u ON u.IdUsuario = p.IdUsuario
            WHERE {filtro}
              AND (@desde IS NULL OR CONVERT(date, SWITCHOFFSET(p.Fecha, '-05:00')) >= @desde)
              AND (@hasta IS NULL OR CONVERT(date, SWITCHOFFSET(p.Fecha, '-05:00')) <= @hasta)
            ORDER BY p.IdPago DESC
            """, c, tx);
        cmd.Parameters.Add("@cliente", SqlDbType.Int).Value = (object?)clienteId ?? DBNull.Value;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        cmd.Parameters.Add("@desde", SqlDbType.Date).Value = desde.HasValue ? desde.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@hasta", SqlDbType.Date).Value = hasta.HasValue ? hasta.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        using var lector = cmd.ExecuteReader();
        var pagos = new List<Pago>();
        while (lector.Read()) pagos.Add(new()
        {
            Id = lector.GetInt32(0), ClienteId = lector.GetInt32(1), ClienteNombre = lector.GetString(2),
            Fecha = lector.GetDateTimeOffset(3), Monto = lector.GetDecimal(4), Metodo = lector.GetString(5),
            SaldoRestante = lector.GetDecimal(6), UsuarioId = lector.GetInt32(7), UsuarioNombre = lector.GetString(8)
        });
        return pagos;
    }

    public List<Pago> ConsultarPagos(int? clienteId, DateOnly? desde = null, DateOnly? hasta = null)
    {
        if (desde.HasValue && hasta.HasValue && desde > hasta)
            throw new ArgumentException("La fecha inicial no puede ser posterior a la final.");
        using var c = conexion.CrearConexion(); c.Open();
        return Consultar(c, null, clienteId, desde: desde, hasta: hasta);
    }

    public Pago RegistrarPago(PagoSolicitud solicitud, int usuarioId)
    {
        decimal monto = PagosValidacion.Validar(solicitud);
        if (usuarioId <= 0) throw new ArgumentException("No se pudo identificar al usuario que registra el abono.");
        using var c = conexion.CrearConexion(); c.Open();
        using var tx = c.BeginTransaction();
        using (var usuario = new SqlCommand("SELECT IdUsuario FROM Usuarios WITH (HOLDLOCK) WHERE IdUsuario = @id AND Activo = 1 AND IdRol IN (1, 2)", c, tx))
        {
            usuario.Parameters.Add("@id", SqlDbType.Int).Value = usuarioId;
            if (usuario.ExecuteScalar() == null) throw new ArgumentException("La cuenta no tiene permiso para registrar abonos.");
        }
        string nombre;
        // Ventas al fiado utiliza este mismo bloqueo y orden. El saldo se lee
        // despues de obtenerlo, para que dos cobros no consuman la misma deuda.
        // Un cliente inactivo puede cancelar una deuda que ya tenia registrada.
        using (var cliente = new SqlCommand("SELECT Nombre FROM Clientes WITH (UPDLOCK, HOLDLOCK) WHERE IdCliente = @id", c, tx))
        {
            cliente.Parameters.Add("@id", SqlDbType.Int).Value = solicitud.ClienteId;
            nombre = cliente.ExecuteScalar() as string ?? throw new ArgumentException("El cliente no existe.");
        }
        decimal deuda;
        using (var saldo = new SqlCommand("SELECT Deuda FROM vw_SaldosClientes WHERE IdCliente = @id", c, tx))
        {
            saldo.Parameters.Add("@id", SqlDbType.Int).Value = solicitud.ClienteId;
            deuda = Convert.ToDecimal(saldo.ExecuteScalar());
        }
        if (monto > deuda) throw new ArgumentException("El abono no puede superar la deuda pendiente. Actualiza el saldo e intenta nuevamente.");
        int idPago;
        using (var insertar = new SqlCommand("""
            DECLARE @nuevo TABLE (Id int);
            INSERT INTO Pagos (IdUsuario, IdCliente, ClienteNombre, Metodo, Monto, SaldoRestante)
            OUTPUT INSERTED.IdPago INTO @nuevo
            VALUES (@usuario, @cliente, @nombre, @metodo, @monto, @restante);
            SELECT Id FROM @nuevo;
            """, c, tx))
        {
            insertar.Parameters.Add("@usuario", SqlDbType.Int).Value = usuarioId;
            insertar.Parameters.Add("@cliente", SqlDbType.Int).Value = solicitud.ClienteId;
            insertar.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = nombre;
            insertar.Parameters.Add("@metodo", SqlDbType.NVarChar, 10).Value = solicitud.Metodo;
            AgregarImporte(insertar, "@monto", monto);
            AgregarImporte(insertar, "@restante", deuda - monto);
            idPago = Convert.ToInt32(insertar.ExecuteScalar());
        }
        Pago pago = Consultar(c, tx, id: idPago).Single();
        tx.Commit();
        // vw_SaldosClientes descuenta el nuevo pago. SaldoInicial y las ventas
        // historicas se conservan; no hay una segunda escritura de la deuda.
        return pago;
    }

    private static void AgregarImporte(SqlCommand cmd, string nombre, decimal valor)
    {
        var parametro = cmd.Parameters.Add(nombre, SqlDbType.Decimal);
        parametro.Precision = 18; parametro.Scale = 2; parametro.Value = valor;
    }
}
