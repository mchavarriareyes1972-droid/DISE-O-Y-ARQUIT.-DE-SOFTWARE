const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

(async () => {
    const browser = await chromium.launch({ headless: true });
    const base = 'http://127.0.0.1:5099';
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [], solicitudesDescarga = [];
    page.on('pageerror', e => errores.push(e.message));
    page.on('request', r => { if (r.url().includes('/reporte/exportar/')) solicitudesDescarga.push(new URL(r.url())); });
    const dinero = n => new Intl.NumberFormat('es-PE', { style: 'currency', currency: 'PEN' }).format(n).replace(/\s/g, '');
    async function texto(id, contenido) {
        await page.waitForFunction(({ id, contenido }) => document.getElementById(id).textContent.includes(contenido), { id, contenido });
    }
    async function periodo(desde, hasta) {
        await page.locator('#fechaDesde').fill(desde); await page.locator('#fechaHasta').fill(hasta);
    }
    async function consultar() {
        await page.locator('#formPeriodo button[type=submit]').click();
        await page.waitForFunction(() => !document.getElementById('exportarCsv').disabled);
    }
    async function descargar(formato, desde, hasta) {
        const [archivo] = await Promise.all([page.waitForEvent('download'), page.locator('#exportar' + formato).click()]);
        assert(archivo.suggestedFilename().includes(desde + '_' + hasta));
        const bytes = fs.readFileSync(await archivo.path());
        assert(formato === 'Pdf' ? bytes.subarray(0, 5).toString() === '%PDF-' : bytes.subarray(0, 3).equals(Buffer.from([239,187,191])));
        await page.waitForFunction(() => !document.getElementById('exportarCsv').disabled);
        return bytes;
    }
    try {
        await page.goto(base + '/Cuenta/Login');
        await page.locator('#login').fill('admin'); await page.locator('#password').fill(process.env.GIOVANNA_ADMIN_PASSWORD);
        await page.getByRole('button', { name: 'Ingresar', exact: true }).click(); await page.waitForURL(base + '/');
        const completo = await (await page.request.get(base + '/api/tienda/reporte')).json();
        await page.goto(base + '/Home/Reportes'); await page.waitForFunction(() => !document.getElementById('exportarCsv').disabled);
        assert.equal(await page.locator('#tablaVentas tr').count(), completo.cantidadVentas);
        assert.equal(await page.locator('#tablaAbonosReporte tr').count(), completo.cantidadAbonos);
        assert.equal((await page.locator('#reporteTotalCobrado').innerText()).replace(/\s/g, ''), dinero(completo.totalCobrado));
        const stockBajo = await page.locator('#reporteStockBajo').innerText();
        await periodo('2000-01-01', '2000-01-01');
        assert(await page.locator('#exportarCsv').isDisabled()); assert(await page.locator('#exportarPdf').isDisabled());
        await consultar(); await texto('periodoReporte', '01/01/2000 a 01/01/2000');
        assert.equal(await page.locator('#reporteCantidad').innerText(), '0');
        assert.equal(await page.locator('#reporteCantidadAbonos').innerText(), '0');
        assert.equal(await page.locator('#reporteStockBajo').innerText(), stockBajo);
        assert((await page.locator('#tablaVentas').innerText()).includes('No hay registros'));
        assert((await page.locator('#tablaAbonosReporte').innerText()).includes('No hay registros'));
        const vacio = (await descargar('Csv', '2000-01-01', '2000-01-01')).toString('utf8');
        assert(!vacio.includes('\n"Venta";') && !vacio.includes('\n"Abono";') && vacio.includes('"Resumen"'));
        await descargar('Pdf', '2000-01-01', '2000-01-01');
        await periodo('2026-09-18', '2026-09-17');
        await page.locator('#formPeriodo button[type=submit]').click(); await texto('mensajeReportes', 'no puede ser posterior');
        assert(await page.locator('#exportarCsv').isDisabled());
        // La pantalla conserva el periodo anterior hasta una consulta valida.
        assert((await page.locator('#periodoReporte').innerText()).includes('01/01/2000'));
        await periodo('2026-09-17', '2026-09-17'); await consultar();
        const dia = await (await page.request.get(base + '/api/tienda/reporte?desde=2026-09-17&hasta=2026-09-17')).json();
        assert.equal(await page.locator('#tablaVentas tr').count(), dia.cantidadVentas);
        assert.equal(await page.locator('#tablaAbonosReporte tr').count(), dia.cantidadAbonos);
        assert.equal((await page.locator('#reporteTotalCobrado').innerText()).replace(/\s/g, ''), dinero(dia.totalCobrado));
        await page.locator('#tablaVentas tr').first().getByRole('button', { name: 'Ver detalle', exact: true }).click();
        await page.locator('#dialogoDetalle').waitFor();
        const detalle = await page.locator('#contenidoDetalle').innerText();
        assert(detalle.includes(dia.ventas[0].detalles[0].codigo) && detalle.includes(dia.ventas[0].usuarioNombre));
        await page.locator('#cerrarDetalle').click();
        await descargar('Csv', '2026-09-17', '2026-09-17'); await descargar('Pdf', '2026-09-17', '2026-09-17');
        fs.mkdirSync('/tmp/giovanna-qa', { recursive: true });
        await page.screenshot({ path: '/tmp/giovanna-qa/reportes-sql.png', fullPage: true });
        // Retener una consulta vieja y resolverla despues de "Ver todas".
        let liberar, recibida;
        const retenida = new Promise(resolve => { liberar = resolve; });
        const llegada = new Promise(resolve => { recibida = resolve; });
        const patron = url => url.pathname === '/api/tienda/reporte' && url.searchParams.get('desde') === '2000-01-01';
        const retener = async route => { const respuesta = await route.fetch(); recibida(); await retenida; await route.fulfill({ response: respuesta }); };
        await page.route(patron, retener);
        await periodo('2000-01-01', '2000-01-01'); await page.locator('#formPeriodo button[type=submit]').click();
        await llegada; await page.locator('#limpiarPeriodo').click();
        await page.waitForFunction(n => document.getElementById('reporteCantidad').textContent === String(n), completo.cantidadVentas);
        liberar(); await page.waitForFunction(() => !document.getElementById('exportarCsv').disabled);
        assert.equal(await page.locator('#reporteCantidad').innerText(), String(completo.cantidadVentas));
        assert.equal(await page.locator('#fechaDesde').inputValue(), '');
        assert((await page.locator('#periodoReporte').innerText()).includes('Inicio a Actualidad'));
        await page.unroute(patron, retener);
        assert(solicitudesDescarga.every(url => url.searchParams.get('desde') === url.searchParams.get('hasta')));
        assert.equal(solicitudesDescarga.length, 4);
        await page.reload(); await page.waitForFunction(() => !document.getElementById('exportarCsv').disabled);
        assert.equal(await page.locator('#tablaAbonosReporte tr').count(), completo.cantidadAbonos);
        assert.deepEqual(errores, []);
        console.log('OK: reportes SQL en Chromium: ventas, abonos, total cobrado, filtros, periodo vacio, fechas invalidas, detalle, CSV/PDF, periodo consultado, respuesta atrasada y recarga.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
