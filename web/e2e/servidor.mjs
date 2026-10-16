// Sirve el build de la web y reenvía las rutas de la API al backend, para que las pruebas
// de punta a punta corran en el mismo origen y no dependan de CORS.
import { createServer } from "node:http";
import { createReadStream, existsSync, statSync } from "node:fs";
import { join, extname } from "node:path";

const RAIZ = new URL("../dist/", import.meta.url).pathname;
const API = process.env.API_URL ?? "http://127.0.0.1:5080";
const PUERTO = Number(process.env.PORT ?? 4173);
// Un único prefijo: `/catalogo` e `/inventario` son también rutas del SPA (ADR 0008).
const PREFIJO_API = "/api";

const tipos = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
};

createServer(async (peticion, respuesta) => {
  const url = new URL(peticion.url ?? "/", `http://localhost:${PUERTO}`);

  if (url.pathname === PREFIJO_API || url.pathname.startsWith(`${PREFIJO_API}/`)) {
    const cuerpo =
      peticion.method === "GET" || peticion.method === "HEAD"
        ? undefined
        : await new Promise((resolver) => {
            const trozos = [];
            peticion.on("data", (t) => trozos.push(t));
            peticion.on("end", () => resolver(Buffer.concat(trozos)));
          });

    const cabeceras = { ...peticion.headers };
    delete cabeceras.host;
    delete cabeceras["content-length"];

    const resultado = await fetch(`${API}${url.pathname}${url.search}`, {
      method: peticion.method,
      headers: cabeceras,
      body: cuerpo && cuerpo.length > 0 ? cuerpo : undefined,
    });

    respuesta.writeHead(resultado.status, {
      "content-type": resultado.headers.get("content-type") ?? "application/json",
    });
    respuesta.end(Buffer.from(await resultado.arrayBuffer()));

    return;
  }

  // SPA: cualquier ruta desconocida devuelve index.html para que el enrutador decida.
  const candidato = join(RAIZ, url.pathname);
  const archivo =
    url.pathname !== "/" && existsSync(candidato) && statSync(candidato).isFile()
      ? candidato
      : join(RAIZ, "index.html");

  respuesta.writeHead(200, { "content-type": tipos[extname(archivo)] ?? "application/octet-stream" });
  createReadStream(archivo).pipe(respuesta);
}).listen(PUERTO, "127.0.0.1", () => {
  process.stdout.write(`web en http://127.0.0.1:${PUERTO} (api ${API})\n`);
});
