import { expect, test, type Locator, type Page } from "@playwright/test";

/// Localiza una tarjeta por su título y devuelve **solo** su interior.
///
/// No es cosmética. Catálogo e Inventario tienen campos con el mismo rótulo («Código»,
/// «Nombre»), así que un `page.getByLabel("Código")` puede resolverse contra el formulario de
/// la pantalla **anterior** mientras el enrutador aún no la ha desmontado: se rellena el
/// campo equivocado, el formulario correcto queda vacío, `required` impide el envío y la
/// prueba falla más adelante con un «no existe la opción», sin ninguna petición de por medio.
/// Era exactamente el fallo del runner, que aquí no se reproducía porque la transición es
/// instantánea. Acotar por tarjeta hace imposible ese cruce.
function tarjeta(page: Page, titulo: string): Locator {
  return page
    .locator("section.tarjeta")
    .filter({ has: page.getByRole("heading", { name: titulo, exact: true }) });
}

/// Espera a que la pantalla pedida esté montada antes de tocar sus campos.
async function irA(page: Page, nombre: string) {
  await page.getByRole("link", { name: nombre }).click();
  await expect(page.getByRole("heading", { level: 1, name: nombre })).toBeVisible();
}

/// `selectOption` solo acepta texto exacto, y las opciones llevan el nombre del producto
/// detrás del SKU; se busca la opción que lo contiene y se elige por su valor.
async function seleccionarPorTexto(ambito: Locator, etiqueta: string, contiene: string) {
  const select = ambito.getByLabel(etiqueta);
  const opcion = select.locator("option", { hasText: contiene }).first();

  // La opción puede llegar con los datos de una consulta en vuelo: esperarla en vez de leer
  // un `value` que todavía no existe, que daría `selectOption(null)`.
  await expect(opcion).toBeAttached();
  await select.selectOption(await opcion.getAttribute("value"));
}

/// Fecha en `yyyy-MM-dd`, que es lo que acepta un `input[type=date]`.
function enDias(dias: number): string {
  const fecha = new Date();
  fecha.setUTCDate(fecha.getUTCDate() + dias);

  return fecha.toISOString().slice(0, 10);
}

async function crearLote(formulario: Locator, sku: string, codigo: string, vencimiento: string) {
  await seleccionarPorTexto(formulario, "Variante del lote", sku);
  await formulario.getByLabel(/Código del lote/).fill(codigo);
  await formulario.getByLabel(/Vencimiento/).fill(vencimiento);
  await formulario.getByRole("button", { name: "Crear lote" }).click();
}

async function registrarEntrada(
  formulario: Locator,
  sku: string,
  lote: string,
  cantidad: string,
  costo: string,
) {
  await formulario.getByLabel("Tipo").selectOption("Entrada");
  await seleccionarPorTexto(formulario, "Variante", sku);
  await seleccionarPorTexto(formulario, "Bodega", "PRI");
  await seleccionarPorTexto(formulario, "Lote", lote);
  await formulario.getByLabel("Cantidad").fill(cantidad);
  await formulario.getByLabel("Costo unitario").fill(costo);
  await formulario.getByRole("button", { name: "Registrar" }).click();
}

/// Datos únicos por ejecución: la base es compartida y el NIT es único por empresa.
function datosNuevos() {
  const sufijo = Math.random().toString(36).slice(2, 10);

  return {
    nit: `NITE2E${sufijo}`.slice(0, 15),
    razonSocial: "Pyme de Pruebas S.A.S.",
    nombreAdmin: "Administradora",
    correo: `admin-${sufijo}@empresa.co`,
    contrasena: "Clave-Muy-Larga-2026",
  };
}

async function registrarYEntrar(page: Page) {
  const datos = datosNuevos();

  await page.goto("/alta");
  await page.getByLabel("NIT").fill(datos.nit);
  await page.getByLabel("Razón social").fill(datos.razonSocial);
  await page.getByLabel("Nombre del administrador").fill(datos.nombreAdmin);
  await page.getByLabel("Correo del administrador").fill(datos.correo);
  await page.getByLabel(/Contraseña/).fill(datos.contrasena);
  await page.getByRole("button", { name: "Registrar" }).click();

  await expect(page).toHaveURL(/\/entrar/);

  await page.getByLabel("NIT de la empresa").fill(datos.nit);
  await page.getByLabel("Correo").fill(datos.correo);
  await page.getByLabel("Contraseña").fill(datos.contrasena);
  await page.getByRole("button", { name: "Entrar" }).click();

  await expect(page.getByRole("button", { name: new RegExp(datos.correo) })).toBeVisible();

  return datos;
}

test("una pyme se registra, entra, carga su catálogo y mueve inventario", async ({ page }) => {
  await registrarYEntrar(page);

  // --- Catálogo: unidad y producto
  await irA(page, "Catálogo");

  const nuevaUnidad = tarjeta(page, "Nueva unidad de medida");
  const nuevoProducto = tarjeta(page, "Nuevo producto");

  await nuevaUnidad.getByRole("button", { name: "Crear unidad" }).click();
  await expect(nuevoProducto.getByRole("option", { name: /UND/ })).toBeAttached();

  const sku = `MART-${Math.random().toString(36).slice(2, 7).toUpperCase()}`;
  await nuevoProducto.getByLabel("SKU").fill(sku);
  await nuevoProducto.getByLabel("Nombre").fill("Martillo de uña");
  await seleccionarPorTexto(nuevoProducto, "Unidad de medida", "UND");
  await nuevoProducto.getByRole("button", { name: "Crear producto" }).click();

  // El producto aparece con su variante única, que hereda el SKU.
  const fila = tarjeta(page, "Productos").getByRole("row").filter({ hasText: sku });
  await expect(fila).toBeVisible();
  await expect(fila.locator('[data-prueba="variantes"]')).toHaveText(sku);

  // --- Inventario: bodega y entrada
  await irA(page, "Inventario");

  const nuevaBodega = tarjeta(page, "Nueva bodega");
  const movimiento = tarjeta(page, "Registrar movimiento");

  await nuevaBodega.getByLabel("Código").fill("PRI");
  await nuevaBodega.getByLabel("Nombre").fill("Principal");
  await nuevaBodega.getByRole("button", { name: "Crear bodega" }).click();
  await expect(movimiento.getByRole("option", { name: /PRI/ })).toBeAttached();

  await seleccionarPorTexto(movimiento, "Variante", sku);
  await seleccionarPorTexto(movimiento, "Bodega", "PRI");
  await movimiento.getByLabel("Cantidad").fill("10");
  await movimiento.getByLabel("Costo unitario").fill("1000");
  await movimiento.getByRole("button", { name: "Registrar" }).click();

  await expect(page.getByRole("alert")).toContainText("Movimiento registrado");

  // Las existencias muestran 10 unidades con su valorización.
  const existencia = tarjeta(page, "Existencias").getByRole("row").filter({ hasText: sku });
  await expect(existencia.locator('[data-prueba="cantidad"]')).toHaveText("10 UND");

  // --- Una salida deja 7
  await movimiento.getByLabel("Tipo").selectOption("Salida");
  await seleccionarPorTexto(movimiento, "Variante", sku);
  await seleccionarPorTexto(movimiento, "Bodega", "PRI");
  await movimiento.getByLabel("Cantidad").fill("3");
  await movimiento.getByRole("button", { name: "Registrar" }).click();

  await expect(existencia.locator('[data-prueba="cantidad"]')).toHaveText("7 UND");

  // --- Kardex: el acumulado del libro tiene que cuadrar con el saldo
  await irA(page, "Kardex");
  await seleccionarPorTexto(page.locator("main"), "Variante", sku);

  await expect(page.locator('[data-prueba="acumulado"]')).toContainText("7 UND");
  await expect(page.getByRole("row").filter({ hasText: "Entrada" })).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "Salida" })).toBeVisible();

  // Si el acumulado no cuadrara, la pantalla lo grita; aquí no debe haber aviso.
  await expect(page.getByRole("alert")).toHaveCount(0);
});

test("sin sesión cualquier ruta lleva a entrar", async ({ page }) => {
  await page.goto("/kardex");

  await expect(page).toHaveURL(/\/entrar/);
  await expect(page.getByRole("button", { name: "Entrar" })).toBeVisible();
});

test("una contraseña equivocada muestra el mensaje del servidor y no entra", async ({ page }) => {
  const datos = await registrarYEntrar(page);

  await page.getByRole("button", { name: new RegExp(datos.correo) }).click();
  await expect(page.getByRole("button", { name: "Entrar" })).toBeVisible();

  await page.getByLabel("NIT de la empresa").fill(datos.nit);
  await page.getByLabel("Correo").fill(datos.correo);
  await page.getByLabel("Contraseña").fill("Clave-Equivocada-2026");
  await page.getByRole("button", { name: "Entrar" }).click();

  await expect(page.getByRole("alert")).toContainText("Credenciales inválidas");
  await expect(page).toHaveURL(/\/entrar/);
});

test("un producto con lotes se despacha por vencimiento más próximo", async ({ page }) => {
  await registrarYEntrar(page);

  // --- Catálogo: un producto que exige lote
  await irA(page, "Catálogo");

  const nuevaUnidad = tarjeta(page, "Nueva unidad de medida");
  const nuevoProducto = tarjeta(page, "Nuevo producto");

  await nuevaUnidad.getByRole("button", { name: "Crear unidad" }).click();
  await expect(nuevoProducto.getByRole("option", { name: /UND/ })).toBeAttached();

  const sku = `MEDI-${Math.random().toString(36).slice(2, 7).toUpperCase()}`;
  await nuevoProducto.getByLabel("SKU").fill(sku);
  await nuevoProducto.getByLabel("Nombre").fill("Jarabe con vencimiento");
  await seleccionarPorTexto(nuevoProducto, "Unidad de medida", "UND");
  await nuevoProducto.getByLabel(/Exige lote/).check();
  await nuevoProducto.getByRole("button", { name: "Crear producto" }).click();

  await expect(tarjeta(page, "Productos").getByRole("row").filter({ hasText: sku })).toBeVisible();

  // --- Inventario: bodega y dos lotes con vencimientos distintos
  await irA(page, "Inventario");

  const nuevaBodega = tarjeta(page, "Nueva bodega");
  await nuevaBodega.getByLabel("Código").fill("PRI");
  await nuevaBodega.getByLabel("Nombre").fill("Principal");
  await nuevaBodega.getByRole("button", { name: "Crear bodega" }).click();

  const movimiento = tarjeta(page, "Registrar movimiento");
  await expect(movimiento.getByRole("option", { name: /PRI/ })).toBeAttached();

  const nuevoLote = tarjeta(page, "Nuevo lote o serial");

  // El lejano primero: si el orden dependiera de cuándo se creó el lote y no del vencimiento,
  // esta prueba lo notaría.
  await crearLote(nuevoLote, sku, "AAA-LEJANO", enDias(90));
  await crearLote(nuevoLote, sku, "ZZZ-PROXIMO", enDias(5));

  // --- Entradas: cada lote con su costo
  await registrarEntrada(movimiento, sku, "AAA-LEJANO", "10", "1000");
  await registrarEntrada(movimiento, sku, "ZZZ-PROXIMO", "10", "2000");

  const existencias = tarjeta(page, "Existencias");
  await expect(existencias.getByRole("row").filter({ hasText: "ZZZ-PROXIMO" })).toBeVisible();

  // --- Salida sin elegir lote: el servidor reparte por vencimiento
  await movimiento.getByLabel("Tipo").selectOption("Salida");
  await seleccionarPorTexto(movimiento, "Variante", sku);
  await seleccionarPorTexto(movimiento, "Bodega", "PRI");
  await movimiento.getByLabel("Lote").selectOption("");
  await movimiento.getByLabel("Cantidad").fill("12");
  await movimiento.getByRole("button", { name: "Registrar" }).click();

  // La pantalla dice que la operación tocó dos lotes: si lo escondiera, quien despacha no
  // sabría de dónde salió la mercancía que acaba de entregar.
  await expect(page.getByRole("alert")).toContainText("2 lotes");

  // El lote próximo queda en cero y el lejano en 8: FEFO, no «el primero que aparezca».
  const filaProximo = existencias.getByRole("row").filter({ hasText: "ZZZ-PROXIMO" });
  const filaLejano = existencias.getByRole("row").filter({ hasText: "AAA-LEJANO" });

  await expect(filaProximo.locator('[data-prueba="cantidad"]')).toHaveText("0 UND");
  await expect(filaLejano.locator('[data-prueba="cantidad"]')).toHaveText("8 UND");

  // --- Una entrada más, **sobre el lote que quedó en cero**
  //
  // No es decorativa: deja el último asiento del libro en una celda cuyo saldo (3) no coincide
  // con el total de la variante (11). Sin ella, total y último saldo valían 8 por casualidad y
  // la pantalla habría dado el mismo resultado comparando el total —que es lo incorrecto— que
  // comparando celda por celda. Se comprobó: con la comparación vieja, la prueba pasaba igual.
  await registrarEntrada(movimiento, sku, "ZZZ-PROXIMO", "3", "2500");

  await expect(filaProximo.locator('[data-prueba="cantidad"]')).toHaveText("3 UND");

  // --- El kardex de cada lote cuadra por separado
  await irA(page, "Kardex");
  await seleccionarPorTexto(page.locator("main"), "Variante", sku);

  // Tres asientos de ese lote: su entrada, su salida en el reparto FEFO y la entrada final.
  await expect(page.getByRole("row").filter({ hasText: "ZZZ-PROXIMO" })).toHaveCount(3);
  await expect(page.getByRole("alert")).toHaveCount(0);
});

/// Esta prueba **no existía** y por eso el defecto llegó hasta el job de despliegue de H4: las
/// demás navegan con el enrutador del cliente, que no pide la ruta al servidor. Abrir
/// `/inventario` de verdad —un favorito, un enlace compartido, una recarga— sí la pide, y el
/// servidor la reenviaba a la Api, que no tiene esa ruta. Ver ADR 0008.
test("abrir una ruta del SPA directamente la sirve el servidor, no la Api", async ({ page }) => {
  const datos = await registrarYEntrar(page);

  for (const ruta of ["/inventario", "/catalogo", "/kardex"]) {
    const respuesta = await page.goto(ruta);

    expect(respuesta?.status(), `${ruta} debe servir el SPA`).toBe(200);
    expect(respuesta?.headers()["content-type"]).toContain("text/html");
    await expect(page.getByRole("button", { name: new RegExp(datos.correo) })).toBeVisible();
  }
});

test("la sesión sobrevive a recargar la página", async ({ page }) => {
  const datos = await registrarYEntrar(page);

  await page.reload();

  // El token de acceso vive en memoria y se pierde al recargar; el de refresco lo recupera.
  await expect(page.getByRole("button", { name: new RegExp(datos.correo) })).toBeVisible();
});
