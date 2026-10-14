# Bodega Giovanna

Proyecto académico de ASP.NET Core MVC con C#, HTML5, CSS3 y JavaScript. Requiere el SDK de .NET 8 y puede abrirse con `Tienda_Giovanna.sln` en Visual Studio.

## Ejecutar

Desde la carpeta del proyecto:

```sh
dotnet run --project Tienda_Giovanna.csproj
```

Abre en el navegador la dirección que indique la consola. En Visual Studio también puedes ejecutar con F5.

## Inicio de sesión (datos de práctica)

| Usuario | Contraseña de práctica | Rol |
|---|---|---|
| `admin` | `Giovanna2026!` | Administrador |
| `cajero` | `Cajero2026!` | Cajero |

El administrador tiene acceso total. El cajero registra ventas y abonos y consulta productos, clientes y reportes. No puede administrar productos, clientes, proveedores, compras ni usuarios. Los permisos se comprueban en C#, además de adaptar las pantallas.

Las contraseñas se guardan como hashes mediante `PasswordHasher` de ASP.NET Core. Al modificar un usuario se invalidan sus sesiones. No se puede quitar el último administrador activo. Tras cinco contraseñas incorrectas para una cuenta, el acceso se bloquea cinco minutos.

Estas credenciales son públicas para la demostración local; los usuarios creados y los cambios de contraseña también se restablecen al reiniciar, porque la base de datos sigue pendiente.

## Funciones disponibles

- Gestión de productos en Inventario: registrar, editar, desactivar y reactivar. Los códigos son únicos, incluso para productos inactivos. Las ediciones conservan las existencias y el historial de ventas; el stock inicial solo se ingresa al registrar y las entradas posteriores se hacen en Compras.
- Gestión de clientes: registrar y editar DNI, nombre, teléfono y dirección; activar y desactivar. El DNI es único, la deuda solo cambia con fiados y abonos y un cliente inactivo puede cancelar sus pendientes.
- Gestión de proveedores: registrar y editar RUC, razón social y contacto; activar y desactivar. El RUC es único y debe tener 11 dígitos (no se consulta SUNAT).
- Compras con varios productos: seleccionar un proveedor activo, agregar artículos al detalle con cantidades y costos, registrar una sola compra y consultar su detalle. Si una línea falla, no se modifica ningún stock.
- Exportación del reporte de ventas y abonos en CSV y PDF con las fechas seleccionadas. No se calculan ganancias netas en este avance.
- Gestión de usuarios y roles desde el módulo Usuarios, disponible para el administrador.
- RF01: buscar productos por código o nombre en Ventas y Compras.
- RF02: consultar la ficha del producto desde Ventas o Inventario.
- RF03: consultar el historial y detalle de ventas por periodo, con fechas de Lima.
- RF04: registrar abonos parciales o totales e historial de pagos.
- RF05: avisos de reposición según el stock mínimo configurado para cada producto.
- RF06: reportes por periodo con ventas al contado, fiados y abonos separados.
- RF07: registrar alternativas en Inventario y ofrecer las que tengan stock al buscar un producto agotado en Ventas.
- RF08: C# calcula el vuelto y valida el monto recibido y las cantidades disponibles.

## Organización

- `Models/TiendaModels.cs`: productos, clientes, ventas, detalles, pagos, compras y solicitudes.
- `Services/TiendaService.cs`: reglas de negocio y almacenamiento temporal compartido por la aplicación.
- `Services/GestionService.cs`: segunda parte del mismo servicio para clientes y proveedores.
- `Services/UsuariosService.cs`: cuentas, contraseñas y validación de sesiones.
- `Services/ExportacionService.cs`: genera CSV y PDF desde el mismo reporte consultado.
- `Controllers/TiendaController.cs`: recibe solicitudes de la interfaz y llama al servicio.
- `Views/Home`: pantallas MVC.
- `wwwroot/js/site.js`: presenta las respuestas de C# y envía las operaciones.

Los datos se guardan **en la memoria del servidor**, no en una base de datos. Recargar una pantalla o cambiar de módulo conserva los datos; detener o reiniciar la aplicación los restablece y cierra las sesiones anteriores. Los navegadores conectados a la misma aplicación comparten el inventario de práctica. El siguiente paso es incorporar persistencia en SQL Server y los demás requisitos de la entrega final. La referencia de pago digital se confirma manualmente; no existe integración con Yape o Plin.

La exportación PDF usa QuestPDF 2026.9.1 con licencia Community para este proyecto académico. Visual Studio restaura el paquete NuGet al compilar por primera vez; después la generación se realiza localmente.

## Probar los RF

```sh
dotnet run --project tests/RfChecks/RfChecks.csproj
```

GitHub Actions compila el proyecto y ejecuta las comprobaciones en cada actualización.

## Probar la gestión de productos

1. En Inventario, pulsa **Nuevo producto** y registra `P008`, `Atún en lata`, precio `6.50`, stock inicial `20` y mínimo `5`.
2. Busca el producto en Ventas y registra una venta. Luego edita su nombre y precio desde Inventario: las ventas anteriores deben conservar sus datos originales.
3. Pulsa **Desactivar**: dejará de aparecer en Ventas y Compras. En Inventario marca **Mostrar también productos inactivos** para consultarlo o activarlo otra vez.
4. Intenta registrar un código repetido: el servidor debe rechazarlo sin modificar el catálogo.
