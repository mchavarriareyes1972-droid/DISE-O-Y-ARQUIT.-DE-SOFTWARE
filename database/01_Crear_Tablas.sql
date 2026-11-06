-- BODEGA GIOVANNA - MICROSOFT SQL SERVER
-- Solo estructura: crear la base, las 11 tablas y las 3 vistas.
-- No incluye INSERT ni datos de ejemplo.
-- SQL Server puede mostrar el esquema predeterminado en su explorador.
-- Ejecutar una sola vez en una instancia donde BodegaGiovanna no exista.
-- Si ya ejecutaste el script anterior, las tablas ya estan creadas.

-- CREAR Y SELECCIONAR LA BASE DE DATOS
CREATE DATABASE BodegaGiovanna;
GO

USE BodegaGiovanna;
GO

-- TABLA 1: Roles
CREATE TABLE Roles (
    IdRol tinyint NOT NULL,
    Nombre nvarchar(20) NOT NULL,
    PRIMARY KEY (IdRol),
    UNIQUE (Nombre),
    CHECK (
        (IdRol = 1 AND Nombre = N'Administrador') OR (IdRol = 2 AND Nombre = N'Cajero'))
);
GO

-- TABLA 2: Usuarios
CREATE TABLE Usuarios (
    IdUsuario int IDENTITY(1,1) NOT NULL,
    Nombre nvarchar(100) NOT NULL,
    Login varchar(30) COLLATE Latin1_General_100_CI_AS NOT NULL,
    PasswordHash nvarchar(512) NOT NULL,
    IdRol tinyint NOT NULL,
    Activo bit NOT NULL DEFAULT (1),
    Version int NOT NULL DEFAULT (1),
    Intentos int NOT NULL DEFAULT (0),
    BloqueadoHasta datetimeoffset(0) NULL,
    PRIMARY KEY (IdUsuario),
    UNIQUE (Login),
    FOREIGN KEY (IdRol) REFERENCES Roles(IdRol),
    CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
    CHECK (
        LEN(Login) BETWEEN 3 AND 30 AND Login COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-zA-Z0-9_.-]%'),
    CHECK (LEN(LTRIM(RTRIM(PasswordHash))) > 0),
    CHECK (Version > 0),
    CHECK (Intentos >= 0)
);
GO

-- TABLA 3: Clientes
CREATE TABLE Clientes (
    IdCliente int IDENTITY(1,1) NOT NULL,
    Documento varchar(8) NOT NULL,
    Nombre nvarchar(100) NOT NULL,
    Telefono nvarchar(20) NULL,
    Direccion nvarchar(150) NULL,
    SaldoInicial decimal(18,2) NOT NULL DEFAULT (0),
    Activo bit NOT NULL DEFAULT (1),
    PRIMARY KEY (IdCliente),
    UNIQUE (Documento),
    CHECK (
        DATALENGTH(Documento) = 8 AND Documento COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'),
    CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
    CHECK (SaldoInicial >= 0)
);
GO

-- TABLA 4: Proveedores
CREATE TABLE Proveedores (
    IdProveedor int IDENTITY(1,1) NOT NULL,
    Ruc varchar(11) NOT NULL,
    RazonSocial nvarchar(100) NOT NULL,
    Contacto nvarchar(100) NULL,
    Telefono nvarchar(20) NULL,
    Correo nvarchar(100) NULL,
    Direccion nvarchar(150) NULL,
    Activo bit NOT NULL DEFAULT (1),
    PRIMARY KEY (IdProveedor),
    UNIQUE (Ruc),
    CHECK (
        DATALENGTH(Ruc) = 11 AND Ruc COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'),
    CHECK (LEN(LTRIM(RTRIM(RazonSocial))) > 0)
);
GO

-- TABLA 5: Productos
CREATE TABLE Productos (
    IdProducto int IDENTITY(1,1) NOT NULL,
    Codigo nvarchar(20) COLLATE Latin1_General_100_CI_AS NOT NULL,
    Nombre nvarchar(100) NOT NULL,
    Categoria nvarchar(50) NULL,
    PrecioVenta decimal(18,2) NOT NULL,
    Stock int NOT NULL DEFAULT (0),
    StockMinimo int NOT NULL DEFAULT (5),
    FechaVencimiento date NULL,
    Activo bit NOT NULL DEFAULT (1),
    PRIMARY KEY (IdProducto),
    UNIQUE (Codigo),
    CHECK (
        DATALENGTH(Codigo) = DATALENGTH(LTRIM(RTRIM(Codigo))) AND LEN(Codigo) > 0),
    CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
    CHECK (PrecioVenta > 0),
    CHECK (Stock >= 0 AND StockMinimo >= 0)
);
GO

-- TABLA 6: ProductosAlternativos
CREATE TABLE ProductosAlternativos (
    IdProducto int NOT NULL,
    IdAlternativo int NOT NULL,
    PRIMARY KEY (IdProducto, IdAlternativo),
    FOREIGN KEY (IdProducto) REFERENCES Productos(IdProducto),
    FOREIGN KEY (IdAlternativo) REFERENCES Productos(IdProducto),
    CHECK (IdProducto <> IdAlternativo)
);
GO

-- TABLA 7: Ventas
CREATE TABLE Ventas (
    IdVenta int IDENTITY(1,1) NOT NULL,
    Fecha datetimeoffset(0) NOT NULL DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
    IdUsuario int NOT NULL,
    IdCliente int NULL,
    ClienteNombre nvarchar(100) NULL,
    FormaPago nvarchar(10) NOT NULL,
    ReferenciaPago nvarchar(100) NULL,
    Total decimal(18,2) NOT NULL,
    MontoRecibido decimal(18,2) NOT NULL,
    Vuelto decimal(18,2) NOT NULL,
    PRIMARY KEY (IdVenta),
    FOREIGN KEY (IdUsuario) REFERENCES Usuarios(IdUsuario),
    FOREIGN KEY (IdCliente) REFERENCES Clientes(IdCliente),
    CHECK (Total > 0 AND MontoRecibido >= 0 AND Vuelto >= 0),
    CHECK (FormaPago IN (N'Efectivo', N'Digital', N'Fiado')),
    CHECK (
        (FormaPago = N'Efectivo' AND MontoRecibido >= Total AND Vuelto = MontoRecibido - Total)
        OR (FormaPago = N'Digital' AND MontoRecibido = Total AND Vuelto = 0 AND ReferenciaPago IS NOT NULL AND LEN(LTRIM(RTRIM(ReferenciaPago))) > 0)
        OR (FormaPago = N'Fiado' AND MontoRecibido = 0 AND Vuelto = 0 AND IdCliente IS NOT NULL AND ClienteNombre IS NOT NULL AND LEN(LTRIM(RTRIM(ClienteNombre))) > 0)
    )
);
GO

-- TABLA 8: DetalleVentas
CREATE TABLE DetalleVentas (
    IdDetalleVenta int IDENTITY(1,1) NOT NULL,
    IdVenta int NOT NULL,
    IdProducto int NOT NULL,
    CodigoProducto nvarchar(20) NOT NULL,
    NombreProducto nvarchar(100) NOT NULL,
    Cantidad int NOT NULL,
    PrecioUnitario decimal(18,2) NOT NULL,
    Subtotal AS CONVERT(decimal(18,2), Cantidad * PrecioUnitario) PERSISTED,
    PRIMARY KEY (IdDetalleVenta),
    UNIQUE (IdVenta, IdProducto),
    FOREIGN KEY (IdVenta) REFERENCES Ventas(IdVenta),
    FOREIGN KEY (IdProducto) REFERENCES Productos(IdProducto),
    CHECK (Cantidad > 0),
    CHECK (PrecioUnitario > 0),
    CHECK (LEN(LTRIM(RTRIM(CodigoProducto))) > 0 AND LEN(LTRIM(RTRIM(NombreProducto))) > 0)
);
GO

-- TABLA 9: Compras
CREATE TABLE Compras (
    IdCompra int IDENTITY(1,1) NOT NULL,
    Fecha datetimeoffset(0) NOT NULL DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
    IdUsuario int NOT NULL,
    IdProveedor int NOT NULL,
    ProveedorNombre nvarchar(100) NOT NULL,
    Total decimal(18,2) NOT NULL,
    PRIMARY KEY (IdCompra),
    FOREIGN KEY (IdUsuario) REFERENCES Usuarios(IdUsuario),
    FOREIGN KEY (IdProveedor) REFERENCES Proveedores(IdProveedor),
    CHECK (Total > 0),
    CHECK (LEN(LTRIM(RTRIM(ProveedorNombre))) > 0)
);
GO

-- TABLA 10: DetalleCompras
CREATE TABLE DetalleCompras (
    IdDetalleCompra int IDENTITY(1,1) NOT NULL,
    IdCompra int NOT NULL,
    IdProducto int NOT NULL,
    CodigoProducto nvarchar(20) NOT NULL,
    NombreProducto nvarchar(100) NOT NULL,
    Cantidad int NOT NULL,
    CostoUnitario decimal(18,2) NOT NULL,
    Subtotal AS CONVERT(decimal(18,2), Cantidad * CostoUnitario) PERSISTED,
    PRIMARY KEY (IdDetalleCompra),
    UNIQUE (IdCompra, IdProducto),
    FOREIGN KEY (IdCompra) REFERENCES Compras(IdCompra),
    FOREIGN KEY (IdProducto) REFERENCES Productos(IdProducto),
    CHECK (Cantidad > 0),
    CHECK (CostoUnitario > 0),
    CHECK (LEN(LTRIM(RTRIM(CodigoProducto))) > 0 AND LEN(LTRIM(RTRIM(NombreProducto))) > 0)
);
GO

-- TABLA 11: Pagos
CREATE TABLE Pagos (
    IdPago int IDENTITY(1,1) NOT NULL,
    Fecha datetimeoffset(0) NOT NULL DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
    IdUsuario int NOT NULL,
    IdCliente int NOT NULL,
    ClienteNombre nvarchar(100) NOT NULL,
    Metodo nvarchar(10) NOT NULL,
    Monto decimal(18,2) NOT NULL,
    SaldoRestante decimal(18,2) NOT NULL,
    PRIMARY KEY (IdPago),
    FOREIGN KEY (IdUsuario) REFERENCES Usuarios(IdUsuario),
    FOREIGN KEY (IdCliente) REFERENCES Clientes(IdCliente),
    CHECK (Metodo IN (N'Efectivo', N'Digital')),
    CHECK (Monto > 0 AND SaldoRestante >= 0),
    CHECK (LEN(LTRIM(RTRIM(ClienteNombre))) > 0)
);
GO

-- VISTA 1: DEUDA DE LOS CLIENTES
CREATE VIEW vw_SaldosClientes AS
    SELECT c.IdCliente, c.Documento, c.Nombre, c.Telefono, c.Direccion, c.Activo,
        c.SaldoInicial,
        CONVERT(decimal(18,2), COALESCE(v.TotalFiado, 0)) AS TotalFiado,
        CONVERT(decimal(18,2), COALESCE(p.TotalAbonado, 0)) AS TotalAbonado,
        CONVERT(decimal(18,2), c.SaldoInicial + COALESCE(v.TotalFiado,0) - COALESCE(p.TotalAbonado,0)) AS Deuda
    FROM Clientes c
    LEFT JOIN (SELECT IdCliente, SUM(Total) AS TotalFiado FROM Ventas
        WHERE FormaPago = N'Fiado' GROUP BY IdCliente) v ON v.IdCliente = c.IdCliente
    LEFT JOIN (SELECT IdCliente, SUM(Monto) AS TotalAbonado FROM Pagos
        GROUP BY IdCliente) p ON p.IdCliente = c.IdCliente;
GO

-- VISTA 2: PRODUCTOS CON STOCK BAJO
CREATE VIEW vw_StockBajo AS
    SELECT IdProducto, Codigo, Nombre, PrecioVenta, Stock, StockMinimo
    FROM Productos WHERE Activo = 1 AND Stock <= StockMinimo;
GO

-- VISTA 3: ALTERNATIVAS DISPONIBLES
CREATE VIEW vw_AlternativasDisponibles AS
    SELECT a.IdProducto, p.IdProducto AS IdAlternativo, p.Codigo, p.Nombre, p.PrecioVenta, p.Stock
    FROM ProductosAlternativos a
    INNER JOIN Productos p ON p.IdProducto = a.IdAlternativo
    INNER JOIN Productos original ON original.IdProducto = a.IdProducto
    WHERE p.Activo = 1 AND p.Stock > 0 AND original.Activo = 1;
GO
