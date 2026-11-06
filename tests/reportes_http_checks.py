"""Contrasta filtros, importes y archivos del reporte con los movimientos SQL."""
import csv
import html
import http.cookiejar
import io
import json
import os
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from pathlib import Path

BASE = 'http://127.0.0.1:5099'
LIMA = timezone(timedelta(hours=-5))
SALIDA = Path('/tmp/giovanna-qa')
SALIDA.mkdir(exist_ok=True)
comprobaciones = 0

def verificar(condicion, mensaje):
    global comprobaciones
    assert condicion, mensaje
    comprobaciones += 1
    print('OK: ' + mensaje)

def sesion(login, clave):
    navegador = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    pagina = navegador.open(BASE + '/Cuenta/Login').read().decode()
    token = html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^\"]+)"', pagina).group(1))
    datos = urllib.parse.urlencode({'Login': login, 'Password': clave, '__RequestVerificationToken': token}).encode()
    respuesta = navegador.open(urllib.request.Request(BASE + '/Cuenta/Login', data=datos))
    assert '/Cuenta/Login' not in respuesta.url
    return navegador

admin = sesion('admin', os.environ['GIOVANNA_ADMIN_PASSWORD'])
caja = sesion('cajero', os.environ['GIOVANNA_CAJERO_PASSWORD'])

def obtener(ruta, navegador=admin, esperado=200):
    try:
        respuesta = navegador.open(BASE + '/api/tienda/' + ruta, timeout=60)
    except urllib.error.HTTPError as error:
        respuesta = error
    verificar(respuesta.code == esperado, ruta + ': HTTP ' + str(esperado))
    return respuesta.read(), respuesta.headers

def reporte(parametros, navegador=admin):
    return json.loads(obtener('reporte?' + urllib.parse.urlencode(parametros), navegador)[0])

def suma(registros, campo):
    return sum((Decimal(str(r[campo])) for r in registros), Decimal(0))

def en_periodo(registro, parametros):
    dia = datetime.fromisoformat(registro['fecha']).astimezone(LIMA).date().isoformat()
    return (not parametros.get('desde') or dia >= parametros['desde']) and (not parametros.get('hasta') or dia <= parametros['hasta'])

def verificar_importes(resultado, ventas, pagos):
    vendido, abonos = suma(ventas, 'total'), suma(pagos, 'monto')
    fiado = suma([v for v in ventas if v['formaPago'] == 'Fiado'], 'total')
    verificar(resultado['cantidadVentas'] == len(ventas) and resultado['cantidadAbonos'] == len(pagos), 'Cantidades de ventas y abonos coinciden con los movimientos filtrados.')
    verificar(Decimal(str(resultado['totalVendido'])) == vendido and Decimal(str(resultado['totalFiado'])) == fiado, 'Total vendido y fiado exactos con decimales.')
    verificar(Decimal(str(resultado['cobradoEnVentas'])) == vendido - fiado and Decimal(str(resultado['abonos'])) == abonos and Decimal(str(resultado['totalCobrado'])) == vendido - fiado + abonos, 'Cobrado del periodo suma ventas cobradas y abonos, excluyendo fiados.')
    verificar(suma([d for v in resultado['ventas'] for d in v['detalles']], 'subtotal') == vendido, 'El detalle vendido suma el mismo total del reporte.')

def verificar_csv(datos, resultado):
    filas = list(csv.DictReader(io.StringIO(datos.decode('utf-8-sig')), delimiter=';'))
    ventas = [f for f in filas if f['Tipo'] == 'Venta']
    pagos = [f for f in filas if f['Tipo'] == 'Abono']
    resumen = [f for f in filas if f['Tipo'] == 'Resumen']
    verificar(len(ventas) == resultado['cantidadVentas'] and len(pagos) == resultado['cantidadAbonos'] and len(resumen) == 1, 'CSV contiene exactamente los movimientos del periodo y un resumen.')
    verificar({int(f['Número']) for f in ventas} == {v['id'] for v in resultado['ventas']} and {int(f['Número']) for f in pagos} == {p['id'] for p in resultado['pagos']}, 'CSV conserva los identificadores del reporte consultado.')
    verificar(all(f['Usuario'] for f in ventas + pagos) and Decimal(resumen[0]['Total cobrado del periodo']) == Decimal(str(resultado['totalCobrado'])), 'CSV incluye usuarios y total cobrado sin duplicar fiados.')
    verificar(sum((Decimal(f['Total vendido']) for f in ventas), Decimal(0)) == Decimal(str(resultado['totalVendido'])) and sum((Decimal(f['Abono']) for f in pagos), Decimal(0)) == Decimal(str(resultado['abonos'])), 'Importes CSV coinciden exactamente con las tarjetas del reporte.')

completo = reporte({})
verificar_importes(completo, completo['ventas'], completo['pagos'])
for parametros in [
    {'desde': '2026-09-17', 'hasta': '2026-10-16'},
    {'desde': '2026-09-17', 'hasta': '2026-09-17'},
    {'desde': '2026-10-16', 'hasta': '2026-10-16'},
    {'desde': '2026-09-21', 'hasta': '2026-09-23'},
    {'desde': '2026-10-01'}, {'hasta': '2026-09-18'},
    {'desde': '2000-01-01', 'hasta': '2000-01-01'}
]:
    resultado = reporte(parametros)
    ventas = [v for v in completo['ventas'] if en_periodo(v, parametros)]
    pagos = [p for p in completo['pagos'] if en_periodo(p, parametros)]
    verificar({v['id'] for v in resultado['ventas']} == {v['id'] for v in ventas} and {p['id'] for p in resultado['pagos']} == {p['id'] for p in pagos}, 'Filtros inclusivos de Lima devuelven las ventas y abonos esperados.')
    verificar_importes(resultado, ventas, pagos)
    verificar(resultado['productosStockBajo'] == completo['productosStockBajo'], 'La alerta de stock bajo sigue representando el inventario actual.')
    consulta = urllib.parse.urlencode(parametros)
    csv_datos, headers = obtener('reporte/exportar/csv?' + consulta)
    verificar('attachment' in headers['Content-Disposition'] and 'text/csv' in headers['Content-Type'], 'CSV se entrega como archivo descargable.')
    verificar_csv(csv_datos, resultado)
    if parametros == {'desde': '2026-09-17', 'hasta': '2026-10-16'}:
        verificar(resultado['cantidadVentas'] == 931 and resultado['cantidadAbonos'] == 124, 'Mes original conserva las 931 ventas y 124 abonos del script.')
    if parametros.get('hasta') == parametros.get('desde') and parametros.get('desde') in ['2026-09-17', '2000-01-01']:
        pdf_datos, headers = obtener('reporte/exportar/pdf?' + consulta, caja)
        verificar(pdf_datos.startswith(b'%PDF-') and 'attachment' in headers['Content-Disposition'], 'Cajero puede descargar un PDF real del periodo.')
        archivo = SALIDA / ('reporte_sql_vacio.pdf' if not ventas else 'reporte_sql_dia.pdf')
        archivo.write_bytes(pdf_datos)
        texto = subprocess.run(['pdftotext', '-layout', str(archivo), '-'], capture_output=True, text=True, check=True).stdout
        normalizado = re.sub(r'\s+', ' ', texto)
        verificar('Bodega Giovanna' in normalizado and 'Total cobrado del periodo: S/ ' + format(Decimal(str(resultado['totalCobrado'])), '.2f') in normalizado, 'PDF contiene titulo y total cobrado correcto.')
        verificar('Página 1 de ' in normalizado and 'Abonos del periodo' in normalizado, 'PDF conserva paginacion y seccion de abonos.')
        if ventas:
            verificar('Usuario: ' in normalizado and all('Venta ' + str(v['id']) + ' |' in normalizado for v in ventas), 'PDF contiene usuarios y todas las ventas del dia filtrado.')
        else:
            verificar('No hay ventas en el periodo consultado.' in normalizado and 'No hay abonos en el periodo consultado.' in normalizado, 'PDF de periodo vacio indica que no hay movimientos.')

verificar(reporte({}, caja)['totalCobrado'] == completo['totalCobrado'], 'Administrador y cajero consultan los mismos totales SQL.')
for ruta in ['reporte', 'reporte/exportar/csv', 'reporte/exportar/pdf']:
    obtener(ruta + '?desde=2026-10-16&hasta=2026-09-17', esperado=400)
    obtener(ruta + '?desde=no-es-fecha', esperado=400)
    obtener(ruta, urllib.request.build_opener(), esperado=401)
obtener('reporte/exportar/xlsx', esperado=400)
verificar(len(reporte({})['ventas']) == len(completo['ventas']) and len(reporte({})['pagos']) == len(completo['pagos']), 'Consultar y exportar reportes no modifica movimientos.')
print(f'{comprobaciones} comprobaciones de reportes SQL y archivos correctas.')
