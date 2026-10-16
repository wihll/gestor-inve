namespace GestorInve.Core;

/// <summary>
/// Saldo de existencias con su costo promedio ponderado. Es el estado que el kardex va
/// arrastrando movimiento a movimiento, y **toda la aritmética del costeo vive aquí**: sin ORM,
/// sin base de datos y sin HTTP, para poder comprobarla con propiedades y no solo con ejemplos.
///
/// Dos reglas que no son obvias y que se prueban:
/// - **Una salida no cambia el promedio.** Sacar mercancía no altera lo que costó la que queda.
/// - **Con saldo ≤ 0, una entrada reinicia el promedio** a su propio costo. La fórmula ponderada
///   divide por el saldo resultante: con saldo cero o negativo da resultados absurdos o cambia
///   de signo, y ese es el camino por el que una valorización se vuelve basura sin avisar.
/// </summary>
public readonly record struct SaldoValorizado
{
    private SaldoValorizado(Cantidad cantidad, Dinero costoPromedio)
    {
        Cantidad = cantidad;
        CostoPromedio = costoPromedio;
    }

    /// <summary>Decimales del costo promedio. Más que el dinero a propósito: redondear en cada paso acumula deriva.</summary>
    public const int DecimalesCosto = 6;

    public Cantidad Cantidad { get; }

    /// <summary>Costo promedio por unidad, con <see cref="DecimalesCosto"/> decimales.</summary>
    public Dinero CostoPromedio { get; }

    public static SaldoValorizado Vacio(Unidad unidad, string moneda) =>
        new(new Cantidad(0m, unidad), new Dinero(0m, moneda));

    /// <summary>Reconstruye un saldo ya conocido (el guardado en la fila de existencias).</summary>
    public static SaldoValorizado De(Cantidad cantidad, Dinero costoPromedio)
    {
        if (costoPromedio.Monto < 0m)
        {
            throw new ArgumentException("El costo promedio no puede ser negativo.", nameof(costoPromedio));
        }

        return new SaldoValorizado(cantidad, costoPromedio);
    }

    /// <summary>Costo al que debe salir la mercancía: el promedio vigente.</summary>
    public Dinero CostoDeSalida() => CostoPromedio;

    /// <summary>Valor en libros de las existencias.</summary>
    public Dinero Valorizado() => (CostoPromedio * Cantidad.Valor).Redondear();

    public SaldoValorizado AplicarEntrada(Cantidad entrada, Dinero costoUnitario)
    {
        ExigirPositiva(entrada, nameof(entrada));
        ExigirMismaUnidad(entrada);
        ExigirMismaMoneda(costoUnitario);

        if (costoUnitario.Monto < 0m)
        {
            throw new ArgumentException("El costo unitario no puede ser negativo.", nameof(costoUnitario));
        }

        var nuevaCantidad = Cantidad + entrada;

        // Saldo no positivo: la fórmula ponderada no tiene sentido, se parte del costo de esta entrada.
        if (Cantidad.Valor <= 0m)
        {
            return new SaldoValorizado(nuevaCantidad, Redondear(costoUnitario));
        }

        var valorActual = CostoPromedio.Monto * Cantidad.Valor;
        var valorEntrada = costoUnitario.Monto * entrada.Valor;
        var promedio = (valorActual + valorEntrada) / nuevaCantidad.Valor;

        return new SaldoValorizado(nuevaCantidad, Redondear(new Dinero(promedio, costoUnitario.Moneda)));
    }

    /// <summary>Salida normal: se rechaza si no hay existencias suficientes.</summary>
    public SaldoValorizado AplicarSalida(Cantidad salida)
    {
        ExigirPositiva(salida, nameof(salida));
        ExigirMismaUnidad(salida);

        if (salida.Valor > Cantidad.Valor)
        {
            throw new InvalidOperationException(
                $"No hay existencias suficientes: hay {Cantidad.Valor} y se piden {salida.Valor}.");
        }

        return new SaldoValorizado(Cantidad - salida, CostoPromedio);
    }

    /// <summary>
    /// Salida que puede dejar el saldo negativo. Solo para quien tiene el permiso de ajuste:
    /// cubre a la pyme que vende antes de registrar la compra, y el movimiento queda marcado
    /// para que el descuadre sea visible y no silencioso.
    /// </summary>
    public SaldoValorizado AplicarSalidaForzada(Cantidad salida)
    {
        ExigirPositiva(salida, nameof(salida));
        ExigirMismaUnidad(salida);

        return new SaldoValorizado(Cantidad - salida, CostoPromedio);
    }

    private static Dinero Redondear(Dinero costo) => costo.Redondear(DecimalesCosto);

    private static void ExigirPositiva(Cantidad cantidad, string nombre)
    {
        if (cantidad.Valor <= 0m)
        {
            throw new ArgumentException("La cantidad del movimiento debe ser positiva.", nombre);
        }
    }

    private void ExigirMismaUnidad(Cantidad otra)
    {
        if (!Cantidad.EsIndefinida && !otra.EsIndefinida && Cantidad.Unidad.Codigo != otra.Unidad.Codigo)
        {
            throw new InvalidOperationException(
                $"El saldo está en {Cantidad.Unidad.Codigo} y el movimiento en {otra.Unidad.Codigo}.");
        }
    }

    private void ExigirMismaMoneda(Dinero otro)
    {
        if (!CostoPromedio.EsIndefinido && !otro.EsIndefinido && CostoPromedio.Moneda != otro.Moneda)
        {
            throw new InvalidOperationException(
                $"El saldo está en {CostoPromedio.Moneda} y el costo en {otro.Moneda}.");
        }
    }
}
