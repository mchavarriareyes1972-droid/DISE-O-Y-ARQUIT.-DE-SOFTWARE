using System.Data;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// El mismo Singleton configura las conexiones; cada operacion crea y libera la suya.
public sealed class ProductosSqlService
{
    private readonly ConexionBD conexion;
    public ProductosSqlService(ConexionBD conexion) { this.conexion = conexion; }
    private const string Consulta = """
        SELECT p.IdProducto, p.Codigo, p.Nombre, p.PrecioVenta, p.Stock, p.StockMinimo, p.Activo, a.IdAlternativo
        FROM Productos p LEFT JOIN ProductosAlternativos a ON a.IdProducto = p.IdProducto
        """;
    private const string Orden = " ORDER BY p.Nombre, p.IdProducto, a.IdAlternativo";

    private static List<Producto> LeerProductos(SqlDataReader lector)
    {
        var productos = new List<Producto>();
        var porId = new Dictionary<int, Producto>();
        while (lector.Read())
        {
            int id = lector.GetInt32(0);
            if (!porId.TryGetValue(id, out var producto))
            {
                producto = new()
                {
                    Id = id, Codigo = lector.GetString(1), Nombre = lector.GetString(2), Precio = lector.GetDecimal(3),
                    Stock = lector.GetInt32(4), StockMinimo = lector.GetInt32(5), Activo = lector.GetBoolean(6)
                };
                porId.Add(id, producto); productos.Add(producto);
            }
            if (!lector.IsDBNull(7)) producto.AlternativosIds.Add(lector.GetInt32(7));
        }
        return productos;
    }

    private static Producto ObtenerProducto(SqlConnection c, SqlTransaction? tx, int id)
    {
        using var cmd = new SqlCommand(Consulta + " WHERE p.IdProducto = @id" + Orden, c, tx);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        using var lector = cmd.ExecuteReader();
        return LeerProductos(lector).SingleOrDefault() ?? throw new ArgumentException("El producto no existe.");
    }

    public List<Producto> BuscarProductos(string? busqueda, bool incluirInactivos = false)
    {
        string texto = (busqueda ?? "").Trim();
        if (texto.Length > 100) return new();
        // Buscar texto literal: los signos %, _ y [ no son comodines del usuario.
        string patron = "%" + texto.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        using var c = conexion.CrearConexion(); c.Open();
        using var cmd = new SqlCommand(Consulta + " WHERE (@todos = 1 OR p.Activo = 1) AND (p.Nombre COLLATE Latin1_General_100_CI_AS LIKE @texto ESCAPE N'~' OR p.Codigo LIKE @texto ESCAPE N'~')" + Orden, c);
        cmd.Parameters.Add("@todos", SqlDbType.Bit).Value = incluirInactivos;
        cmd.Parameters.Add("@texto", SqlDbType.NVarChar, 202).Value = patron;
        using var lector = cmd.ExecuteReader();
        return LeerProductos(lector);
    }

    public object ConsultarProducto(int id)
    {
        using var c = conexion.CrearConexion(); c.Open();
        Producto producto = ObtenerProducto(c, null, id);
        using var cmd = new SqlCommand(Consulta + " WHERE p.IdProducto IN (SELECT IdAlternativo FROM vw_AlternativasDisponibles WHERE IdProducto = @id)" + Orden, c);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        using var lector = cmd.ExecuteReader();
        return new { Producto = producto, Alternativas = LeerProductos(lector) };
    }

    public int ContarStockBajo()
    {
        using var c = conexion.CrearConexion(); c.Open();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM vw_StockBajo", c);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private Producto Guardar(string sql, Producto? datos = null, int? id = null, bool? activo = null, int? minimo = null, int? usuarioId = null)
    {
        using var c = conexion.CrearConexion(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            Producto? anterior = null;
            if (id.HasValue)
            {
                using var bloqueo = new SqlCommand("SELECT IdProducto FROM Productos WITH (UPDLOCK,HOLDLOCK) WHERE IdProducto=@id",c,tx);
                bloqueo.Parameters.AddWithValue("@id",id.Value); bloqueo.ExecuteScalar();
                anterior = ObtenerProducto(c,tx,id.Value);
            }
            using var cmd = new SqlCommand(sql, c, tx);
            if (datos != null)
            {
                cmd.Parameters.Add("@codigo", SqlDbType.NVarChar, 20).Value = datos.Codigo;
                cmd.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = datos.Nombre;
                var precio = cmd.Parameters.Add("@precio", SqlDbType.Decimal); precio.Precision = 18; precio.Scale = 2; precio.Value = datos.Precio;
                cmd.Parameters.Add("@stock", SqlDbType.Int).Value = datos.Stock;
                cmd.Parameters.Add("@minimo", SqlDbType.Int).Value = datos.StockMinimo;
            }
            if (id.HasValue) cmd.Parameters.Add("@id", SqlDbType.Int).Value = id.Value;
            if (activo.HasValue) cmd.Parameters.Add("@activo", SqlDbType.Bit).Value = activo.Value;
            if (minimo.HasValue) cmd.Parameters.Add("@minimo", SqlDbType.Int).Value = minimo.Value;
            object? resultado = cmd.ExecuteScalar();
            if (resultado == null) throw new ArgumentException("El producto no existe.");
            Producto producto = ObtenerProducto(c, tx, Convert.ToInt32(resultado));
            if (anterior == null) HistorialProductosSql.Registrar(c,tx,producto.Id,usuarioId,"Registro inicial","Alta del producto.",null,0,null);
            else {
                if (anterior.Precio != producto.Precio) HistorialProductosSql.Registrar(c,tx,producto.Id,usuarioId,"Precio","Cambio de precio de venta.",null,anterior.Stock,anterior.Precio);
                if (anterior.Activo != producto.Activo) HistorialProductosSql.Registrar(c,tx,producto.Id,usuarioId,"Estado",producto.Activo ? "Producto activado." : "Producto desactivado.",null,anterior.Stock,anterior.Precio);
            }
            tx.Commit();
            return producto;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new ArgumentException("Ya existe un producto con ese código, incluso si está inactivo.");
        }
    }

    public Producto RegistrarProducto(RegistroProductoSolicitud solicitud, int? usuarioId = null)
    {
        Producto datos = ProductosValidacion.Validar(solicitud);
        if (solicitud.StockInicial < 0) throw new ArgumentException("El stock inicial no puede ser negativo.");
        datos.Stock = solicitud.StockInicial;
        return Guardar("INSERT INTO Productos (Codigo, Nombre, PrecioVenta, Stock, StockMinimo) OUTPUT INSERTED.IdProducto VALUES (@codigo, @nombre, @precio, @stock, @minimo)", datos, usuarioId: usuarioId);
    }

    public Producto EditarProducto(int id, ProductoSolicitud solicitud, int? usuarioId = null) => Guardar(
        "UPDATE Productos SET Codigo = @codigo, Nombre = @nombre, PrecioVenta = @precio, StockMinimo = @minimo OUTPUT INSERTED.IdProducto WHERE IdProducto = @id",
        ProductosValidacion.Validar(solicitud), id, usuarioId: usuarioId);

    public Producto CambiarEstadoProducto(int id, EstadoProductoSolicitud solicitud, int? usuarioId = null) => Guardar(
        "UPDATE Productos SET Activo = @activo OUTPUT INSERTED.IdProducto WHERE IdProducto = @id",
        id: id, activo: solicitud.Activo ?? throw new ArgumentException("Indica si deseas activar o desactivar el producto."), usuarioId: usuarioId);

    public Producto ActualizarStockMinimo(StockMinimoSolicitud solicitud)
    {
        if (solicitud.StockMinimo < 0) throw new ArgumentException("El stock mínimo no puede ser negativo.");
        return Guardar("UPDATE Productos SET StockMinimo = @minimo OUTPUT INSERTED.IdProducto WHERE IdProducto = @id",
            id: solicitud.ProductoId, minimo: solicitud.StockMinimo);
    }

    public Producto RegistrarAlternativas(AlternativasSolicitud solicitud)
    {
        List<int> ids = (solicitud.AlternativosIds ?? new()).Distinct().Order().ToList();
        if (ids.Contains(solicitud.ProductoId)) throw new ArgumentException("Un producto no puede ser su propia alternativa.");
        using var c = conexion.CrearConexion(); c.Open();
        using var tx = c.BeginTransaction();
        // Bloquear los productos por identificador para coordinar cambios de estado
        // y reemplazos simultaneos. Validar todo antes de borrar las relaciones previas.
        foreach (int id in ids.Append(solicitud.ProductoId).Order())
        {
            using var validar = new SqlCommand("SELECT Activo FROM Productos WITH (UPDLOCK, HOLDLOCK) WHERE IdProducto = @id", c, tx);
            validar.Parameters.Add("@id", SqlDbType.Int).Value = id;
            object? activo = validar.ExecuteScalar();
            if (activo == null) throw new ArgumentException("El producto no existe.");
            if (!(bool)activo) throw new ArgumentException("El producto está inactivo. Actívalo desde Inventario para utilizarlo.");
        }
        using (var borrar = new SqlCommand("DELETE FROM ProductosAlternativos WHERE IdProducto = @id", c, tx))
        {
            borrar.Parameters.Add("@id", SqlDbType.Int).Value = solicitud.ProductoId;
            borrar.ExecuteNonQuery();
        }
        foreach (int id in ids)
        {
            using var insertar = new SqlCommand("INSERT INTO ProductosAlternativos (IdProducto, IdAlternativo) VALUES (@original, @alternativo)", c, tx);
            insertar.Parameters.Add("@original", SqlDbType.Int).Value = solicitud.ProductoId;
            insertar.Parameters.Add("@alternativo", SqlDbType.Int).Value = id;
            insertar.ExecuteNonQuery();
        }
        Producto producto = ObtenerProducto(c, tx, solicitud.ProductoId);
        tx.Commit();
        return producto;
    }
    public object Historial(int id, DateOnly? desde, DateOnly? hasta, string? tipo)
    {
        if (hasta == DateOnly.MaxValue) throw new ArgumentException("Selecciona una fecha final menor a 31/12/9999.");
        if (desde > hasta) throw new ArgumentException("La fecha inicial no puede superar la final.");
        using var c = conexion.CrearConexion(); c.Open();
        var producto = ObtenerProducto(c,null,id);
        using var existe = new SqlCommand("SELECT OBJECT_ID('dbo.HistorialProductos','U')",c);
        if (existe.ExecuteScalar() is DBNull) throw new ArgumentException("Ejecuta primero el script 03_Historial_Productos.sql en tu base de datos.");
        using var cmd = new SqlCommand("""
            SELECT h.IdHistorial,h.Fecha,h.Tipo,h.Descripcion,u.Nombre,h.Referencia,h.StockAnterior,h.StockNuevo,h.PrecioAnterior,h.PrecioNuevo
            FROM HistorialProductos h LEFT JOIN Usuarios u ON u.IdUsuario=h.IdUsuario
            WHERE h.IdProducto=@id AND (@desde IS NULL OR h.Fecha>=@desde)
              AND (@hasta IS NULL OR h.Fecha<@hasta) AND (@tipo IS NULL OR h.Tipo=@tipo)
            ORDER BY h.Fecha DESC,h.IdHistorial DESC
            """,c);
        cmd.Parameters.AddWithValue("@id",id);
        cmd.Parameters.Add("@desde",SqlDbType.DateTimeOffset).Value = desde.HasValue ? new DateTimeOffset(desde.Value.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-5)) : DBNull.Value;
        cmd.Parameters.Add("@hasta",SqlDbType.DateTimeOffset).Value = hasta.HasValue ? new DateTimeOffset(hasta.Value.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-5)).AddDays(1) : DBNull.Value;
        cmd.Parameters.Add("@tipo",SqlDbType.NVarChar,30).Value = string.IsNullOrWhiteSpace(tipo) ? DBNull.Value : tipo;
        var filas = new List<object>();
        using var lector=cmd.ExecuteReader();
        while(lector.Read()) filas.Add(new {
            Id=lector.GetInt64(0), Fecha=lector.GetDateTimeOffset(1), Tipo=lector.GetString(2), Descripcion=lector.GetString(3),
            Usuario=lector.IsDBNull(4)?"Sin usuario registrado":lector.GetString(4), Referencia=lector.IsDBNull(5)?"—":lector.GetString(5),
            StockAnterior=lector.IsDBNull(6)?(int?)null:lector.GetInt32(6), StockNuevo=lector.IsDBNull(7)?(int?)null:lector.GetInt32(7),
            PrecioAnterior=lector.IsDBNull(8)?(decimal?)null:lector.GetDecimal(8), PrecioNuevo=lector.IsDBNull(9)?(decimal?)null:lector.GetDecimal(9)
        });
        return new { Producto=producto, Movimientos=filas };
    }
}
