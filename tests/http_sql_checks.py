"""Comprueba el acceso web con las credenciales SQL generadas por CI."""
import html
import http.cookiejar
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

base='http://127.0.0.1:5099'
def navegador():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def token(texto):
    return html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^\"]+)"',texto).group(1))
for _ in range(60):
    try:
        urllib.request.urlopen(base+'/Cuenta/Login',timeout=2).close()
        break
    except OSError:
        time.sleep(.5)
else:
    raise AssertionError('La aplicacion no inicio.')
def entrar(login,clave):
    n=navegador()
    pagina=n.open(base+'/Cuenta/Login').read().decode()
    datos=urllib.parse.urlencode({'Login':login,'Password':clave,'__RequestVerificationToken':token(pagina)}).encode()
    respuesta=n.open(urllib.request.Request(base+'/Cuenta/Login',data=datos))
    return n,respuesta.url,respuesta.read().decode()
admin,url,pagina=entrar('admin',os.environ['GIOVANNA_ADMIN_PASSWORD'])
assert '/Cuenta/Login' not in url
usuarios=json.loads(admin.open(base+'/api/usuarios').read())
assert {u['login'] for u in usuarios}=={'admin','cajero'}
assert all('passwordHash' not in u and 'password' not in u for u in usuarios)
caja,url,pagina=entrar('cajero',os.environ['GIOVANNA_CAJERO_PASSWORD'])
assert '/Cuenta/Login' not in url
try:
    caja.open(base+'/api/usuarios')
    raise AssertionError('El cajero obtuvo acceso administrativo.')
except urllib.error.HTTPError as error:
    assert error.code==403
_,url,_=entrar('admin','incorrecta')
assert '/Cuenta/Login' in url
_,url,_=entrar("admin' OR 1=1--",'incorrecta')
assert '/Cuenta/Login' in url
print('OK: acceso web SQL de administrador y cajero, permisos, hashes ocultos y claves incorrectas rechazadas.')

comprobaciones = 0
def verificar(condicion, mensaje):
    global comprobaciones
    assert condicion, mensaje
    comprobaciones += 1
    print('OK: ' + mensaje)

pagina_clientes = admin.open(base + '/Home/Clientes').read().decode()
csrf = token(pagina_clientes)
def api(ruta, datos=None, conexion=admin, esperado=200, con_token=True):
    headers = {}
    if con_token:
        headers['RequestVerificationToken'] = csrf
    if datos is not None:
        headers['Content-Type'] = 'application/json'
    solicitud = urllib.request.Request(base + '/api/tienda/' + ruta,
        data=json.dumps(datos).encode() if datos is not None else None, headers=headers)
    try:
        respuesta = conexion.open(solicitud, timeout=15)
    except urllib.error.HTTPError as error:
        respuesta = error
    verificar(respuesta.code == esperado, f'{solicitud.get_method()} {ruta}: HTTP {esperado}.')
    contenido = respuesta.read()
    return json.loads(contenido) if contenido else None

datos_cliente = {'documento': '99000011', 'nombre': 'Cliente HTTP SQL', 'telefono': '999123456', 'direccion': 'Lima'}
if '--verificar-reinicio' in sys.argv:
    clientes = api('clientes?incluirInactivos=true')
    cliente = next(c for c in clientes if c['documento'] == datos_cliente['documento'])
    verificar(cliente['nombre'] == 'Cliente HTTP editado' and not cliente['activo'], 'Edicion y baja conservadas al reiniciar el servidor web.')
    verificar(not any(c['id'] == cliente['id'] for c in api('clientes')), 'El filtro de activos sigue funcionando despues del reinicio.')
    api(f"clientes/{cliente['id']}/estado", {'activo': True})
    verificar(any(c['id'] == cliente['id'] for c in api('clientes')), 'Reactivacion guardada despues del reinicio.')
else:
    clientes = api('clientes?incluirInactivos=true')
    verificar(len(clientes) == 60, 'La API muestra los 60 clientes cargados en SQL.')
    verificar('data-pendiente="true"' in pagina_clientes, 'La pantalla indica que el registro de nuevos abonos esta pendiente.')
    pagos = api('pagos')
    verificar(len(pagos) == 124, 'La API muestra los 124 pagos de SQL.')
    verificar(all(p['clienteId'] == clientes[0]['id'] for p in api(f"pagos?clienteId={clientes[0]['id']}")), 'Historial filtrado correctamente por cliente SQL.')
    cliente = api('clientes', datos_cliente)
    verificar(cliente['deuda'] == 0 and cliente['activo'], 'Cuenta del cliente registrada sin deuda inicial.')
    ruta = f"clientes/{cliente['id']}"
    api(ruta + '/editar', {**datos_cliente, 'nombre': 'Cliente HTTP editado', 'deuda': 999, 'saldoInicial': 999})
    editado = next(c for c in api('clientes') if c['id'] == cliente['id'])
    verificar(editado['nombre'] == 'Cliente HTTP editado' and editado['deuda'] == 0, 'El formulario edita datos y no permite inyectar deuda ni saldo inicial.')
    api(ruta + '/estado', {'activo': False})
    verificar(not any(c['id'] == cliente['id'] for c in api('clientes')), 'El cliente inactivo no aparece en el listado de activos.')
    api('clientes', datos_cliente, esperado=400)
    api('clientes', {**datos_cliente, 'documento': '123'}, esperado=400)
    api(ruta + '/editar', {**datos_cliente, 'nombre': ' '}, esperado=400)
    api('clientes/2147483647/editar', datos_cliente, esperado=400)
    api('clientes/2147483647/estado', {'activo': False}, esperado=400)
    api(ruta + '/estado', {}, esperado=400)
    api('clientes', datos_cliente, esperado=400, con_token=False)
    api('clientes', datos_cliente, conexion=caja, esperado=403)
    api(ruta + '/editar', datos_cliente, conexion=caja, esperado=403)
    api(ruta + '/estado', {'activo': True}, conexion=caja, esperado=403)
    verificar(len(api('clientes?incluirInactivos=true', conexion=caja)) == 61, 'El cajero puede consultar clientes SQL sin administrarlos.')
    api('pagos', {'clienteId': clientes[0]['id'], 'monto': 1, 'metodo': 'Efectivo'}, esperado=400)
    api('ventas', {'formaPago': 'Fiado', 'clienteId': clientes[0]['id'], 'items': [{'productoId': 1, 'cantidad': 1}]}, esperado=400)
    verificar(len(api('pagos')) == 124, 'Las operaciones pendientes no generan pagos de practica ni modifican el historial SQL.')

datos_proveedor = {'ruc': '20990000011', 'razonSocial': 'Proveedor HTTP SQL', 'contacto': 'Contacto',
    'telefono': '999123123', 'correo': None, 'direccion': 'Lima'}
if '--verificar-reinicio' in sys.argv:
    proveedores = api('proveedores?incluirInactivos=true')
    proveedor = next(p for p in proveedores if p['ruc'] == datos_proveedor['ruc'])
    verificar(proveedor['razonSocial'] == 'Proveedor HTTP editado' and proveedor['correo'] == 'contacto@example.test' and not proveedor['activo'], 'Datos y baja del proveedor se conservan al reiniciar el servidor web.')
    verificar(not any(p['id'] == proveedor['id'] for p in api('proveedores')), 'Proveedor inactivo excluido de la seleccion de Compras despues del reinicio.')
    api(f"proveedores/{proveedor['id']}/estado", {'activo': True})
    verificar(any(p['id'] == proveedor['id'] for p in api('proveedores')), 'Reactivacion del proveedor persistida.')
    api('compras', {'proveedorId': proveedor['id'], 'items': [{'productoId': 1, 'cantidad': 1, 'costoUnitario': 2}]}, esperado=400)
    pagina_compras = html.unescape(admin.open(base + '/Home/Compras').read().decode())
    verificar('El registro de ventas y compras estará disponible' in pagina_compras, 'Compras informa que el registro esta pendiente y protege las existencias SQL.')
else:
    proveedores = api('proveedores?incluirInactivos=true')
    verificar(len(proveedores) == 12, 'La API muestra los 12 proveedores cargados en SQL.')
    proveedor = api('proveedores', datos_proveedor)
    verificar(proveedor['activo'] and proveedor['correo'] == '', 'Proveedor guardado con estado activo y correo opcional.')
    ruta_proveedor = f"proveedores/{proveedor['id']}"
    datos_editados = {**datos_proveedor, 'razonSocial': 'Proveedor HTTP editado', 'contacto': "Ana O'Connor", 'correo': 'contacto@example.test', 'direccion': 'Av. SQL 123'}
    editado = api(ruta_proveedor + '/editar', datos_editados)
    verificar(editado['contacto'] == "Ana O'Connor" and editado['direccion'] == 'Av. SQL 123', 'Edicion de los datos de contacto y direccion guardada.')
    api(ruta_proveedor + '/estado', {'activo': False})
    verificar(not any(p['id'] == proveedor['id'] for p in api('proveedores')), 'Proveedor desactivado no aparece en la seleccion de Compras.')
    verificar(len(api('proveedores?incluirInactivos=true')) == 13, 'El listado completo conserva los proveedores inactivos.')
    api('proveedores', datos_proveedor, esperado=400)
    api(ruta_proveedor + '/editar', {**datos_proveedor, 'ruc': proveedores[0]['ruc']}, esperado=400)
    api('proveedores', {**datos_proveedor, 'ruc': '123'}, esperado=400)
    api('proveedores', {**datos_proveedor, 'ruc': '2099000000A'}, esperado=400)
    api('proveedores', {**datos_proveedor, 'correo': 'invalido'}, esperado=400)
    api(ruta_proveedor + '/editar', {**datos_proveedor, 'razonSocial': ' '}, esperado=400)
    api('proveedores/2147483647/editar', datos_proveedor, esperado=400)
    api('proveedores/2147483647/estado', {'activo': True}, esperado=400)
    api(ruta_proveedor + '/estado', {}, esperado=400)
    api('proveedores', datos_proveedor, esperado=400, con_token=False)
    api('proveedores', conexion=caja, esperado=403)
    api('proveedores', datos_proveedor, conexion=caja, esperado=403)
    api(ruta_proveedor + '/editar', datos_proveedor, conexion=caja, esperado=403)
    api(ruta_proveedor + '/estado', {'activo': True}, conexion=caja, esperado=403)
    api('compras', {'proveedorId': proveedor['id'], 'items': [{'productoId': 1, 'cantidad': 1, 'costoUnitario': 2}]}, esperado=400)
    verificar(not api('compras'), 'Comprar a un proveedor inactivo no registra movimientos de practica.')

datos_producto = {'codigo': 'QA_HTTP_01', 'nombre': 'Producto HTTP 100% [SQL]', 'precio': 6.5, 'stockInicial': 0, 'stockMinimo': 5}
if '--verificar-reinicio' in sys.argv:
    productos = api('productos?incluirInactivos=true')
    producto = next(p for p in productos if p['codigo'] == datos_producto['codigo'])
    verificar(producto['nombre'] == 'Producto HTTP editado' and producto['precio'] == 7.25 and producto['stockMinimo'] == 9 and not producto['activo'], 'Nombre, precio, minimo y baja del producto se conservan al reiniciar.')
    verificar(len(producto['alternativosIds']) == 2, 'Las alternativas del producto tambien se conservan al reiniciar.')
    api(f"productos/{producto['id']}/estado", {'activo': True})
    ficha = api(f"productos/{producto['id']}")
    verificar(len(ficha['alternativas']) == 1 and ficha['alternativas'][0]['codigo'] == 'QA_HTTP_02', 'Al reactivar solo se ofrece el sustituto con stock.')
    api('alternativas', {'productoId': producto['id'], 'alternativosIds': []})
    verificar(not api(f"productos/{producto['id']}")['alternativas'], 'Se pueden limpiar las alternativas despues del reinicio.')
else:
    productos = api('productos?incluirInactivos=true')
    verificar(len(productos) == 120 and any(p['codigo'] == 'P120' for p in productos), 'La API muestra los 120 productos cargados en SQL.')
    verificar(sum(len(p['alternativosIds']) for p in productos) == 56, 'La API carga las relaciones de alternativas existentes.')
    verificar(api('productos?busqueda=p001')[0]['nombre'] == 'Arroz extra bolsa 1 kg', 'Busqueda por codigo SQL sin distinguir mayusculas.')
    verificar(api('reporte')['productosStockBajo'] == 7, 'La alerta del reporte coincide con los siete productos de stock bajo en SQL.')
    producto = api('productos', datos_producto)
    sustituto = api('productos', {**datos_producto, 'codigo': 'QA_HTTP_02', 'nombre': 'Sustituto HTTP con stock', 'stockInicial': 10, 'stockMinimo': 0})
    agotado = api('productos', {**datos_producto, 'codigo': 'QA_HTTP_03', 'nombre': 'Sustituto HTTP agotado', 'stockInicial': 0, 'stockMinimo': 0})
    ruta_producto = f"productos/{producto['id']}"
    verificar(api('productos?busqueda=' + urllib.parse.quote('100% [SQL]'))[0]['id'] == producto['id'], 'Busqueda de caracteres especiales como texto literal.')
    editado = api(ruta_producto + '/editar', {**datos_producto, 'nombre': 'Producto HTTP editado', 'precio': 7.25, 'stockInicial': 999, 'stock': 999})
    verificar(editado['stock'] == 0 and editado['precio'] == 7.25, 'Editar precio no permite inyectar cambios de existencias.')
    api('stock-minimo', {'productoId': producto['id'], 'stockMinimo': 9})
    api('alternativas', {'productoId': producto['id'], 'alternativosIds': [sustituto['id'], sustituto['id'], agotado['id']]})
    ficha = api(ruta_producto)
    verificar(len(ficha['producto']['alternativosIds']) == 2 and len(ficha['alternativas']) == 1, 'Relaciones sin duplicados y sugerencias con stock.')
    api('alternativas', {'productoId': producto['id'], 'alternativosIds': [sustituto['id'], 2147483647]}, esperado=400)
    verificar(len(api(ruta_producto)['producto']['alternativosIds']) == 2, 'Una alternativa invalida no borra la seleccion previa.')
    api('alternativas', {'productoId': producto['id'], 'alternativosIds': [producto['id']]}, esperado=400)
    api(ruta_producto + '/estado', {'activo': False})
    verificar(not api('productos?busqueda=QA_HTTP_01'), 'Producto desactivado no aparece en la lista de venta.')
    api('productos', {**datos_producto, 'codigo': 'qa_http_01'}, esperado=400)
    api(ruta_producto + '/editar', {**datos_producto, 'codigo': 'P001'}, esperado=400)
    api('productos', {**datos_producto, 'codigo': 'QA_ERROR', 'precio': 0}, esperado=400)
    api('productos', {**datos_producto, 'codigo': 'QA_ERROR', 'precio': 10000000000000000}, esperado=400)
    api('productos', {**datos_producto, 'codigo': 'QA_ERROR', 'stockInicial': -1}, esperado=400)
    api('stock-minimo', {'productoId': producto['id'], 'stockMinimo': -1}, esperado=400)
    api('productos/2147483647', esperado=400)
    api('productos/2147483647/editar', datos_producto, esperado=400)
    api(ruta_producto + '/estado', {}, esperado=400)
    api('productos', datos_producto, esperado=400, con_token=False)
    verificar(len(api('productos', conexion=caja)) == 122, 'Cajero consulta el catalogo SQL activo.')
    api('productos', datos_producto, conexion=caja, esperado=403)
    api(ruta_producto + '/editar', datos_producto, conexion=caja, esperado=403)
    api(ruta_producto + '/estado', {'activo': True}, conexion=caja, esperado=403)
    api('stock-minimo', {'productoId': producto['id'], 'stockMinimo': 9}, conexion=caja, esperado=403)
    api('alternativas', {'productoId': producto['id'], 'alternativosIds': []}, conexion=caja, esperado=403)
    for forma in ['Efectivo', 'Digital', 'Fiado']:
        api('ventas', {'formaPago': forma, 'montoRecibido': 100, 'referenciaPago': 'PRUEBA', 'clienteId': 1,
            'items': [{'productoId': sustituto['id'], 'cantidad': 1}]}, esperado=400)
    api('compras', {'proveedorId': 1, 'items': [{'productoId': sustituto['id'], 'cantidad': 1, 'costoUnitario': 2}]}, esperado=400)
    verificar(api(f"productos/{sustituto['id']}")['producto']['stock'] == 10 and not api('compras') and not api('ventas'), 'Registrar movimientos pendientes no cambia stock ni genera movimientos en memoria.')
print(f'{comprobaciones} comprobaciones HTTP con SQL correctas.')
