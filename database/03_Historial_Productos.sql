-- Ejecutar en BodegaGiovanna. No borra tablas ni registros existentes.
-- Instalar con la aplicacion detenida antes de utilizar la nueva version.
USE BodegaGiovanna;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.HistorialProductos','U') IS NULL
BEGIN
 CREATE TABLE dbo.HistorialProductos (
  IdHistorial bigint IDENTITY PRIMARY KEY,
  IdProducto int NOT NULL REFERENCES dbo.Productos(IdProducto),
  Fecha datetimeoffset(0) NOT NULL DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'),
  Tipo nvarchar(30) NOT NULL,
  Descripcion nvarchar(500) NOT NULL,
  IdUsuario int NULL REFERENCES dbo.Usuarios(IdUsuario),
  Referencia nvarchar(60) NULL,
  StockAnterior int NULL, StockNuevo int NULL,
  PrecioAnterior decimal(18,2) NULL, PrecioNuevo decimal(18,2) NULL
 );
 CREATE INDEX IX_HistorialProductos_ProductoFecha ON dbo.HistorialProductos(IdProducto,Fecha DESC,IdHistorial DESC);
 -- Compras y ventas antiguas: se conocen cantidades, no el stock exacto de cada instante.
 INSERT dbo.HistorialProductos(IdProducto,Fecha,Tipo,Descripcion,IdUsuario,Referencia)
 SELECT d.IdProducto,c.Fecha,N'Compra',CONCAT(N'Entrada de ',d.Cantidad,N' unidades (movimiento anterior a la instalación).'),c.IdUsuario,CONCAT(N'Compra N.º ',c.IdCompra)
 FROM dbo.DetalleCompras d JOIN dbo.Compras c ON c.IdCompra=d.IdCompra;
 INSERT dbo.HistorialProductos(IdProducto,Fecha,Tipo,Descripcion,IdUsuario,Referencia)
 SELECT d.IdProducto,v.Fecha,N'Venta',CONCAT(N'Salida de ',d.Cantidad,N' unidades (movimiento anterior a la instalación).'),v.IdUsuario,CONCAT(N'Venta N.º ',v.IdVenta)
 FROM dbo.DetalleVentas d JOIN dbo.Ventas v ON v.IdVenta=d.IdVenta;
 INSERT dbo.HistorialProductos(IdProducto,Tipo,Descripcion,StockNuevo,PrecioNuevo)
 SELECT IdProducto,N'Inicio de seguimiento',N'Valores actuales al instalar el historial; no representa el alta original.',Stock,PrecioVenta FROM dbo.Productos;
END;
COMMIT;
PRINT N'Historial de productos instalado. Los registros existentes se conservaron.';
GO
