using System.Data;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public sealed class ComprasSqlService
{
    private readonly ConexionBD conexion;
    public ComprasSqlService(ConexionBD conexion) { this.conexion = conexion; }
    private const string Consulta = """
        SELECT c.IdCompra, c.Fecha, c.IdProveedor, c.ProveedorNombre, c.Total, c.IdUsuario, u.Nombre,
               d.IdProducto, d.CodigoProducto, d.NombreProducto, d.Cantidad, d.CostoUnitario, d.Subtotal
        FROM Compras c INNER JOIN Usuarios u ON u.IdUsuario = c.IdUsuario
        LEFT JOIN DetalleCompras d ON d.IdCompra = c.IdCompra
        WHERE @id IS NULL OR c.IdCompra = @id
        ORDER BY c.IdCompra DESC, d.IdDetalleCompra
        """;

    private static List<Compra> Consultar(SqlConnection c, SqlTransaction? tx, int? id = null)
    {
        using var cmd = new SqlCommand(Consulta, c, tx);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        using var lector = cmd.ExecuteReader();
        var compras = new List<Compra>();
        var porId = new Dictionary<int, Compra>();
        while (lector.Read())
        {
            int compraId = lector.GetInt32(0);
            if (!porId.TryGetValue(compraId, out var compra))
            {
                compra = new()
                {
                    Id = compraId, Fecha = lector.GetDateTimeOffset(1), ProveedorId = lector.GetInt32(2),
                    ProveedorNombre = lector.GetString(3), Total = lector.GetDecimal(4),
                    UsuarioId = lector.GetInt32(5), UsuarioNombre = lector.GetString(6)
                };
                porId.Add(compraId, compra); compras.Add(compra);
            }
            if (!lector.IsDBNull(7)) compra.Detalles.Add(new()
            {
                ProductoId = lector.GetInt32(7), Codigo = lector.GetString(8), ProductoNombre = lector.GetString(9),
                Cantidad = lector.GetInt32(10), CostoUnitario = lector.GetDecimal(11), Subtotal = lector.GetDecimal(12)
            });
        }
        return compras;
    }

    public List<Compra> ConsultarCompras()
    {
        using var c = conexion.CrearConexion(); c.Open();
        return Consultar(c, null);
    }

    public Compra ConsultarCompra(int id)
    {
        using var c = conexion.CrearConexion(); c.Open();
        return Consultar(c, null, id).SingleOrDefault() ?? throw new ArgumentException("La compra no existe.");
    }

    public Compra RegistrarCompra(CompraSolicitud solicitud, int usuarioId)
    {
        List<LineaCompraSolicitud> items = ComprasValidacion.Validar(solicitud);
        if (usuarioId <= 0) throw new ArgumentException("No se pudo identificar al usuario que registra la compra.");
        using var c = conexion.CrearConexion(); c.Open();
        using var tx = c.BeginTransaction();
        // La sesion aporta el identificador. La base confirma el rol y estado actual,
        // y conserva esos datos mientras se confirma la compra.
        using (var usuario = new SqlCommand("SELECT IdUsuario FROM Usuarios WITH (HOLDLOCK) WHERE IdUsuario = @id AND Activo = 1 AND IdRol = 1", c, tx))
        {
            usuario.Parameters.Add("@id", SqlDbType.Int).Value = usuarioId;
            if (usuario.ExecuteScalar() == null) throw new ArgumentException("La cuenta no tiene permiso para registrar compras.");
        }
        string proveedorNombre;
        using (var proveedor = new SqlCommand("SELECT RazonSocial FROM Proveedores WITH (HOLDLOCK) WHERE IdProveedor = @id AND Activo = 1", c, tx))
        {
            proveedor.Parameters.Add("@id", SqlDbType.Int).Value = solicitud.ProveedorId;
            proveedorNombre = proveedor.ExecuteScalar() as string ?? throw new ArgumentException("Selecciona un proveedor activo registrado.");
        }
        var detalles = new List<DetalleCompra>();
        // Bloquear por identificador evita incrementos perdidos y ordena las compras
        // simultaneas. Las alternativas utilizan el mismo orden de bloqueo.
        foreach (var item in items)
        {
            using var producto = new SqlCommand("SELECT Codigo, Nombre, Stock, Activo FROM Productos WITH (UPDLOCK, HOLDLOCK) WHERE IdProducto = @id", c, tx);
            producto.Parameters.Add("@id", SqlDbType.Int).Value = item.ProductoId;
            using var lector = producto.ExecuteReader();
            if (!lector.Read()) throw new ArgumentException("El producto no existe.");
            if (!lector.GetBoolean(3)) throw new ArgumentException("El producto está inactivo. Actívalo desde Inventario para utilizarlo.");
            if ((long)lector.GetInt32(2) + item.Cantidad > int.MaxValue) throw new ArgumentException("La cantidad excede el límite del inventario.");
            detalles.Add(new()
            {
                ProductoId = item.ProductoId, Codigo = lector.GetString(0), ProductoNombre = lector.GetString(1),
                Cantidad = item.Cantidad, CostoUnitario = item.CostoUnitario, Subtotal = item.Cantidad * item.CostoUnitario
            });
        }
        int idCompra;
        using (var cabecera = new SqlCommand("INSERT INTO Compras (IdUsuario, IdProveedor, ProveedorNombre, Total) OUTPUT INSERTED.IdCompra VALUES (@usuario, @proveedor, @nombre, @total)", c, tx))
        {
            cabecera.Parameters.Add("@usuario", SqlDbType.Int).Value = usuarioId;
            cabecera.Parameters.Add("@proveedor", SqlDbType.Int).Value = solicitud.ProveedorId;
            cabecera.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = proveedorNombre;
            AgregarImporte(cabecera, "@total", detalles.Sum(d => d.Subtotal));
            idCompra = Convert.ToInt32(cabecera.ExecuteScalar());
        }
        foreach (var detalle in detalles)
        {
            using var insertar = new SqlCommand("INSERT INTO DetalleCompras (IdCompra, IdProducto, CodigoProducto, NombreProducto, Cantidad, CostoUnitario) VALUES (@compra, @producto, @codigo, @nombre, @cantidad, @costo)", c, tx);
            insertar.Parameters.Add("@compra", SqlDbType.Int).Value = idCompra;
            insertar.Parameters.Add("@producto", SqlDbType.Int).Value = detalle.ProductoId;
            insertar.Parameters.Add("@codigo", SqlDbType.NVarChar, 20).Value = detalle.Codigo;
            insertar.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = detalle.ProductoNombre;
            insertar.Parameters.Add("@cantidad", SqlDbType.Int).Value = detalle.Cantidad;
            AgregarImporte(insertar, "@costo", detalle.CostoUnitario);
            insertar.ExecuteNonQuery();
            using var stock = new SqlCommand("UPDATE Productos SET Stock = Stock + @cantidad WHERE IdProducto = @id", c, tx);
            stock.Parameters.Add("@id", SqlDbType.Int).Value = detalle.ProductoId;
            stock.Parameters.Add("@cantidad", SqlDbType.Int).Value = detalle.Cantidad;
            if (stock.ExecuteNonQuery() != 1) throw new ArgumentException("No se pudo actualizar el inventario.");
        }
        Compra compra = Consultar(c, tx, idCompra).Single();
        tx.Commit();
        return compra;
    }

    private static void AgregarImporte(SqlCommand cmd, string nombre, decimal valor)
    {
        var parametro = cmd.Parameters.Add(nombre, SqlDbType.Decimal);
        parametro.Precision = 18; parametro.Scale = 2; parametro.Value = valor;
    }
}
