"""Comprobaciones contra la aplicación MVC real que se ejecuta en CI."""
import html
import http.cookiejar
import json
import re
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

base = 'http://127.0.0.1:5099'
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
for intento in range(60):
    try:
        inicio = opener.open(base, timeout=2).read().decode()
        break
    except (OSError, urllib.error.URLError):
        time.sleep(0.5)
else:
    raise RuntimeError('La aplicación no inició. Revisa el registro del servidor.')

token = html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', inicio).group(1))
comprobaciones = 0

def check(condicion, mensaje):
    global comprobaciones
    if not condicion:
        raise AssertionError(mensaje)
    comprobaciones += 1

def api(ruta, datos=None, esperado=200, csrf=True):
    headers = {}
    cuerpo = None
    if datos is not None:
        cuerpo = json.dumps(datos).encode()
        headers['Content-Type'] = 'application/json'
        if csrf:
            headers['RequestVerificationToken'] = token
    request = urllib.request.Request(base + '/api/tienda/' + ruta, data=cuerpo, headers=headers)
    try:
        respuesta = opener.open(request)
    except urllib.error.HTTPError as error:
        respuesta = error
    contenido = respuesta.read().decode()
    check(respuesta.code == esperado, f'{ruta}: esperaba {esperado}, recibió {respuesta.code}: {contenido}')
    return json.loads(contenido) if contenido else None

for vista in ['Ventas', 'Inventario', 'Compras', 'Clientes', 'Reportes']:
    respuesta = opener.open(base + '/Home/' + vista)
    check(respuesta.code == 200, 'La vista ' + vista + ' debe abrir.')

productos = api('productos?busqueda=P001')
check(len(productos) == 1 and productos[0]['id'] == 1, 'RF01: buscar por código.')
api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 10, 'items': [{'productoId': 1, 'cantidad': 2}]}, esperado=400, csrf=False)
venta = api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 10, 'items': [{'productoId': 1, 'cantidad': 2}]})
check(venta['total'] == 9 and venta['vuelto'] == 1, 'RF08: total y vuelto.')
detalle = api('ventas/' + str(venta['id']))
check(detalle['detalles'][0]['cantidad'] == 2 and detalle['fecha'], 'RF03: detalle y fecha de la venta.')
api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 1000, 'items': [{'productoId': 1, 'cantidad': 100}]}, esperado=400)
check(api('productos?busqueda=P001')[0]['stock'] == 16, 'Una venta rechazada no modifica stock.')
api('ventas', {'formaPago': 'Fiado', 'clienteId': 1, 'items': [{'productoId': 2, 'cantidad': 1}]})
pago = api('pagos', {'clienteId': 1, 'monto': 6.7, 'metodo': 'Efectivo'})
check(pago['saldoRestante'] == 10, 'RF04: pago parcial.')
api('pagos', {'clienteId': 1, 'monto': 10, 'metodo': 'Digital'})
check(len(api('pagos?clienteId=1')) == 2, 'RF04: historial de pagos.')
api('pagos', {'clienteId': 1, 'monto': 0.01, 'metodo': 'Efectivo'}, esperado=400)
api('ventas', {'formaPago': 'Digital', 'items': [{'productoId': 5, 'cantidad': 1}]}, esperado=400)
api('ventas', {'formaPago': 'Digital', 'referenciaPago': 'OPERACION-HTTP', 'items': [{'productoId': 5, 'cantidad': 1}]})
api('stock-minimo', {'productoId': 2, 'stockMinimo': 0})
check(not api('productos?busqueda=P002')[0]['stockBajo'], 'RF05: mínimo configurable.')
api('alternativas', {'productoId': 6, 'alternativosIds': [7]})
check(api('productos/6')['alternativas'][0]['id'] == 7, 'RF07: alternativa disponible.')
api('alternativas', {'productoId': 6, 'alternativosIds': [6]}, esperado=400)
api('compras', {'proveedor': 'Prueba HTTP', 'productoId': 6, 'cantidad': 2, 'costoUnitario': 1})
check(api('productos?busqueda=P006')[0]['stock'] == 2, 'Compra repone inventario.')
api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 3, 'items': [{'productoId': 6, 'cantidad': 2}]})
hoy = datetime.now(timezone(timedelta(hours=-5))).date().isoformat()
reporte = api('reporte?desde=' + hoy + '&hasta=' + hoy)
check(reporte['cantidadVentas'] == 4 and reporte['totalVendido'] == 18.7, 'RF06: reporte por periodo.')
check(reporte['cobradoEnVentas'] == 14.5 and reporte['totalFiado'] == 4.2 and reporte['abonos'] == 16.7, 'RF06: no sumar fiados como dinero cobrado.')
api('reporte?desde=2026-12-31&hasta=2026-01-01', esperado=400)
print(f'OK: {comprobaciones} comprobaciones HTTP superadas.')
