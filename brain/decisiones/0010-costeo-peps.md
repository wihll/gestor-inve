---
estado: aceptado
fecha: 2026-10-16
---
# 0010 Costeo PEPS

## Contexto
[[0006-kardex-inmutable-y-costeo]] eligió **promedio ponderado** como único método, por ser el que una pyme entiende sin formación contable. PEPS (primeras entradas, primeras salidas) es el otro método que la norma colombiana admite, y hay dos razones de peso para tenerlo: con inflación da un costo de ventas más bajo y un inventario final más alto, lo que cambia el impuesto; y una empresa que ya lo usa en su contabilidad no puede migrar a un sistema que la obligue a cambiar de método.

## Decisiones

### PEPS se implementa con capas, y el kardex no cambia de forma
Cada entrada crea una **capa de costo** (`inventario.capas_costo`): cantidad inicial, cantidad restante, costo unitario y el movimiento que la originó. Una salida consume capas de la más antigua a la más nueva.

El libro de movimientos **no cambia**. El asiento sigue guardando un `costo_unitario` y un `costo_total`, y bajo PEPS ese costo unitario es el **ponderado de las capas consumidas**: una salida de 30 que toma 10 a 1.000 y 20 a 2.000 se asienta a 1.666,666667 con un total de 50.000 exacto. Lo que importa es que `costo_total` sea el valor real de lo que salió; el unitario es informativo y se deriva de él.

No se parte el asiento por capa, a diferencia de lo que sí se hace por lote en [[0009-lotes-y-seriales]]. La diferencia es que el lote es una realidad física que el usuario ve, toca y despacha, mientras que la capa es un artefacto del método de valoración: partir el asiento por capa llenaría el kardex de líneas que no corresponden a ningún hecho del almacén, y el usuario no sabría qué está mirando.

### `Existencia` conserva su forma; `costo_promedio` pasa a ser el costo implícito
Bajo PEPS, `costo_promedio` deja de ser un promedio móvil y pasa a ser el **costo implícito**: valor en libros dividido por cantidad. Es exactamente lo que hace falta para valorizar, y mantener la misma columna evita duplicar el estado del saldo en dos sitios, que es la forma más segura de que terminen discrepando.

La invariante que lo amarra, y que se prueba: la suma de las capas restantes es igual a la cantidad de la existencia, y la suma de `cantidad_restante × costo_unitario` es igual a su valorización. Si las capas y la existencia se separan, el error es visible y no silencioso.

### El método es por empresa y se fija al primer movimiento
Vive en `inventario.configuracion`, una fila por empresa, **no** en la entidad `Empresa`. Dos razones: el módulo de inventario no declara dependencia de seguridad (ADR 0006) y añadirla por un enum no se justifica; y el ajuste pertenece a quien lo usa.

Cambiar el método **con movimientos registrados se rechaza**. No es rigidez: PEPS y promedio dan costos de salida distintos para la misma historia, así que cambiarlo recalcularía costos ya asentados y, en un sistema cuyo kardex es inmutable, eso no se puede hacer sin reescribir el libro. Quien de verdad necesite cambiar, cierra el periodo y arranca otra empresa, que es también lo que exige la contabilidad.

### Saldo negativo: se agotan las capas y el resto sale al último costo conocido
Una salida forzada —solo con `inventario:ajustar`— puede pasarse de lo que hay en capas. Entonces se consumen todas y lo que falta se valora al costo de la **última capa consumida** (o al costo implícito del saldo si no había ninguna). No se crean capas negativas: una capa representa mercancía que entró y todavía no ha salido, y una capa negativa no representa nada.

Cuando después entra mercancía con saldo ≤ 0, las capas **se reinician** con el costo de esa entrada, igual que el promedio reinicia en el mismo caso y por el mismo motivo: arrastrar el estado anterior a través de un saldo negativo produce valorizaciones absurdas que nadie detecta.

### La aritmética vive en `Core`, sin base de datos
`SaldoPeps` es un tipo inmutable con la lista ordenada de capas y las operaciones de entrada y salida. Como `SaldoValorizado`, no conoce EF Core ni PostgreSQL, así que se puede comprobar con propiedades de FsCheck y no solo con ejemplos. Las dos invariantes que se prueban como propiedades, sobre secuencias arbitrarias de movimientos:
1. La cantidad del saldo es la suma firmada de los movimientos.
2. La valorización es igual a la suma de las capas, y **nunca negativa mientras el saldo sea positivo**.

Y la propiedad que distingue el método: con precios crecientes, PEPS deja un **inventario final más valorizado** que el promedio ponderado tras la misma secuencia. Si esa propiedad no se cumpliera, lo implementado no sería PEPS por mucho que se llame así.

## Consecuencias
- Migración `CapasCosto`: tabla nueva con RLS y la tabla `inventario.configuracion`. Las empresas existentes quedan en promedio ponderado, que es el comportamiento que ya tenían.
- El servicio se bifurca en un solo punto: resolver el costo de salida y actualizar el estado. El resto —bloqueo pesimista, inmutabilidad, lotes, FEFO— es común a los dos métodos.
- Lo que **no** trae: no hay UEPS (últimas entradas, primeras salidas), que la norma colombiana no admite para efectos fiscales; ni costo estándar con variaciones; ni retroactividad al cambiar de método, que es justamente lo que se impide.
