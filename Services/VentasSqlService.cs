using System.Data;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public sealed class VentasSqlService
{
    private readonly ConexionBD conexion;
    public VentasSqlService(ConexionBD conexion) { this.conexion = conexion; }
    private const string Consulta = """
        SELECT v.IdVenta, v.Fecha, v.IdUsuario, u.Nombre, v.IdCliente, v.ClienteNombre,
               v.FormaPago, v.ReferenciaPago, v.Total, v.MontoRecibido, v.Vuelto,
               d.IdProducto, d.CodigoProducto, d.NombreProducto, d.Cantidad, d.PrecioUnitario, d.Subtotal
        FROM Ventas v INNER JOIN Usuarios u ON u.IdUsuario = v.IdUsuario
        LEFT JOIN DetalleVentas d ON d.IdVenta = v.IdVenta
        WHERE (@id IS NULL OR v.IdVenta = @id)
          AND (@desde IS NULL OR CONVERT(date, SWITCHOFFSET(v.Fecha, '-05:00')) >= @desde)
          AND (@hasta IS NULL OR CONVERT(date, SWITCHOFFSET(v.Fecha, '-05:00')) <= @hasta)
        ORDER BY v.IdVenta DESC, d.IdDetalleVenta
        """;

    private static List<Venta> Consultar(SqlConnection c, SqlTransaction? tx, int? id = null, DateOnly? desde = null, DateOnly? hasta = null)
    {
        using var cmd = new SqlCommand(Consulta, c, tx);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        cmd.Parameters.Add("@desde", SqlDbType.Date).Value = desde.HasValue ? desde.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@hasta", SqlDbType.Date).Value = hasta.HasValue ? hasta.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        using var lector = cmd.ExecuteReader();
        var ventas = new List<Venta>();
        var porId = new Dictionary<int, Venta>();
        while (lector.Read())
        {
            int ventaId = lector.GetInt32(0);
            if (!porId.TryGetValue(ventaId, out var venta))
            {
                venta = new()
                {
                    Id = ventaId, Fecha = lector.GetDateTimeOffset(1), UsuarioId = lector.GetInt32(2), UsuarioNombre = lector.GetString(3),
                    ClienteId = lector.IsDBNull(4) ? null : lector.GetInt32(4), ClienteNombre = lector.IsDBNull(5) ? null : lector.GetString(5),
                    FormaPago = lector.GetString(6), ReferenciaPago = lector.IsDBNull(7) ? null : lector.GetString(7),
                    Total = lector.GetDecimal(8), MontoRecibido = lector.GetDecimal(9), Vuelto = lector.GetDecimal(10)
                };
                porId.Add(ventaId, venta); ventas.Add(venta);
            }
            if (!lector.IsDBNull(11)) venta.Detalles.Add(new()
            {
                ProductoId = lector.GetInt32(11), Codigo = lector.GetString(12), Nombre = lector.GetString(13),
                Cantidad = lector.GetInt32(14), PrecioUnitario = lector.GetDecimal(15), Subtotal = lector.GetDecimal(16)
            });
        }
        return ventas;
    }

    public List<Venta> ConsultarVentas(DateOnly? desde, DateOnly? hasta)
    {
        if (desde.HasValue && hasta.HasValue && desde > hasta)
            throw new ArgumentException("La fecha inicial no puede ser posterior a la final.");
        using var c = conexion.CrearConexion(); c.Open();
        return Consultar(c, null, desde: desde, hasta: hasta);
    }

    public Venta ConsultarVenta(int id)
    {
        using var c = conexion.CrearConexion(); c.Open();
        return Consultar(c, null, id).SingleOrDefault() ?? throw new ArgumentException("La venta no existe.");
    }

    public Venta RegistrarVenta(VentaSolicitud solicitud, int usuarioId)
    {
        var items = VentasValidacion.Validar(solicitud);
        if (usuarioId <= 0) throw new ArgumentException("No se pudo identificar al usuario que registra la venta.");
        using var c = conexion.CrearConexion(); c.Open();
        using var tx = c.BeginTransaction();
        using (var usuario = new SqlCommand("SELECT IdUsuario FROM Usuarios WITH (HOLDLOCK) WHERE IdUsuario = @id AND Activo = 1 AND IdRol IN (1, 2)", c, tx))
        {
            usuario.Parameters.Add("@id", SqlDbType.Int).Value = usuarioId;
            if (usuario.ExecuteScalar() == null) throw new ArgumentException("La cuenta no tiene permiso para registrar ventas.");
        }
        string? clienteNombre = null;
        decimal deuda = 0;
        if (solicitud.FormaPago == "Fiado")
        {
            // Este bloqueo tambien permite serializar los futuros abonos del mismo cliente.
            using var cliente = new SqlCommand("SELECT Nombre FROM Clientes WITH (UPDLOCK, HOLDLOCK) WHERE IdCliente = @id AND Activo = 1", c, tx);
            cliente.Parameters.Add("@id", SqlDbType.Int).Value = solicitud.ClienteId!.Value;
            clienteNombre = cliente.ExecuteScalar() as string ?? throw new ArgumentException("Selecciona un cliente activo para la venta al fiado.");
            using var saldo = new SqlCommand("SELECT Deuda FROM vw_SaldosClientes WHERE IdCliente = @id", c, tx);
            saldo.Parameters.Add("@id", SqlDbType.Int).Value = solicitud.ClienteId.Value;
            deuda = Convert.ToDecimal(saldo.ExecuteScalar());
        }
        var detalles = new List<DetalleVenta>();
        decimal total = 0;
        // Compras y alternativas bloquean los productos en el mismo orden de identificador.
        foreach (var item in items)
        {
            using var producto = new SqlCommand("SELECT Codigo, Nombre, PrecioVenta, Stock, Activo FROM Productos WITH (UPDLOCK, HOLDLOCK) WHERE IdProducto = @id", c, tx);
            producto.Parameters.Add("@id", SqlDbType.Int).Value = item.ProductoId;
            using var lector = producto.ExecuteReader();
            if (!lector.Read()) throw new ArgumentException("El producto no existe.");
            if (!lector.GetBoolean(4)) throw new ArgumentException("El producto está inactivo. Actívalo desde Inventario para utilizarlo.");
            string nombre = lector.GetString(1);
            int stock = lector.GetInt32(3);
            if (item.Cantidad > stock) throw new ArgumentException($"Stock insuficiente de {nombre}. Disponible: {stock}.");
            decimal precio = lector.GetDecimal(2), subtotal = precio * item.Cantidad;
            total += subtotal;
            if (total > VentasValidacion.ImporteMaximo) throw new ArgumentException("El total excede el límite permitido.");
            detalles.Add(new() { ProductoId = item.ProductoId, Codigo = lector.GetString(0), Nombre = nombre,
                Cantidad = item.Cantidad, PrecioUnitario = precio, Subtotal = subtotal });
        }
        decimal recibido = solicitud.FormaPago == "Efectivo" ? decimal.Round(solicitud.MontoRecibido, 2, MidpointRounding.AwayFromZero)
            : solicitud.FormaPago == "Digital" ? total : 0;
        if (solicitud.FormaPago == "Efectivo" && recibido < total) throw new ArgumentException("El monto recibido no cubre el total.");
        if (clienteNombre != null && deuda + total > VentasValidacion.ImporteMaximo) throw new ArgumentException("La deuda excede el límite permitido.");
        int idVenta;
        using (var cabecera = new SqlCommand("INSERT INTO Ventas (IdUsuario, IdCliente, ClienteNombre, FormaPago, ReferenciaPago, Total, MontoRecibido, Vuelto) OUTPUT INSERTED.IdVenta VALUES (@usuario, @cliente, @nombre, @forma, @referencia, @total, @recibido, @vuelto)", c, tx))
        {
            cabecera.Parameters.Add("@usuario", SqlDbType.Int).Value = usuarioId;
            cabecera.Parameters.Add("@cliente", SqlDbType.Int).Value = clienteNombre != null ? solicitud.ClienteId!.Value : DBNull.Value;
            cabecera.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = (object?)clienteNombre ?? DBNull.Value;
            cabecera.Parameters.Add("@forma", SqlDbType.NVarChar, 10).Value = solicitud.FormaPago;
            cabecera.Parameters.Add("@referencia", SqlDbType.NVarChar, 100).Value = solicitud.FormaPago == "Digital" ? solicitud.ReferenciaPago!.Trim() : DBNull.Value;
            AgregarImporte(cabecera, "@total", total); AgregarImporte(cabecera, "@recibido", recibido);
            AgregarImporte(cabecera, "@vuelto", solicitud.FormaPago == "Efectivo" ? recibido - total : 0);
            idVenta = Convert.ToInt32(cabecera.ExecuteScalar());
        }
        foreach (var detalle in detalles)
        {
            using var insertar = new SqlCommand("INSERT INTO DetalleVentas (IdVenta, IdProducto, CodigoProducto, NombreProducto, Cantidad, PrecioUnitario) VALUES (@venta, @producto, @codigo, @nombre, @cantidad, @precio)", c, tx);
            insertar.Parameters.Add("@venta", SqlDbType.Int).Value = idVenta;
            insertar.Parameters.Add("@producto", SqlDbType.Int).Value = detalle.ProductoId;
            insertar.Parameters.Add("@codigo", SqlDbType.NVarChar, 20).Value = detalle.Codigo;
            insertar.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = detalle.Nombre;
            insertar.Parameters.Add("@cantidad", SqlDbType.Int).Value = detalle.Cantidad;
            AgregarImporte(insertar, "@precio", detalle.PrecioUnitario); insertar.ExecuteNonQuery();
            using var stock = new SqlCommand("UPDATE Productos SET Stock = Stock - @cantidad WHERE IdProducto = @id AND Stock >= @cantidad", c, tx);
            stock.Parameters.Add("@id", SqlDbType.Int).Value = detalle.ProductoId;
            stock.Parameters.Add("@cantidad", SqlDbType.Int).Value = detalle.Cantidad;
            if (stock.ExecuteNonQuery() != 1) throw new ArgumentException("No se pudo actualizar el inventario.");
        }
        Venta venta = Consultar(c, tx, idVenta).Single();
        tx.Commit();
        // La vista de saldos incorpora el fiado confirmado; SaldoInicial se conserva.
        return venta;
    }

    private static void AgregarImporte(SqlCommand cmd, string nombre, decimal valor)
    {
        var parametro = cmd.Parameters.Add(nombre, SqlDbType.Decimal);
        parametro.Precision = 18; parametro.Scale = 2; parametro.Value = valor;
    }
}
