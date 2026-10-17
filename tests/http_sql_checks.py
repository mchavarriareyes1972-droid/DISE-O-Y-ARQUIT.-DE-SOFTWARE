"""Comprueba el acceso web con las credenciales SQL generadas por CI."""
import html
import http.cookiejar
import json
import os
import re
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
