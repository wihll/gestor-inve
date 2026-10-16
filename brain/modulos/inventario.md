---
estado: implementado
actualizado: 2026-10-16
---
# Módulo Inventario
Proyecto: `src/Modules/GestorInve.Inventario` (referencia declarada a `GestorInve.Catalogo`, más `Core` y `Persistencia`). Esquema `inventario`.

## Entidades
- `Bodega`: código único por empresa. **Crear una crea su ubicación `GENERAL`.**
- `Ubicacion`: código único por bodega. Las existencias se cuentan por (variante, bodega, ubicación, lote).
- `Lote`: código único por variante, vencimiento opcional (`date`, no instante) y `es_serial`. **Un serial es un lote de una sola unidad**; ver [[decisiones/0009-lotes-y-seriales]].
- `Movimiento`: el asiento del kardex. **Inmutable**, con disparador en la base que rechaza `UPDATE` y `DELETE`. Guarda cantidad, costo unitario y total, el **saldo resultante**, la unidad y la moneda del momento, `lote_id`, `traslado_id`, `movimiento_reversado_id` (único), `forzado_negativo` y `operacion_id` (agrupa los asientos que generó una sola petición, p. ej. un reparto FEFO).
- `Existencia`: proyección del saldo por celda. Una fila por celda, garantizada por un índice único con **`nulls not distinct`** —sin esa cláusula habría dos filas para la misma celda sin lote y el bloqueo pesimista dejaría de servir—; es lo que se bloquea con `FOR UPDATE`.

## Servicio
`ServicioMovimientos`: entrada, salida, ajuste, traslado y reversa. Cada operación en una transacción con bloqueo pesimista de la celda. Los traslados bloquean sus dos celdas en orden de identificador para no abrazarse.

## Reglas
- La cantidad del movimiento es siempre positiva; el sentido lo da el tipo.
- Una salida sin existencias se rechaza, salvo con `inventario:ajustar`, y entonces queda marcada.
- Un movimiento se reversa a lo sumo una vez, y la reversa usa el costo del original.
- Un traslado conserva el costo: mover mercancía no cambia lo que costó.
- Un producto que exige lote o serial no admite movimientos **sin** lote; uno que no la exige no admite movimientos **con** lote.
- Una **salida sin lote** de un producto con lotes se reparte por vencimiento más próximo (FEFO), con un asiento por lote y un `operacion_id` común. FEFO no elige lotes vencidos ni fuerza saldos negativos.
- Un lote vencido **no sale** como despacho: la baja entra como ajuste negativo, con el permiso de ajuste.
- Un serial solo admite cantidad 1 y no puede tener existencia en dos celdas.
- Un traslado conserva el lote; la reversa usa el lote del asiento original sin volver a juzgar el vencimiento.
- Promedio ponderado; con saldo ≤ 0 la entrada reinicia el promedio. Detalle en [[decisiones/0006-kardex-inmutable-y-costeo]].

## API
Bajo `/api` (ADR 0008): `/api/inventario/bodegas` (+ `/ubicaciones`), `/api/inventario/lotes`, `/api/inventario/variantes/{id}/lotes`, `/api/inventario/movimientos`, `/api/inventario/traslados`, `/api/inventario/movimientos/{id}/reversa`, `/api/inventario/existencias` y `/api/inventario/kardex`. Permisos `inventario:leer`, `inventario:mover` e `inventario:ajustar`.

`POST /api/inventario/movimientos` devuelve una **lista** de asientos, porque una salida FEFO puede partirse en varios.

## Pendiente
- Conteo físico guiado, existencias mínimas por bodega, reservas de lote, alertas de vencimiento próximo (H9).
- Partición automática del traslado entre lotes: hoy un traslado mueve un lote a la vez.
- PEPS y los informes de valorización (H8).
