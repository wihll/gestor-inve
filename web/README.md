# web

Interfaz de gestor-inve: Vite + React + TypeScript.

## Desarrollo
```bash
npm install
npm run api:tipos   # regenera los tipos desde ../openapi.json
npm run dev         # proxy a la Api en http://127.0.0.1:5080
```

## Comprobaciones
```bash
npm run build       # tsc --noEmit + build de producción
npm test            # unitarias (vitest)
npm run e2e         # punta a punta (necesita la Api y la web arrancadas)
```

Las pruebas de punta a punta usan un navegador real contra la **Api real**. En esta imagen
Chromium ya está instalado, así que se apunta a él en vez de descargar otro:

```bash
CHROMIUM_PATH=/opt/pw-browsers/chromium npx playwright test
```

El servidor de `e2e/servidor.mjs` sirve el build y reenvía las rutas de la Api al backend, para
que todo corra en el mismo origen.

## Tipos generados
`src/api/esquema.ts` **no se edita a mano**: sale del OpenAPI de la Api. Un cambio de contrato
tiene que romper el build, no una pantalla del usuario.
