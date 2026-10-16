---
estado: aceptado
fecha: 2026-10-16
---
# 0007 Interfaz web

## Contexto
El backend llevaba seis hitos y **cero usuarios posibles**: sin pantallas, una pyme no puede usar el sistema. H3 añade `web/` con Vite, React y TypeScript.

## Decisiones

### Tipos generados del OpenAPI, no escritos a mano
`npm run api:tipos` genera `src/api/esquema.ts` desde el documento OpenAPI **real** de la API. Si la API cambia su contrato, el `tsc --noEmit` del build falla; si los tipos se escribieran a mano, el fallo aparecería en una pantalla del cliente.

Generarlos destapó dos defectos del contrato que estaban en producción desde H7:
1. **Los endpoints no declaraban sus respuestas.** El OpenAPI solo tenía los 15 esquemas de petición; se añadieron `Produces<T>` y los códigos de error, y pasó a 30. Sin esto, el cliente «tipado» habría sido una colección de suposiciones.
2. **`TipoMovimiento` viajaba como un entero.** Un número en el contrato es ilegible y **frágil**: insertar o reordenar un valor del enum cambia en silencio el significado de los datos ya enviados. Ahora va como texto (`"Entrada"`), con `JsonStringEnumConverter`. Las pruebas de integración se ajustaron a las mismas opciones de JSON que usa la API, porque antes comprobaban un contrato distinto del que ven los clientes.

Los decimales se declaran como `number | string` (así los describe .NET, porque `System.Text.Json` admite ambos al leer). El cliente los normaliza en un único sitio, `aNumero`, para que ninguna pantalla haga su propia suposición y un `"12"` no acabe concatenado en vez de sumado.

### Dónde viven los tokens, y el riesgo asumido
El de **acceso** vive solo en memoria. El de **refresco** va a `localStorage`, porque sin él la sesión se perdería en cada recarga.

**El riesgo es real y conocido:** un XSS puede leer `localStorage`. La defensa correcta es una cookie `httpOnly`, que exige cambiar la API y añadir protección CSRF, y **queda pendiente**. Lo que acota el daño mientras tanto es la rotación con detección de reutilización de H5: en cuanto el atacante usa el token robado, el servidor revoca la familia completa y la víctima tiene que volver a entrar.

### Un solo refresco en vuelo
Si tres peticiones reciben 401 a la vez y cada una refrescara por su cuenta, dos reutilizarían el mismo token de refresco, el servidor lo interpretaría como un robo y **echaría al usuario sin motivo**. El cliente comparte una única promesa de refresco. Verificado quitando esa compartición: la prueba detecta tres refrescos en vez de uno.

### Pruebas en navegador real, no con una API simulada
Las pruebas de punta a punta corren en Chromium contra la **API real y PostgreSQL**. Con un doble no se comprobaría lo que de verdad falla: el contrato, el refresco de la sesión y que el kardex de la pantalla cuadre con el del servidor. Se verificó su valor saboteando el signo de la salida en el backend: el e2e falla.

La pantalla del kardex **recalcula el acumulado** y lo compara con el saldo que guardó el servidor; si no coinciden, lo muestra como alerta en vez de esconderlo.

### Accesibilidad y uso real
Enlace para saltar al contenido, foco siempre visible, `role="alert"` en los avisos, etiquetas asociadas a cada campo y tablas con encabezados. Botones de 44 px de alto y campos de 16 px: una bodega se opera con el teléfono en la mano, y menos de eso falla al tocar o provoca zoom en iOS. Tema claro y oscuro con los mismos tokens.

### Sin biblioteca de i18n
La interfaz es solo es-CO, así que los textos van en el código y los números por `Intl`. Añadir una biblioteca de traducción para un idioma sería peso sin uso; cuando haya un segundo idioma, se añade.

## Pendientes
- **Cookie `httpOnly` para el refresco** (cambio de API) y protección CSRF.
- Pantallas de administración de usuarios y roles, traslados y reversas desde la interfaz (hoy solo por API).
- Sin paginación: con miles de productos las listas se harán lentas.
