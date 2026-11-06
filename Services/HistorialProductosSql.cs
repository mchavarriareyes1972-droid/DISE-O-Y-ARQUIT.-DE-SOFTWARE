using Microsoft.Data.SqlClient;
using System.Data;

namespace Tienda_Giovanna.Services;

internal static class HistorialProductosSql
{
    internal static void Registrar(SqlConnection c, SqlTransaction tx, int id, int? usuario, string tipo,
        string descripcion, string? referencia, int? stockAnterior, decimal? precioAnterior)
    {
        using var cmd = new SqlCommand("""
            INSERT HistorialProductos(IdProducto,Tipo,Descripcion,IdUsuario,Referencia,StockAnterior,StockNuevo,PrecioAnterior,PrecioNuevo)
            SELECT IdProducto,@tipo,@descripcion,@usuario,@referencia,@stock,Stock,@precio,PrecioVenta FROM Productos WHERE IdProducto=@id
            """, c, tx);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@tipo", tipo);
        cmd.Parameters.AddWithValue("@descripcion", descripcion);
        cmd.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@referencia", (object?)referencia ?? DBNull.Value);
        cmd.Parameters.Add("@stock", SqlDbType.Int).Value = (object?)stockAnterior ?? DBNull.Value;
        var precio = cmd.Parameters.Add("@precio", SqlDbType.Decimal); precio.Precision=18; precio.Scale=2; precio.Value=(object?)precioAnterior ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
    internal static void Movimiento(SqlConnection c, SqlTransaction tx, int id, int usuario, int cantidad, string tipo, int referencia)
    {
        using var cmd = new SqlCommand("SELECT Stock,PrecioVenta FROM Productos WHERE IdProducto=@id",c,tx);
        cmd.Parameters.AddWithValue("@id",id);
        int stock; decimal precio;
        using(var lector=cmd.ExecuteReader()) { lector.Read(); stock=lector.GetInt32(0); precio=lector.GetDecimal(1); }
        Registrar(c,tx,id,usuario,tipo,$"{(cantidad > 0 ? "Entrada" : "Salida")} de {Math.Abs(cantidad)} unidades.", $"{tipo} N.º {referencia}",stock-cantidad,precio);
    }
}
