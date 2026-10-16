using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using GestorInve.Core;

namespace GestorInve.Core.Tests;

/// <summary>
/// Propiedades de <see cref="Cantidad"/> comprobadas con valores generados, no elegidos a mano.
/// El redondeo y la escala son justo donde los casos escritos por una persona se quedan cortos:
/// los errores de inventario aparecen con los valores raros, no con 2 + 2.
/// </summary>
public class CantidadPropiedadesTests
{
    /// <summary>Genera cantidades válidas: un valor acotado y una unidad con decimales coherentes.</summary>
    private static Arbitrary<Cantidad> CantidadesDe(int decimales) =>
        ArbMap.Default.ArbFor<decimal>()
            .Filter(v => Math.Abs(v) < 1_000_000_000m)
            .Generator
            .Select(v => new Cantidad(Math.Round(v, decimales, MidpointRounding.AwayFromZero), new Unidad("KG", decimales)))
            .ToArbitrary();

    [Property]
    public Property La_escala_nunca_excede_los_decimales_de_la_unidad()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<int>().Filter(d => d is >= 0 and <= Unidad.DecimalesMaximos),
            ArbMap.Default.ArbFor<decimal>().Filter(v => Math.Abs(v) < 1_000_000_000m),
            (decimales, valor) =>
            {
                var unidad = new Unidad("KG", decimales);
                var redondeado = Math.Round(valor, decimales, MidpointRounding.AwayFromZero);

                // Construir con el valor ya redondeado nunca debe fallar.
                var cantidad = new Cantidad(redondeado, unidad);

                return cantidad.Valor == redondeado;
            });
    }

    [Property]
    public Property Un_valor_con_mas_decimales_de_los_permitidos_siempre_se_rechaza()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<int>().Filter(d => d is >= 0 and < Unidad.DecimalesMaximos),
            (decimales) =>
            {
                var unidad = new Unidad("KG", decimales);

                // Un uno en la posición siguiente a la última permitida: siempre sobra.
                var excedido = 1m + (1m / Pow10(decimales + 1));

                try
                {
                    _ = new Cantidad(excedido, unidad);
                    return false;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            });
    }

    [Property]
    public Property La_suma_es_conmutativa()
    {
        return Prop.ForAll(CantidadesDe(3), CantidadesDe(3), (a, b) => (a + b).Valor == (b + a).Valor);
    }

    [Property]
    public Property La_suma_es_asociativa_y_exacta()
    {
        return Prop.ForAll(
            CantidadesDe(3),
            CantidadesDe(3),
            CantidadesDe(3),
            (a, b, c) => ((a + b) + c).Valor == (a + (b + c)).Valor);
    }

    [Property]
    public Property Default_es_neutro_por_ambos_lados()
    {
        return Prop.ForAll(CantidadesDe(3), a =>
            (a + default(Cantidad)).Valor == a.Valor
            && (default(Cantidad) + a).Valor == a.Valor
            && (a + default(Cantidad)).Unidad.Codigo == a.Unidad.Codigo);
    }

    [Property]
    public Property Restar_lo_mismo_siempre_da_cero()
    {
        return Prop.ForAll(CantidadesDe(3), a => (a - a).Valor == 0m);
    }

    [Property]
    public Property Normalizar_es_idempotente()
    {
        return Prop.ForAll(CantidadesDe(2), a =>
        {
            var una = a.Normalizar();

            return una.Normalizar().Valor == una.Valor;
        });
    }

    [Property]
    public Property Multiplicar_mantiene_la_escala_dentro_de_la_unidad()
    {
        return Prop.ForAll(
            CantidadesDe(0),
            ArbMap.Default.ArbFor<decimal>().Filter(f => Math.Abs(f) < 1000m),
            (a, factor) =>
            {
                var resultado = a * factor;

                // Unidad sin decimales: el resultado tiene que ser entero.
                return resultado.Valor == decimal.Truncate(resultado.Valor);
            });
    }

    [Property]
    public Property Multiplicar_por_uno_no_cambia_el_valor()
    {
        return Prop.ForAll(CantidadesDe(3), a => (a * 1m).Valor == a.Valor);
    }

    [Property]
    public Property El_signo_se_refleja_en_EsNegativa()
    {
        return Prop.ForAll(CantidadesDe(3), a => a.EsNegativa == a.Valor < 0m);
    }

    private static decimal Pow10(int exponente)
    {
        var resultado = 1m;
        for (var i = 0; i < exponente; i++)
        {
            resultado *= 10m;
        }

        return resultado;
    }
}
