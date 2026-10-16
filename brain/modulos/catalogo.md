---
estado: implementado
actualizado: 2026-10-16
---
# Módulo Catálogo
Proyecto: `src/Modules/GestorInve.Catalogo` (referencia a `GestorInve.Core` y `GestorInve.Persistencia`). Esquema de base: `catalogo`.

## Entidades
- `UnidadMedida`: código, nombre y **decimales admitidos**. Sin factores de conversión (ADR 0005). `ComoUnidad()` devuelve la `Unidad` del dominio.
- `Categoria`: árbol con padre. `MoverBajo` rechaza ciclos, que colgarían cualquier recorrido del árbol.
- `Producto`: ficha comercial. **No lleva existencias.** SKU único por empresa, unidad, categoría, atributos declarados y banderas `ExigeLote` / `ExigeSerial`.
- `Variante`: **la unidad de inventario del sistema**; el kardex de H7 apunta aquí. SKU propio, atributos en `jsonb`, límites de stock.
- `CodigoBarras`: de la variante, único por empresa.

## Reglas que vive el dominio
- Un producto sin atributos nace con su variante única, que hereda el SKU.
- Los atributos de una variante coinciden exactamente con los declarados por su producto.
- No hay dos variantes del mismo producto con la misma combinación de atributos.
- El mínimo de stock no puede superar el máximo.
- Una cantidad no admite más decimales que su unidad (`Cantidad` en `Core`).

## API
`/catalogo/unidades`, `/catalogo/categorias` (incluido recolocar en el árbol), `/catalogo/productos`, `/catalogo/productos/{id}/variantes`, `/catalogo/variantes/{id}/codigos`, `/catalogo/variantes/{id}/limites` y `/catalogo/codigos/{codigo}` para resolver un escaneo.
Permisos `catalogo:leer` y `catalogo:escribir`. Un registro de otra empresa responde **404 y no 403**, para no confirmar que existe.

## Pendiente
- Editar y desactivar desde la API (hoy solo alta y consulta).
- Lotes y seriales como entidades (H7), existencias por bodega (H7).
- Importación y exportación masiva (H9).
