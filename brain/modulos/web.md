---
estado: implementado
actualizado: 2026-10-16
---
# Interfaz web
Proyecto: `web/` (Vite + React + TypeScript). No es un módulo del backend.

## Piezas
- `src/api/esquema.ts`: **generado** del OpenAPI. No se edita a mano.
- `src/api/cliente.ts`: cliente tipado, sesión y refresco con una sola petición en vuelo.
- `src/api/formato.ts`: dinero, cantidades y fechas en es-CO; `aNumero` normaliza los decimales que el contrato permite como texto.
- `src/sesion/`: contexto de sesión y permisos (la interfaz esconde lo que el servidor ya rechazaría; nunca es la única defensa).
- `src/paginas/`: entrar, alta de empresa, catálogo, inventario, kardex.

## Pruebas
- `tests/`: unitarias (vitest), incluido el refresco único en vuelo.
- `e2e/`: punta a punta en Chromium real contra la Api y PostgreSQL.

## Pendiente
- Cookie `httpOnly` para el refresco (cambio de Api) y CSRF.
- Usuarios y roles, traslados y reversas desde la interfaz.
- Paginación en las listas.
