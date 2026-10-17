"""Base aislada de CI. Todas las contrasenas y hashes nacen durante la prueba."""
import base64
import hashlib
import os
from pathlib import Path
import secrets
import struct
import subprocess
import tempfile
import time

clave_servidor = secrets.token_urlsafe(24) + '!A1'
clave_admin = secrets.token_urlsafe(24) + '!A1'
clave_caja = secrets.token_urlsafe(24) + '!A1'
for valor in [clave_servidor, clave_admin, clave_caja]:
    print('::add-mask::' + valor, flush=True)
entorno = dict(os.environ, MSSQL_SA_PASSWORD=clave_servidor, SQLCMDPASSWORD=clave_servidor)
contenedor = subprocess.run([
    'docker', 'run', '-d', '--name', 'giovanna-sql-ci',
    '-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_PID=Developer', '-e', 'MSSQL_SA_PASSWORD',
    '-p', '127.0.0.1:1433:1433', 'mcr.microsoft.com/mssql/server:2022-latest'
], env=entorno, capture_output=True, text=True, check=True).stdout.strip()
sqlcmd = ['docker', 'exec', '-e', 'SQLCMDPASSWORD', contenedor,
          '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b']
for intento in range(60):
    listo = subprocess.run(sqlcmd + ['-Q', 'SELECT 1'], env=entorno, capture_output=True, timeout=10)
    if listo.returncode == 0:
        break
    time.sleep(2)
else:
    raise RuntimeError('SQL Server no termino de iniciar.')

def hash_identity(clave):
    sal = secrets.token_bytes(16)
    subclave = hashlib.pbkdf2_hmac('sha512', clave.encode(), sal, 100000, 32)
    return base64.b64encode(b'\x01' + struct.pack('>III', 2, 100000, 16) + sal + subclave).decode()

with tempfile.TemporaryDirectory() as temporal:
    for archivo in ['01_Crear_Tablas.sql', '02_Datos_Un_Mes.sql']:
        texto = Path('database', archivo).read_text(encoding='utf-8-sig')
        texto = texto.replace('$(HASH_ADMIN)', hash_identity(clave_admin)).replace('$(HASH_CAJERO)', hash_identity(clave_caja))
        local = Path(temporal, archivo)
        local.write_text(texto, encoding='utf-8-sig')
        subprocess.run(['docker', 'cp', str(local), contenedor + ':/tmp/' + archivo], check=True)
        subprocess.run(sqlcmd + ['-i', '/tmp/' + archivo], env=entorno, check=True)
conexion = 'Server=localhost,1433;Database=BodegaGiovanna;User ID=sa;Password=' + clave_servidor + ';Encrypt=True;TrustServerCertificate=True'
print('::add-mask::' + conexion, flush=True)
variables = {'GIOVANNA_SQL_PRUEBAS': conexion, 'ConnectionStrings__BodegaGiovanna': conexion,
             'GIOVANNA_ADMIN_PASSWORD': clave_admin, 'GIOVANNA_CAJERO_PASSWORD': clave_caja}
with open(os.environ['GITHUB_ENV'], 'a') as salida:
    for nombre, valor in variables.items():
        salida.write(nombre + '=' + valor + '\n')
print('Base temporal creada y cargada; claves generadas para esta ejecucion.')
