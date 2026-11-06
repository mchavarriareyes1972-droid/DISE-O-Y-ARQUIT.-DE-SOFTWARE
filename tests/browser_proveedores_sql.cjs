const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Ejecutar solo contra la base temporal del flujo verificar-sql.
(async () => {
    const browser = await chromium.launch({ headless: true });
    const base = 'http://127.0.0.1:5099';
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [];
    page.on('pageerror', error => errores.push(error.message));
    async function entrar(p, usuario, clave) {
        await p.goto(base + '/Cuenta/Login');
        await p.locator('#login').fill(usuario);
        await p.locator('#password').fill(clave);
        await p.getByRole('button', { name: 'Ingresar', exact: true }).click();
        await p.waitForURL(base + '/');
    }
    async function esperarTexto(id, texto) {
        await page.waitForFunction(({ id, texto }) => document.getElementById(id).textContent.includes(texto), { id, texto });
    }
    try {
        await entrar(page, 'admin', process.env.GIOVANNA_ADMIN_PASSWORD);
        await page.goto(base + '/Home/Proveedores');
        // Las pruebas HTTP previas registraron un proveedor y lo reactivaron.
        await esperarTexto('cantidadProveedores', 'Proveedores registrados: 13');
        assert.equal(await page.locator('#tablaProveedores tr').count(), 13);
        await page.locator('#nuevoProveedor').click();
        await page.locator('#rucProveedor').fill('20990000021');
        await page.locator('#razonProveedor').fill("Distribuidor O'Connor SQL");
        await page.locator('#contactoProveedor').fill('Contacto de pantalla');
        await page.locator('#telefonoProveedor').fill('999123123');
        await page.locator('#correoProveedor').fill('proveedor@example.test');
        await page.locator('#direccionProveedor').fill('Av. de prueba 123');
        await page.locator('#formProveedor button[type=submit]').click();
        await page.locator('#dialogoProveedor').waitFor({ state: 'hidden' });
        await esperarTexto('cantidadProveedores', 'Proveedores registrados: 14');
        await page.locator('#buscarProveedor').fill('20990000021');
        const fila = page.locator('#tablaProveedores tr').filter({ hasText: '20990000021' });
        assert((await fila.innerText()).includes("Distribuidor O'Connor SQL"));
        await fila.getByRole('button', { name: 'Ficha', exact: true }).click();
        await page.locator('#dialogoDetalle').waitFor();
        assert((await page.locator('#contenidoDetalle').innerText()).includes('Av. de prueba 123'));
        await page.locator('#cerrarDetalle').click();
        await fila.getByRole('button', { name: 'Editar', exact: true }).click();
        assert.equal(await page.locator('#correoProveedor').inputValue(), 'proveedor@example.test');
        await page.locator('#razonProveedor').fill('Proveedor SQL actualizado en pantalla');
        await page.locator('#formProveedor button[type=submit]').click();
        await page.locator('#dialogoProveedor').waitFor({ state: 'hidden' });
        await esperarTexto('tablaProveedores', 'Proveedor SQL actualizado en pantalla');
        await fila.getByRole('button', { name: 'Desactivar', exact: true }).click();
        await esperarTexto('tablaProveedores', 'Inactivo');
        await page.reload();
        await esperarTexto('cantidadProveedores', 'Proveedores registrados: 14');
        await page.locator('#buscarProveedor').fill('20990000021');
        assert((await fila.innerText()).includes('Proveedor SQL actualizado en pantalla'));
        assert((await fila.innerText()).includes('Inactivo'));
        await page.goto(base + '/Home/Compras');
        await page.locator('#proveedorCompra option').first().waitFor({ state: 'attached' });
        assert.equal(await page.locator('#proveedorCompra option').filter({ hasText: '20990000021' }).count(), 0);
        await page.goto(base + '/Home/Proveedores');
        await esperarTexto('cantidadProveedores', 'Proveedores registrados: 14');
        await page.locator('#buscarProveedor').fill('20990000021');
        await fila.getByRole('button', { name: 'Activar', exact: true }).click();
        await fila.getByRole('button', { name: 'Desactivar', exact: true }).waitFor();

        await page.locator('#nuevoProveedor').click();
        await page.locator('#rucProveedor').fill('20990000021');
        await page.locator('#razonProveedor').fill('Duplicado');
        await page.locator('#formProveedor button[type=submit]').click();
        await esperarTexto('mensajeProveedorFormulario', 'Ya existe un proveedor');
        await page.locator('#cancelarProveedor').click();
        await page.goto(base + '/Home/Compras');
        const opcion = page.locator('#proveedorCompra option').filter({ hasText: '20990000021' });
        await opcion.waitFor({ state: 'attached' });
        assert((await opcion.innerText()).includes('Proveedor SQL actualizado en pantalla'));

        await page.goto(base + '/Home/Proveedores');
        await esperarTexto('cantidadProveedores', 'Proveedores registrados: 14');
        await page.locator('#buscarProveedor').fill('actualizado en pantalla');
        assert.equal(await page.locator('#tablaProveedores tr').count(), 1);
        fs.mkdirSync('/tmp/giovanna-qa', { recursive: true });
        await page.screenshot({ path: '/tmp/giovanna-qa/proveedores-sql.png', fullPage: true });
        const caja = await browser.newPage();
        caja.on('pageerror', error => errores.push(error.message));
        await entrar(caja, 'cajero', process.env.GIOVANNA_CAJERO_PASSWORD);
        assert.equal(await caja.getByRole('link', { name: 'Proveedores', exact: true }).count(), 0);
        await caja.goto(base + '/Home/Proveedores');
        assert(caja.url().includes('/Cuenta/AccesoDenegado'));
        assert.deepEqual(errores, []);
        console.log('OK: proveedores SQL en Chromium: listado, contador, alta, ficha, edicion, baja, reactivacion, recarga, duplicados, seleccion en Compras y permisos.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exit(1); });
