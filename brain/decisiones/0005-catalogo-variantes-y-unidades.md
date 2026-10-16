---
estado: aceptado
fecha: 2026-10-16
---
# 0005 Catálogo: variantes, atributos y unidades

## Contexto
El catálogo es la base de todo lo que viene: el kardex (H7), el costeo (H8) y los informes (H9) apuntan a lo que aquí se decida. Equivocarse en **qué entidad lleva las existencias** obliga a migrar el kardex entero, que es lo más caro de cambiar en un ERP.

El usuario fijó dos requisitos: variantes (talla, color, sabor) desde el inicio, y cantidades fraccionadas pero **sin conversiones** entre unidades.

## Decisiones

### La variante es la única portadora de stock
Todo lo que se cuenta, mueve, cuesta y valora se refiere a una **variante**, nunca a un producto. El producto es la ficha comercial («Camiseta Polo»); la variante es lo que hay en la estantería («Polo talla M roja»).

Para que el caso simple no se complique, **crear un producto sin atributos crea su variante única**, que hereda el SKU. Un martillo y una camiseta recorren el mismo camino, y H7 no necesita distinguir «productos con variantes» de «productos sin ellas»: esa bifurcación es justo el error que luego no se puede deshacer sin reescribir los movimientos.

Si el producto **sí** declara atributos, nace sin variantes: no existe «una camiseta sin talla», y quien la da de alta debe decir cuáles vende.

### Atributos en `jsonb`, no en tablas normalizadas
La variante guarda `{"color":"rojo","talla":"M"}` en una columna `jsonb`, con las claves ordenadas para que el texto sea estable. El producto declara los nombres de atributo en `text[]`.

Invariantes que se validan en el dominio: los atributos de la variante deben coincidir **exactamente** con los declarados por su producto, y no puede haber dos variantes del mismo producto con la misma combinación (si no, vender «talla M roja» sería ambiguo).

**Alternativa descartada por ahora:** tres tablas normalizadas (atributo, valor, valor-de-variante). Da integridad referencial sobre los valores y listas desplegables «gratis», a cambio de tres tablas y varios *joins* en cada consulta del catálogo, antes de que exista un solo movimiento. Migrar de `jsonb` a tablas más adelante es acotado, porque los valores ya están normalizados y ordenados.

### Una unidad por producto, sin conversiones
`UnidadMedida` guarda código, nombre y **decimales admitidos** (UND = 0, KG = 3). No lleva factor ni magnitud. En `Core`, `Unidad` y `Cantidad` validan lo que importa: **una cantidad con más decimales de los que admite su unidad se rechaza**, para que no existan «0,5 martillos» en una bodega.

El redondeo al escalar por un factor es explícito y se aleja de cero: 7 unidades al 33 % son 2 piezas, no 2,31. Si el resto se arrastrara en silencio, el kardex acumularía deriva.

**Ruta hacia las conversiones (si algún día se piden):** es **aditiva**. Se añaden columnas de factor y magnitud a `unidades_medida`, un convertidor en `Core` y una unidad de compra opcional en el producto. **No obliga a migrar el kardex**, porque las cantidades ya se guardan en la única unidad del producto. Por eso se puede posponer sin deuda.

### Códigos de barras en la variante
Un código identifica lo que se escanea en la caja, así que cuelga de la variante y es **único por empresa**: si dos variantes lo compartieran, escanear no diría qué se está vendiendo. Una variante puede tener varios (presentaciones, reempaques).

### Unicidad por empresa, no global
SKU, código de barras, código de unidad y nombre de categoría son únicos **dentro de la empresa**. Dos clientes del mismo despliegue pueden usar el mismo SKU para artículos distintos, y hay una prueba de integración que lo comprueba.

### `Add` explícito para los hijos de un agregado
Las entidades nacen con su Id ya puesto (GUID v7). Cuando se añade un hijo a un agregado **ya cargado**, EF lo toma por existente y emite un `UPDATE` que no afecta a ninguna fila. Los endpoints llaman a `db.Add(...)` sobre la entidad nueva. Lo descubrió una prueba de integración, no el compilador; conviene recordarlo en los módulos siguientes.

## Consecuencias
- Toda tabla nueva del catálogo necesita su política RLS en la migración. `PoliticasRlsTests` lo exige y se verificó saboteando dos tablas.
- La lectura de productos proyecta **en memoria**: los atributos no son columnas traducibles (campo privado excluido del modelo en el producto, JSON a deserializar en la variante).
- `ModulosDePersistencia` se movió a `src/GestorInve.Composicion`, la raíz de composición. Antes la Api y las pruebas de persistencia mantenían listas separadas y el módulo nuevo quedó invisible para las pruebas: ahora hay una sola lista.

## Fuera de alcance
- **Lotes y seriales como entidades**: el producto solo declara si los exige (`ExigeLote`, `ExigeSerial`). Las entidades viven con los movimientos (H7), porque un lote sin kardex no significa nada.
- Listas de precios e impuestos (fase de ventas), importación y exportación (H9), existencias mínimas **por bodega** (H7; hoy están en la variante).
