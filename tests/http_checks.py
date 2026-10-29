"""Comprobaciones contra la aplicación MVC real que se ejecuta en CI."""
import html
import http.cookiejar
import json
import re
import time
import urllib.error
import urllib.request
import urllib.parse
import csv
import io
from pathlib import Path
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

def extraer_token(pagina):
    return html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^\"]+)"', pagina).group(1))

def iniciar_sesion(usuario, clave):
    navegador = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    pagina = navegador.open(base + '/Cuenta/Login').read().decode()
    datos = urllib.parse.urlencode({'Login': usuario, 'Password': clave, '__RequestVerificationToken': extraer_token(pagina)}).encode()
    resultado = navegador.open(urllib.request.Request(base + '/Cuenta/Login', data=datos))
    if '/Cuenta/Login' in resultado.url:
        raise AssertionError('No se pudo iniciar sesión de prueba.')
    return navegador, extraer_token(resultado.read().decode())

opener, token = iniciar_sesion('admin', 'Giovanna2026!')
comprobaciones = 0

def check(condicion, mensaje):
    global comprobaciones
    if not condicion:
        raise AssertionError(mensaje)
    comprobaciones += 1

def api(ruta, datos=None, esperado=200, csrf=True, conexion=None, raiz="/api/tienda/"):
    navegador, verificacion = conexion or (opener, token)
    headers = {}
    cuerpo = None
    if datos is not None:
        cuerpo = json.dumps(datos).encode()
        headers['Content-Type'] = 'application/json'
        if csrf:
            headers['RequestVerificationToken'] = verificacion
    request = urllib.request.Request(base + raiz + ruta, data=cuerpo, headers=headers)
    try:
        respuesta = navegador.open(request)
    except urllib.error.HTTPError as error:
        respuesta = error
    contenido = respuesta.read().decode()
    check(respuesta.code == esperado, f'{ruta}: esperaba {esperado}, recibió {respuesta.code}: {contenido}')
    return json.loads(contenido) if contenido else None

for vista in ['Ventas', 'Inventario', 'Compras', 'Clientes', 'Reportes', 'Proveedores', 'Usuarios']:
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
api('compras', {'proveedorId': 1, 'items': [{'productoId': 6, 'cantidad': 2, 'costoUnitario': 1}]})
check(api('productos?busqueda=P006')[0]['stock'] == 2, 'Compra repone inventario.')
api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 3, 'items': [{'productoId': 6, 'cantidad': 2}]})
hoy = datetime.now(timezone(timedelta(hours=-5))).date().isoformat()
reporte = api('reporte?desde=' + hoy + '&hasta=' + hoy)
check(reporte['cantidadVentas'] == 4 and reporte['totalVendido'] == 18.7, 'RF06: reporte por periodo.')
check(reporte['cobradoEnVentas'] == 14.5 and reporte['totalFiado'] == 4.2 and reporte['abonos'] == 16.7, 'RF06: no sumar fiados como dinero cobrado.')
api('reporte?desde=2026-12-31&hasta=2026-01-01', esperado=400)
# Gestión de productos a través de los controladores y sus validaciones.
nuevo = {'codigo': ' p008 ', 'nombre': 'Atún en lata', 'precio': 6.5, 'stockInicial': 20, 'stockMinimo': 5}
api('productos', nuevo, esperado=400, csrf=False)
atun = api('productos', nuevo)
pid = str(atun['id'])
check(atun['codigo'] == 'P008' and atun['stock'] == 20 and atun['activo'], 'Registrar y normalizar producto.')
api('productos', nuevo, esperado=400)
for campo, valor in [('nombre', '   '), ('codigo', ''), ('precio', 0), ('precio', -1), ('stockInicial', -1), ('stockMinimo', -1), ('codigo', 'X' * 21), ('nombre', 'X' * 101)]:
    api('productos', {**nuevo, 'codigo': 'P009', campo: valor}, esperado=400)
venta_atun = api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 20, 'items': [{'productoId': atun['id'], 'cantidad': 2}]})
edicion = {'codigo': 'P008', 'nombre': 'Atún premium', 'precio': 7, 'stockMinimo': 3, 'stock': 999, 'stockInicial': 999}
api('productos/' + pid + '/editar', edicion, esperado=400, csrf=False)
editado = api('productos/' + pid + '/editar', edicion)
check(editado['stock'] == 18 and editado['precio'] == 7, 'Editar no modifica existencias mediante campos adicionales.')
historial = api('ventas/' + str(venta_atun['id']))
check(historial['total'] == 13 and historial['detalles'][0]['nombre'] == 'Atún en lata' and historial['detalles'][0]['precioUnitario'] == 6.5, 'Conservar detalle anterior a edición.')
api('productos/' + pid + '/editar', {**edicion, 'codigo': 'P001'}, esperado=400)
api('productos/' + pid + '/estado', {'activo': False}, esperado=400, csrf=False)
api('productos/' + pid + '/estado', {}, esperado=400)
api('productos/' + pid + '/estado', {'activo': False})
check(api('productos?busqueda=P008') == [], 'No mostrar inactivos en ventas y compras.')
check(not api('productos?busqueda=P008&incluirInactivos=true')[0]['activo'], 'Inactivos disponibles en administración.')
api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 20, 'items': [{'productoId': atun['id'], 'cantidad': 1}]}, esperado=400)
api('compras', {'proveedorId': 1, 'items': [{'productoId': atun['id'], 'cantidad': 1, 'costoUnitario': 1}]}, esperado=400)
api('productos', nuevo, esperado=400)
api('productos/' + pid + '/estado', {'activo': True})
check(api('productos?busqueda=P008')[0]['stock'] == 18, 'Reactivar conserva stock.')
api('productos/9999/editar', edicion, esperado=400)
api('productos/9999/estado', {'activo': False}, esperado=400)

# Clientes y proveedores: identificadores únicos y conservación de historia.
cliente_datos = {'documento': '33333333', 'nombre': 'Cliente HTTP', 'telefono': '999444555', 'direccion': 'Lima'}
cliente = api('clientes', cliente_datos)
cid = str(cliente['id'])
check(cliente['deuda'] == 0, 'Cliente nuevo sin deuda.')
api('clientes', cliente_datos, esperado=400)
api('clientes', {**cliente_datos, 'documento': '123'}, esperado=400)
api('ventas', {'formaPago': 'Fiado', 'clienteId': cliente['id'], 'items': [{'productoId': 5, 'cantidad': 1}]})
editado_cliente = api('clientes/' + cid + '/editar', {**cliente_datos, 'nombre': 'Cliente actualizado', 'deuda': 999})
check(editado_cliente['deuda'] == 2.5, 'No editar deudas mediante el formulario de clientes.')
api('clientes/' + cid + '/estado', {'activo': False})
api('ventas', {'formaPago': 'Fiado', 'clienteId': cliente['id'], 'items': [{'productoId': 5, 'cantidad': 1}]}, esperado=400)
api('pagos', {'clienteId': cliente['id'], 'monto': 2.5, 'metodo': 'Efectivo'})
api('clientes/' + cid + '/estado', {'activo': True})

proveedor_datos = {'ruc': '20999999999', 'razonSocial': 'Proveedor HTTP', 'correo': 'prueba@example.com'}
proveedor = api('proveedores', proveedor_datos)
prid = str(proveedor['id'])
api('proveedores', proveedor_datos, esperado=400)
api('proveedores', {**proveedor_datos, 'ruc': '20888888888', 'correo': 'incorrecto'}, esperado=400)
stock1 = api('productos?busqueda=P001')[0]['stock']
stock2 = api('productos?busqueda=P002')[0]['stock']
lineas = [{'productoId': 1, 'cantidad': 2, 'costoUnitario': 3}, {'productoId': 2, 'cantidad': 3, 'costoUnitario': 2}]
compra_multiple = api('compras', {'proveedorId': proveedor['id'], 'items': lineas})
check(compra_multiple['total'] == 12 and len(compra_multiple['detalles']) == 2, 'Compra múltiple con subtotal y total.')
check(api('productos?busqueda=P001')[0]['stock'] == stock1 + 2 and api('productos?busqueda=P002')[0]['stock'] == stock2 + 3, 'Reponer varios productos.')
api('compras', {'proveedorId': proveedor['id'], 'items': [lineas[0], {**lineas[1], 'productoId': 999}]}, esperado=400)
check(api('productos?busqueda=P001')[0]['stock'] == stock1 + 2, 'Compra inválida no repone parcialmente.')
api('compras', {'proveedorId': proveedor['id'], 'items': [lineas[0], lineas[0]]}, esperado=400)
api('proveedores/' + prid + '/editar', {**proveedor_datos, 'razonSocial': 'Proveedor actualizado'})
check(api('compras/' + str(compra_multiple['id']))['proveedorNombre'] == 'Proveedor HTTP', 'Compra conserva proveedor original.')
api('proveedores/' + prid + '/estado', {'activo': False})
api('compras', {'proveedorId': proveedor['id'], 'items': lineas}, esperado=400)

# Descargas reales: contenido, filtro de fechas y PDF con texto español.
salida = Path('/tmp/giovanna-qa')
salida.mkdir(exist_ok=True)
for formato in ['csv', 'pdf']:
    respuesta = opener.open(base + '/api/tienda/reporte/exportar/' + formato + '?desde=' + hoy + '&hasta=' + hoy)
    contenido = respuesta.read()
    check('attachment' in respuesta.headers['Content-Disposition'], 'Reporte descargable.')
    if formato == 'csv':
        filas = list(csv.reader(io.StringIO(contenido.decode('utf-8-sig')), delimiter=';'))
        check(any(f[0] == 'Abono' for f in filas) and any(f[0] == 'Venta' for f in filas), 'CSV contiene ventas y pagos.')
    else:
        check(contenido.startswith(b'%PDF-'), 'PDF real desde la API.')
    (salida / ('reporte_http.' + formato)).write_bytes(contenido)
vacio = opener.open(base + '/api/tienda/reporte/exportar/csv?desde=2000-01-01&hasta=2000-01-01').read().decode('utf-8-sig')
check(not any(f[0] == 'Venta' for f in csv.reader(io.StringIO(vacio), delimiter=';')), 'CSV respeta el periodo.')
api('reporte/exportar/csv?desde=2026-12-31&hasta=2026-01-01', esperado=400)

# Autorización real: la cuenta de caja consulta y cobra, sin acceso administrativo.
caja = iniciar_sesion('cajero', 'Cajero2026!')
api('productos', conexion=caja)
api('clientes', conexion=caja)
api('productos', nuevo, esperado=403, conexion=caja)
api('productos/' + pid + '/editar', edicion, esperado=403, conexion=caja)
api('stock-minimo', {'productoId': 1, 'stockMinimo': 1}, esperado=403, conexion=caja)
api('clientes', cliente_datos, esperado=403, conexion=caja)
api('proveedores', proveedor_datos, esperado=403, conexion=caja)
api('compras', {'proveedorId': 1, 'items': lineas}, esperado=403, conexion=caja)
api('compras', esperado=403, conexion=caja)
api('', esperado=403, conexion=caja, raiz='/api/usuarios')
api('ventas', {'formaPago': 'Efectivo', 'montoRecibido': 10, 'items': [{'productoId': 5, 'cantidad': 1}]}, conexion=caja)
api('pagos', {'clienteId': 2, 'monto': 1, 'metodo': 'Efectivo'}, conexion=caja)

# Usuarios, cambio de rol y revocación de cookies existentes.
usuario_datos = {'login': 'auxiliar', 'nombre': 'Auxiliar HTTP', 'rol': 'Cajero', 'password': 'Prueba2026!'}
usuario = api('', usuario_datos, raiz='/api/usuarios')
uid = str(usuario['id'])
api('', usuario_datos, esperado=400, raiz='/api/usuarios')
usuarios = api('', raiz='/api/usuarios')
check(all('passwordHash' not in u and 'password' not in u for u in usuarios), 'No exponer contraseñas ni hashes.')
api('/1/editar', {'login': 'admin', 'nombre': 'Admin', 'rol': 'Cajero', 'activo': True}, esperado=400, raiz='/api/usuarios')
anterior = iniciar_sesion('auxiliar', 'Prueba2026!')
api('/' + uid + '/editar', {**usuario_datos, 'rol': 'Administrador', 'activo': True, 'password': None}, raiz='/api/usuarios')
api('productos', esperado=401, conexion=anterior)
nueva = iniciar_sesion('auxiliar', 'Prueba2026!')
api('', conexion=nueva, raiz='/api/usuarios')
api('/' + uid + '/editar', {**usuario_datos, 'activo': False, 'password': None}, raiz='/api/usuarios')
api('productos', esperado=401, conexion=nueva)
anonimo = (urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar())), '')
api('productos', esperado=401, conexion=anonimo)
api('productos', nuevo, esperado=401, conexion=anonimo)
print(f'OK: {comprobaciones} comprobaciones HTTP superadas.')

