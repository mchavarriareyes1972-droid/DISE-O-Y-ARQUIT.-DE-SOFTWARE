const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Base temporal de CI: las pruebas HTTP previas agregaron tres productos.
(async () => {
    const browser = await chromium.launch({ headless: true });
    const base = 'http://127.0.0.1:5099';
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [];
    page.on('pageerror', error => errores.push(error.message));
    async function entrar(p, usuario, clave) {
        await p.goto(base + '/Cuenta/Login');
        await p.locator('#login').fill(usuario); await p.locator('#password').fill(clave);
        await p.getByRole('button', { name: 'Ingresar', exact: true }).click(); await p.waitForURL(base + '/');
    }
    async function texto(id, contenido) {
        await page.waitForFunction(({ id, contenido }) => document.getElementById(id).textContent.includes(contenido), { id, contenido });
    }
    async function inventario() {
        await page.goto(base + '/Home/Inventario');
        await texto('cantidadProductos', 'Productos mostrados:');
    }
    async function opcion(id, nombre) {
        const o = page.locator(`#${id} option`).filter({ hasText: nombre });
        await o.waitFor({ state: 'attached' }); return o.getAttribute('value');
    }
    try {
        await entrar(page, 'admin', process.env.GIOVANNA_ADMIN_PASSWORD);
        await inventario();
        assert.equal(await page.locator('#tablaInventario tr').count(), 123);
        assert(await page.locator('#tablaInventario .status.low').count() >= 7);
        for (const [codigo, nombre, stock] of [['QA_UI_01', "Aceite O'Connor 100% [UI]", '0'], ['QA_UI_02', 'Sustituto UI disponible', '8']]) {
            await page.locator('#nuevoProducto').click();
            await page.locator('#codigoProducto').fill(codigo); await page.locator('#nombreProducto').fill(nombre);
            await page.locator('#precioProducto').fill('6.50'); await page.locator('#stockInicialProducto').fill(stock);
            await page.locator('#formProducto button[type=submit]').click();
            await page.locator('#dialogoProducto').waitFor({ state: 'hidden' });
        }
        await texto('cantidadProductos', 'Productos mostrados: 125');
        await page.locator('#buscarInventario').fill('QA_UI_01');
        await texto('cantidadProductos', 'Productos mostrados: 1 ·');
        const fila = page.locator('#tablaInventario tr').filter({ hasText: 'QA_UI_01' });
        await fila.getByRole('button', { name: 'Editar', exact: true }).click();
        assert(await page.locator('#stockInicialProducto').isDisabled());
        await page.locator('#nombreProducto').fill('Producto SQL UI actualizado');
        await page.locator('#precioProducto').fill('7.25'); await page.locator('#minimoProducto').fill('4');
        await page.locator('#formProducto button[type=submit]').click();
        await page.locator('#dialogoProducto').waitFor({ state: 'hidden' });
        await page.reload(); await page.locator('#buscarInventario').fill('');
        await texto('cantidadProductos', 'Productos mostrados: 125');
        await page.locator('#buscarInventario').fill('QA_UI_01');
        await texto('cantidadProductos', 'Productos mostrados: 1 ·');
        assert((await fila.innerText()).includes('Producto SQL UI actualizado'));
        assert.equal(await fila.locator('td').nth(3).innerText(), '0');

        const original = await opcion('productoOriginal', 'Producto SQL UI actualizado');
        const sustituto = await opcion('productoMinimo', 'Sustituto UI disponible');
        await page.locator('#productoMinimo').selectOption(sustituto);
        await page.locator('#stockMinimo').fill('8'); await page.locator('#formStockMinimo button[type=submit]').click();
        await texto('mensajeInventario', 'Stock mínimo actualizado.');
        await page.locator('#productoOriginal').selectOption(original);
        const agotado = await opcion('productosAlternativos', 'Sustituto HTTP agotado');
        await page.locator('#productosAlternativos').selectOption([sustituto, agotado]);
        await page.locator('#formAlternativas button[type=submit]').click();
        await texto('mensajeInventario', 'Alternativas registradas.');
        await page.reload(); await page.locator('#buscarInventario').fill('');
        await texto('cantidadProductos', 'Productos mostrados: 125');
        await page.locator('#productoOriginal').selectOption(original);
        assert.equal(await page.locator('#productosAlternativos option:checked').count(), 2);
        await page.locator('#productoMinimo').selectOption(sustituto);
        assert.equal(await page.locator('#stockMinimo').inputValue(), '8');

        await page.goto(base + '/Home/Ventas');
        await page.locator('#buscarProducto').fill('QA_UI_01');
        const ventaFila = page.locator('#listaProductos .product-row').filter({ hasText: 'QA_UI_01' });
        await ventaFila.getByRole('button', { name: 'Ver alternativas', exact: true }).click();
        await page.locator('#dialogoDetalle').waitFor();
        const detalle = await page.locator('#contenidoDetalle').innerText();
        assert(detalle.includes('Sustituto UI disponible') && !detalle.includes('Sustituto HTTP agotado'));
        await page.locator('#contenidoDetalle').getByRole('button', { name: 'Agregar', exact: true }).click();
        assert((await page.locator('#detalleVenta').innerText()).includes('Sustituto UI disponible'));
        assert(await page.locator('#formVenta button[type=submit]').isEnabled());
        await page.goto(base + '/Home/Compras');
        assert(await page.locator('#formCompra button[type=submit]').isDisabled());

        await inventario();
        await page.locator('#buscarInventario').fill('QA_UI_01');
        await texto('cantidadProductos', 'Productos mostrados: 1 ·');
        await fila.getByRole('button', { name: 'Desactivar', exact: true }).click();
        await texto('tablaInventario', 'No hay registros para mostrar.');
        await page.locator('#mostrarInactivos').check();
        await texto('tablaInventario', 'Inactivo');
        await fila.getByRole('button', { name: 'Activar', exact: true }).click();
        await fila.getByRole('button', { name: 'Desactivar', exact: true }).waitFor();
        await page.locator('#nuevoProducto').click();
        await page.locator('#codigoProducto').fill('qa_ui_01'); await page.locator('#nombreProducto').fill('Duplicado');
        await page.locator('#precioProducto').fill('1'); await page.locator('#formProducto button[type=submit]').click();
        await texto('mensajeProducto', 'Ya existe un producto'); await page.locator('#cancelarProducto').click();
        fs.mkdirSync('/tmp/giovanna-qa', { recursive: true });
        await page.screenshot({ path: '/tmp/giovanna-qa/productos-sql.png', fullPage: true });

        const caja = await browser.newPage(); caja.on('pageerror', error => errores.push(error.message));
        await entrar(caja, 'cajero', process.env.GIOVANNA_CAJERO_PASSWORD);
        await caja.goto(base + '/Home/Inventario');
        await caja.waitForFunction(() => document.getElementById('cantidadProductos').textContent.includes('Productos mostrados: 125'));
        assert.equal(await caja.locator('#nuevoProducto').count(), 0);
        assert.equal(await caja.locator('#formStockMinimo').count(), 0);
        assert.equal(await caja.getByRole('button', { name: 'Editar', exact: true }).count(), 0);
        assert.deepEqual(errores, []);
        console.log('OK: productos SQL en Chromium: catalogo, alertas, alta, edicion, stock conservado, minimos, alternativas disponibles, recarga, estados, duplicados y permisos.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
