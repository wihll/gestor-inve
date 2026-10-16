---
estado: implementado
actualizado: 2026-10-16
---
# Módulo Inventario
Proyecto: `src/Modules/GestorInve.Inventario` (referencia declarada a `GestorInve.Catalogo`, más `Core` y `Persistencia`). Esquema `inventario`.

## Entidades
- `Bodega`: código único por empresa. **Crear una crea su ubicación `GENERAL`.**
- `Ubicacion`: código único por bodega. Las existencias se cuentan por (variante, bodega, ubicación).
- `Movimiento`: el asiento del kardex. **Inmutable**, con disparador en la base que rechaza `UPDATE` y `DELETE`. Guarda cantidad, costo unitario y total, el **saldo resultante**, la unidad y la moneda del momento, `traslado_id`, `movimiento_reversado_id` (único) y `forzado_negativo`.
- `Existencia`: proyección del saldo por celda. Una fila por celda; es lo que se bloquea con `FOR UPDATE`.

## Servicio
`ServicioMovimientos`: entrada, salida, ajuste, traslado y reversa. Cada operación en una transacción con bloqueo pesimista de la celda. Los traslados bloquean sus dos celdas en orden de identificador para no abrazarse.

## Reglas
- La cantidad del movimiento es siempre positiva; el sentido lo da el tipo.
- Una salida sin existencias se rechaza, salvo con `inventario:ajustar`, y entonces queda marcada.
- Un movimiento se reversa a lo sumo una vez, y la reversa usa el costo del original.
- Un traslado conserva el costo: mover mercancía no cambia lo que costó.
- Un producto que exige lote o serial **rechaza el movimiento** (501) hasta H7b.
- Promedio ponderado; con saldo ≤ 0 la entrada reinicia el promedio. Detalle en [[decisiones/0006-kardex-inmutable-y-costeo]].

## API
`/inventario/bodegas` (+ `/ubicaciones`), `/inventario/movimientos`, `/inventario/traslados`, `/inventario/movimientos/{id}/reversa`, `/inventario/existencias` y `/inventario/kardex`. Permisos `inventario:leer`, `inventario:mover` e `inventario:ajustar`.

## Pendiente
- Lotes y seriales (H7b), conteo físico guiado, existencias mínimas por bodega, reservas.
- PEPS y los informes de valorización (H8).
