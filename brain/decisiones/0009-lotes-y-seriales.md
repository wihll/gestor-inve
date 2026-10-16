---
estado: aceptado
fecha: 2026-10-16
---
# 0009 Lotes y seriales

## Contexto
El catálogo deja marcar un producto como `ExigeLote` o `ExigeSerial` desde H6, pero el kardex no tenía esa dimensión. H7 cerró el hueco con una alarma: un movimiento de un producto que exige trazabilidad se rechazaba (`TrazabilidadNoSoportada`), para no aceptar el asiento perdiendo la trazabilidad que el producto pide. H7b lo implementa.

Se hace **ahora**: añadir una dimensión al kardex es una migración de un libro contable, y es incomparablemente más barata mientras el libro está vacío que cuando una pyme lleva dos años de movimientos.

## Decisiones

### Un serial es un lote de una sola unidad
Se modela **una** entidad, `Lote`, con `EsSerial` como bandera, en lugar de dos entidades y dos dimensiones en el kardex. Un serial es, operativamente, un lote cuya existencia solo puede ser 0 o 1: el número de serie de una impresora se rastrea igual que el número de lote de un medicamento, y la diferencia está en la regla de cantidad, no en la estructura.

La alternativa —`LoteId` y `SerialId` como columnas distintas— ensancha el libro con dos dimensiones de las que siempre hay como mucho una, y obliga a duplicar cada consulta, cada índice y cada regla de costeo. Si algún día un serial necesita pertenecer a un lote (un equipo con número de serie dentro de un lote de fabricación), se añade `LoteContenedorId` al propio lote, sin tocar el kardex.

### La celda del inventario pasa a ser (variante, bodega, ubicación, lote)
`existencias` y `movimientos` ganan `LoteId` **nulable**: nulo es el caso de un producto sin trazabilidad, que es la mayoría. No se usa un lote sintético «sin lote» porque obligaría a crear una fila de catálogo por cada variante sin trazabilidad, para no decir nada.

El índice único de la celda pasa a `(variante, bodega, ubicación, lote) nulls not distinct`. Esa cláusula es el detalle que importa: por omisión PostgreSQL considera **distintos** dos NULL, así que el índice habría dejado crear dos filas de existencias para la misma celda sin lote. Eso rompería el bloqueo pesimista —dos transacciones bloquearían filas diferentes— y con él la garantía de que el libro cuadra. Requiere PostgreSQL 15 o superior, que ya era el mínimo.

### Las reglas van en el dominio, no en la base
- Producto con `ExigeLote` o `ExigeSerial` y movimiento **sin** lote → se rechaza.
- Producto **sin** trazabilidad y movimiento **con** lote → se rechaza. No se acepta una dimensión que el producto no declara: aceptarla crearía existencias que ninguna pantalla sabría mostrar y que el saldo sin lote no contaría.
- El lote tiene que ser de **esa** variante. Un lote es del artículo, no del inventario: compartirlo entre variantes confundiría dos cosas que vencen en fechas distintas.
- `ExigeSerial`: toda cantidad debe ser exactamente 1, y una entrada de un serial que ya tiene existencia se rechaza. Un serial que está en dos sitios a la vez no es un serial.

### Salida sin lote indicado: FEFO, y partiendo el movimiento
Para un producto con lotes, una salida puede **no** indicar lote. En ese caso se consumen los lotes por vencimiento más próximo primero (FEFO, *first expired, first out*), y los sin vencimiento al final. Si la cantidad pedida abarca varios lotes, se registra **un movimiento por lote**, todos en la misma transacción y agrupados por `OperacionId`.

Partir el movimiento no es un detalle de implementación: es lo único correcto. Un asiento del kardex dice «salieron 30 unidades del lote L1 y quedó este saldo»; un asiento que dijera «salieron 30 unidades de varios lotes» no permitiría reconstruir el saldo de ningún lote, que es justo para lo que existe la trazabilidad. La agrupación por `OperacionId` deja que la pantalla y los reportes lo presenten como una sola operación.

FEFO y no PEPS: lo que le importa a una pyme con lotes es no quedarse con mercancía vencida en la bodega, y eso lo decide la fecha de vencimiento, no la de entrada. PEPS como **método de costeo** es H8 y es un asunto distinto: FEFO elige de qué lote sale la mercancía física, PEPS decide a qué costo se valora.

### El promedio ponderado pasa a ser por lote, sin cambiar la aritmética
La fórmula de [[0006-kardex-inmutable-y-costeo]] se aplica por celda, y la celda ahora incluye el lote. La consecuencia es que un producto con lotes tiene **un promedio por lote**, lo cual es lo correcto y no costó nada: dos lotes comprados a precios distintos no deben promediarse entre sí, porque la trazabilidad existe precisamente para distinguirlos.

El saldo agregado de una variante en una bodega deja de ser una fila y pasa a ser una suma de filas, valorizada como la suma de `cantidad × costo_promedio`. No es el promedio de los promedios, que estaría mal ponderado.

### Un lote vencido no sale
Una salida de un lote cuya fecha de vencimiento ya pasó se rechaza (`LoteVencido`). No es una advertencia: despachar mercancía vencida es el riesgo concreto por el que una pyme pide lotes, y un sistema que lo permite «avisando» no sirve de nada. La corrección existe y es explícita: un `AjusteNegativo`, que exige el permiso `inventario:ajustar` y queda en el libro como lo que es, una baja de inventario y no una venta.

FEFO tampoco elige lotes vencidos: si lo hiciera, la salida fallaría siempre en cuanto hubiera un lote caducado en la bodega.

### El traslado conserva el lote
Mover mercancía no cambia su lote ni su vencimiento. Por eso el traslado exige que origen y destino sean el mismo lote, y la entrada hereda el lote además del costo.

## Consecuencias
- Migración `Lotes`: tabla nueva con RLS, columna `lote_id` en `movimientos` y `existencias`, y el índice único reemplazado. La tabla `movimientos` lleva un disparador que impide `UPDATE` y `DELETE` por filas; un `ALTER TABLE` es DDL y no lo toca, así que la migración no necesita desactivarlo.
- El 501 de `TrazabilidadNoSoportada` desaparece. El valor del enum se queda: quitarlo cambiaría el contrato de la Api sin necesidad.
- Lo que **no** trae: no hay reservas de lote, ni alertas de vencimiento próximo, ni partición automática en los traslados (un traslado mueve un lote a la vez). Las alertas van en H9 con los reportes.
