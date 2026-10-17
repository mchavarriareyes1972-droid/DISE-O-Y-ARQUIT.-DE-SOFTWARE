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
print(f'{comprobaciones} comprobaciones HTTP de clientes SQL correctas.')
