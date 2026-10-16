using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using GestorInve.Core;

namespace GestorInve.Core.Tests;

/// <summary>
/// Propiedades del costeo promedio con valores generados. Un error aquí no se ve: produce
/// valorizaciones plausibles pero falsas, que solo aparecen meses después al cuadrar el
/// inventario contra la contabilidad. Los ejemplos escritos a mano no bastan.
/// </summary>
public class SaldoValorizadoPropiedadesTests
{
    private static readonly Unidad Unidades = new("UND", 0);

    /// <summary>Entradas con cantidad entera positiva y costo positivo acotado.</summary>
    private static Arbitrary<(decimal Cantidad, decimal Costo)> Entradas() =>
        ArbMap.Default.ArbFor<int>()
            .Generator
            .Select(n => (Math.Abs(n % 500) + 1, Math.Abs(n % 90_000) + 10))
            .Select(p => ((decimal)p.Item1, (decimal)p.Item2))
            .ToArbitrary();

    private static SaldoValorizado Vacio() => SaldoValorizado.Vacio(Unidades, "COP");

    private static SaldoValorizado Aplicar(SaldoValorizado saldo, (decimal Cantidad, decimal Costo) entrada) =>
        saldo.AplicarEntrada(new Cantidad(entrada.Cantidad, Unidades), new Dinero(entrada.Costo, "COP"));

    /// <summary>
    /// La propiedad central del promedio ponderado: el resultado nunca puede quedar fuera del
    /// rango de los costos que lo formaron. Si queda fuera, la fórmula está mal.
    /// </summary>
    [Property]
    public Property El_promedio_queda_entre_el_costo_minimo_y_el_maximo()
    {
        return Prop.ForAll(
            Gen.ListOf(Entradas().Generator).ToArbitrary(),
            entradas =>
            {
                var lista = entradas.ToList();

                if (lista.Count == 0)
                {
                    return true;
                }

                var saldo = lista.Aggregate(Vacio(), Aplicar);

                var minimo = lista.Min(e => e.Costo);
                var maximo = lista.Max(e => e.Costo);

                // Margen de un centésimo por el redondeo a seis decimales del promedio.
                return saldo.CostoPromedio.Monto >= minimo - 0.01m
                       && saldo.CostoPromedio.Monto <= maximo + 0.01m;
            });
    }

    [Property]
    public Property La_cantidad_es_la_suma_de_las_entradas()
    {
        return Prop.ForAll(
            Gen.ListOf(Entradas().Generator).ToArbitrary(),
            entradas =>
            {
                var lista = entradas.ToList();
                var saldo = lista.Aggregate(Vacio(), Aplicar);

                return saldo.Cantidad.Valor == lista.Sum(e => e.Cantidad);
            });
    }

    [Property]
    public Property Una_salida_nunca_cambia_el_promedio()
    {
        return Prop.ForAll(
            Entradas(),
            ArbMap.Default.ArbFor<int>(),
            (entrada, bruto) =>
            {
                var saldo = Aplicar(Vacio(), entrada);
                var salida = (Math.Abs(bruto) % (int)entrada.Cantidad) + 1;

                var despues = saldo.AplicarSalida(new Cantidad(salida, Unidades));

                return despues.CostoPromedio.Monto == saldo.CostoPromedio.Monto
                       && despues.Cantidad.Valor == entrada.Cantidad - salida;
            });
    }

    [Property]
    public Property El_promedio_nunca_es_negativo()
    {
        return Prop.ForAll(
            Gen.ListOf(Entradas().Generator).ToArbitrary(),
            entradas => entradas.Aggregate(Vacio(), Aplicar).CostoPromedio.Monto >= 0m);
    }

    /// <summary>Repetir la misma entrada no debe mover el promedio: ya es ese costo.</summary>
    [Property]
    public Property Entradas_al_mismo_costo_dejan_el_promedio_en_ese_costo()
    {
        return Prop.ForAll(
            Entradas(),
            ArbMap.Default.ArbFor<int>(),
            (entrada, repeticiones) =>
            {
                var veces = (Math.Abs(repeticiones) % 20) + 1;
                var saldo = Vacio();

                for (var i = 0; i < veces; i++)
                {
                    saldo = Aplicar(saldo, entrada);
                }

                return Math.Abs(saldo.CostoPromedio.Monto - entrada.Costo) < 0.000_01m;
            });
    }

    [Property]
    public Property Entrar_y_sacar_lo_mismo_devuelve_la_cantidad_previa()
    {
        return Prop.ForAll(
            Entradas(),
            Entradas(),
            (primera, segunda) =>
            {
                var saldo = Aplicar(Vacio(), primera);
                var cantidadPrevia = saldo.Cantidad.Valor;

                var ida = Aplicar(saldo, segunda);
                var vuelta = ida.AplicarSalida(new Cantidad(segunda.Cantidad, Unidades));

                return vuelta.Cantidad.Valor == cantidadPrevia;
            });
    }

    /// <summary>Tras agotar el saldo, la entrada siguiente manda: no arrastra el promedio viejo.</summary>
    [Property]
    public Property Agotar_y_volver_a_entrar_reinicia_el_promedio()
    {
        return Prop.ForAll(
            Entradas(),
            Entradas(),
            (primera, segunda) =>
            {
                var agotado = Aplicar(Vacio(), primera)
                    .AplicarSalida(new Cantidad(primera.Cantidad, Unidades));

                var reiniciado = Aplicar(agotado, segunda);

                return reiniciado.CostoPromedio.Monto == segunda.Costo
                       && reiniciado.Cantidad.Valor == segunda.Cantidad;
            });
    }

    [Property]
    public Property El_valorizado_es_cantidad_por_promedio()
    {
        return Prop.ForAll(
            Gen.ListOf(Entradas().Generator).ToArbitrary(),
            entradas =>
            {
                var saldo = entradas.Aggregate(Vacio(), Aplicar);
                var esperado = Math.Round(
                    saldo.CostoPromedio.Monto * saldo.Cantidad.Valor,
                    2,
                    MidpointRounding.AwayFromZero);

                return saldo.Valorizado().Monto == esperado;
            });
    }

    [Property]
    public Property Nunca_se_puede_sacar_mas_de_lo_que_hay_sin_forzar()
    {
        return Prop.ForAll(
            Entradas(),
            entrada =>
            {
                var saldo = Aplicar(Vacio(), entrada);
                var exceso = new Cantidad(entrada.Cantidad + 1m, Unidades);

                try
                {
                    saldo.AplicarSalida(exceso);
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            });
    }

    /// <summary>
    /// El camino que de verdad justifica la regla de reinicio: con saldo **negativo**, la
    /// fórmula ponderada divide por un resultado que puede acercarse a cero o cambiar de signo.
    /// Agotar el saldo no basta para destapar el fallo (ahí la fórmula da el mismo número),
    /// así que esta propiedad fuerza el negativo a propósito.
    /// </summary>
    [Property]
    public Property Con_saldo_negativo_la_entrada_siguiente_fija_su_propio_costo()
    {
        return Prop.ForAll(
            Entradas(),
            Entradas(),
            ArbMap.Default.ArbFor<int>(),
            (primera, segunda, bruto) =>
            {
                var saldo = Aplicar(Vacio(), primera);

                // Se saca más de lo que hay: el saldo queda negativo.
                var exceso = primera.Cantidad + (Math.Abs(bruto) % 50) + 1;
                var negativo = saldo.AplicarSalidaForzada(new Cantidad(exceso, Unidades));

                if (negativo.Cantidad.Valor >= 0m)
                {
                    return true;
                }

                var reiniciado = Aplicar(negativo, segunda);

                return reiniciado.CostoPromedio.Monto == segunda.Costo
                       && reiniciado.Cantidad.Valor == negativo.Cantidad.Valor + segunda.Cantidad;
            });
    }

    /// <summary>Con saldo negativo, el promedio tampoco puede volverse negativo ni absurdo.</summary>
    [Property]
    public Property El_promedio_sigue_siendo_sano_tras_pasar_por_negativo()
    {
        return Prop.ForAll(
            Entradas(),
            Entradas(),
            (primera, segunda) =>
            {
                var saldo = Aplicar(Vacio(), primera)
                    .AplicarSalidaForzada(new Cantidad(primera.Cantidad * 2m, Unidades));

                var despues = Aplicar(saldo, segunda);

                return despues.CostoPromedio.Monto >= 0m
                       && despues.CostoPromedio.Monto <= Math.Max(primera.Costo, segunda.Costo);
            });
    }
}
