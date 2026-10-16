---
estado: aceptado
fecha: 2026-10-16
---
# 0006 Kardex inmutable, ubicaciones y costeo promedio

## Contexto
El kardex es el libro del inventario y el hito **menos reversible** del proyecto: su forma no se cambia sin reescribir movimientos. Un error de costeo no se nota el día que ocurre, sino meses después al cruzar inventario contra contabilidad.

Decisiones del usuario que fijaron el alcance: **negativos bloqueados salvo permiso especial**, **kardex valorizado con promedio ponderado** y **ubicaciones dentro de la bodega desde el inicio**.

## Decisiones

### Ubicación obligatoria, con `GENERAL` por defecto
Las existencias se cuentan por (variante, bodega, **ubicación**). Crear una bodega crea su ubicación `GENERAL`, y un movimiento sin ubicación explícita va a ella.

El motivo no es la comodidad: es que **el kardex tenga una única forma**. Si la ubicación fuera opcional, el libro tendría dos clases de asiento y todo lo que lo lea —informes, traslados, conteos, el costeo— necesitaría ramas. Esa bifurcación es exactamente lo que no se deshace después. Preguntar por las ubicaciones antes de escribir el libro evitó una migración del kardex, que es el cambio más caro que tiene un ERP.

### El movimiento guarda su saldo resultante
Cada fila lleva `saldo_cantidad_despues` y `saldo_costo_promedio_despues`. Así el kardex se imprime y se audita **leyendo el libro**, sin recalcular desde el origen, y un descuadre se detecta comparando el acumulado del libro con la fila de existencias. Es redundancia deliberada: el libro es la verdad y `existencias` es una proyección que existe para consultar y **bloquear** el saldo.

### Unidad y moneda congeladas en cada movimiento
Si mañana cambia la unidad del producto, los asientos históricos tienen que seguir significando lo mismo. Por eso cada movimiento guarda el código de la unidad, sus decimales y la moneda.

### Inmutabilidad garantizada por la base, no por el código
Reversar crea un movimiento opuesto que apunta al original (`movimiento_reversado_id`, con **índice único**: cada asiento se reversa a lo sumo una vez). El original **nunca** se toca.

Además hay un **disparador en PostgreSQL** que rechaza cualquier `UPDATE` o `DELETE` sobre `inventario.movimientos`. La inmutabilidad no puede depender de que la aplicación se porte bien: la vía real por la que alguien «arregla» un movimiento es `psql`. Hay una prueba que lo comprueba por SQL crudo.

La reversa usa el **costo del movimiento original**, no el promedio vigente: deshacer una salida devuelve la mercancía al costo con el que salió.

### Promedio ponderado y la trampa del saldo no positivo
En la entrada, `nuevo = (saldo·promedio + cantidad·costo) / (saldo + cantidad)`. La salida **no** altera el promedio.

Con saldo ≤ 0 esa fórmula divide por un resultado que puede acercarse a cero o cambiar de signo, así que **si el saldo es ≤ 0 antes de una entrada, el promedio se reinicia al costo de esa entrada**. Agotar el saldo no basta para destapar el fallo (ahí la fórmula da el mismo número): el camino peligroso es el saldo **negativo**, y hay propiedades FsCheck que lo fuerzan a propósito. Se verificó quitando la regla: las propiedades fallan al primer intento.

El costo se guarda con **6 decimales** y solo se redondea al presentar. Redondear a dos en cada paso acumula deriva, y en un kardex de miles de movimientos eso descuadra la valorización.

### Bloqueo pesimista sobre la celda
Cada operación abre transacción y bloquea la fila de existencias con `SELECT … FOR UPDATE` antes de calcular. Se descartó el bloqueo optimista porque reintentar en silencio un asiento contable es peor que serializar.

**Medido, no supuesto:** con el bloqueo, 10 salidas simultáneas de 5 sobre un saldo de 100 dejan exactamente 50. Al quitar el `FOR UPDATE`, la misma prueba deja **90**: ocho de las diez salidas se perdieron. Los traslados bloquean sus dos celdas **en orden de identificador**, para que dos traslados cruzados no se abracen.

### Negativos con permiso, y marcados
Una salida sin existencias se rechaza con 409. Quien tiene `inventario:ajustar` puede forzarla, y el movimiento queda con `forzado_negativo = true`: cubre a la pyme que vende antes de registrar la compra, sin que el descuadre sea invisible. El endpoint resuelve el permiso y se lo pasa al servicio como un dato; el dominio no conoce HTTP.

### Dependencias entre módulos: declaradas, no prohibidas
`Inventario` necesita el catálogo (no puede validar una variante ni conocer su unidad sin él). La regla anterior —«ningún módulo referencia a otro»— es irreal en un ERP: ventas necesitará catálogo e inventario, y contabilidad casi todo.

Se sustituye por **dependencias declaradas**: una lista explícita en `ArquitecturaTests` (`Inventario → Catalogo`), más una prueba de que el grafo **no tiene ciclos**. Una dependencia nueva que no esté declarada sigue rompiendo el CI; se verificó añadiendo `Ventas → Catalogo` sin declararla, y la prueba falló. Esto **debilita** la fila correspondiente de `CONSTRAINTS.md`, y por eso está aquí: dos módulos que se necesitan mutuamente no se pueden razonar, probar ni desplegar por separado, así que el ciclo sigue prohibido.

## Fuera de alcance
- **Lotes y seriales (H7b).** Un producto con `ExigeLote` o `ExigeSerial` **rechaza el movimiento** con 501, en lugar de aceptarlo perdiendo la trazabilidad que el producto declara exigir. Es un hueco visible, no silencioso.
- **PEPS (H8):** la aritmética vive aislada en `SaldoValorizado`, así que añadir un segundo método no toca el libro.
- Conteo físico guiado, existencias mínimas por bodega, reservas de stock, multimoneda (cada movimiento ya guarda la suya).
