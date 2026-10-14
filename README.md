# Bodega Giovanna

Proyecto académico de ASP.NET Core MVC con C#, HTML5, CSS3 y JavaScript. Requiere el SDK de .NET 8 y puede abrirse con `Tienda_Giovanna.sln` en Visual Studio.

## Ejecutar

Desde la carpeta del proyecto:

```sh
dotnet run --project Tienda_Giovanna.csproj
```

Abre en el navegador la dirección que indique la consola. En Visual Studio también puedes ejecutar con F5.

## Funciones disponibles

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
- `Controllers/TiendaController.cs`: recibe solicitudes de la interfaz y llama al servicio.
- `Views/Home`: pantallas MVC.
- `wwwroot/js/site.js`: presenta las respuestas de C# y envía las operaciones.

Los datos se guardan **en la memoria del servidor**, no en una base de datos. Recargar una pantalla o cambiar de módulo conserva los datos; detener o reiniciar la aplicación los restablece. Los navegadores conectados a la misma aplicación comparten el inventario de práctica. El siguiente paso es incorporar persistencia en SQL Server y los roles y demás requisitos de la entrega final. La referencia de pago digital se confirma manualmente; no existe integración con Yape o Plin.

## Probar los RF

```sh
dotnet run --project tests/RfChecks/RfChecks.csproj
```

GitHub Actions compila el proyecto y ejecuta las comprobaciones en cada actualización.
