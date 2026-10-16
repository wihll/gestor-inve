using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using GestorInve.Core;

namespace GestorInve.Core.Tests;

/// <summary>
/// Propiedades de PEPS sobre secuencias generadas de movimientos. Un error en el consumo de
/// capas no se ve: produce un costo de ventas plausible pero falso, que acaba en la declaración
/// de renta y solo aparece al cuadrar el inventario contra la contabilidad meses después.
/// </summary>
public class SaldoPepsPropiedadesTests
{
    private static readonly Unidad Unidades = new("UND", 0);

    /// <summary>Un movimiento generado: entrada con costo, o salida.</summary>
    private sealed record Movimiento(bool EsEntrada, decimal Cantidad, decimal Costo);

    private static Arbitrary<Movimiento> Movimientos() =>
        ArbMap.Default.ArbFor<int>()
            .Generator
            .Select(n => new Movimiento(
                EsEntrada: n % 3 != 0,
                Cantidad: (Math.Abs(n % 200) + 1),
                Costo: (Math.Abs(n % 80_000) + 10)))
            .ToArbitrary();

    private static SaldoPeps Vacio() => SaldoPeps.Vacio(Unidades, "COP");

    private static Cantidad C(decimal valor) => new(valor, Unidades);

    /// <summary>
    /// Aplica la secuencia **sin forzar negativos**: una salida mayor que el saldo se recorta, que
    /// es lo que hace el servicio cuando no hay permiso de ajuste. Devuelve también el costo
    /// acumulado de las salidas, que es el costo de ventas del periodo.
    /// </summary>
    private static (SaldoPeps Saldo, decimal CostoDeSalidas, decimal Entradas, decimal Salidas) Aplicar(
        IEnumerable<Movimiento> movimientos)
    {
        var saldo = Vacio();
        var costoSalidas = 0m;
        var entradas = 0m;
        var salidas = 0m;

        foreach (var m in movimientos)
        {
            if (m.EsEntrada)
            {
                saldo = saldo.AplicarEntrada(C(m.Cantidad), new Dinero(m.Costo, "COP"));
                entradas += m.Cantidad;
                continue;
            }

            var disponible = saldo.Cantidad.Valor;

            if (disponible <= 0m)
            {
                continue;
            }

            var sacar = Math.Min(m.Cantidad, disponible);
            var (restante, costo) = saldo.ConsumirSalida(C(sacar));

            saldo = restante;
            costoSalidas += costo.Monto;
            salidas += sacar;
        }

        return (saldo, costoSalidas, entradas, salidas);
    }

    [Property]
    public Property La_cantidad_es_la_suma_firmada_de_los_movimientos()
    {
        return Prop.ForAll(
            Gen.ListOf(Movimientos().Generator).ToArbitrary(),
            movimientos =>
            {
                var (saldo, _, entradas, salidas) = Aplicar(movimientos);

                return saldo.Cantidad.Valor == entradas - salidas;
            });
    }

    /// <summary>
    /// La invariante que amarra las capas al saldo: si se separan, la valorización miente y
    /// nada lo avisa. Es la que justifica guardar las capas como estado persistido.
    /// </summary>
    [Property]
    public Property Las_capas_siempre_cuadran_con_el_saldo()
    {
        return Prop.ForAll(
            Gen.ListOf(Movimientos().Generator).ToArbitrary(),
            movimientos => Aplicar(movimientos).Saldo.Cuadra());
    }

    [Property]
    public Property El_valorizado_es_la_suma_de_las_capas_y_nunca_negativo()
    {
        return Prop.ForAll(
            Gen.ListOf(Movimientos().Generator).ToArbitrary(),
            movimientos =>
            {
                var saldo = Aplicar(movimientos).Saldo;

                var suma = Math.Round(
                    saldo.Capas.Sum(c => c.CostoUnitario.Monto * c.Cantidad.Valor),
                    2,
                    MidpointRounding.AwayFromZero);

                return saldo.Valorizado().Monto == suma && saldo.Valorizado().Monto >= 0m;
            });
    }

    /// <summary>
    /// Conservación del valor: lo que entró es igual a lo que salió más lo que queda. Es la
    /// comprobación contable de que ningún peso se crea ni se pierde al consumir capas, que es
    /// justo donde un redondeo mal puesto hace daño.
    /// </summary>
    [Property]
    public Property El_valor_que_entra_es_el_que_sale_mas_el_que_queda()
    {
        return Prop.ForAll(
            Gen.ListOf(Movimientos().Generator).ToArbitrary(),
            movimientos =>
            {
                var lista = movimientos.ToList();
                var (saldo, costoSalidas, _, _) = Aplicar(lista);

                // Solo vale mientras el saldo no haya pasado por cero: el reinicio de capas
                // descarta el estado anterior a propósito, así que ahí la igualdad no aplica.
                var paso = Vacio();
                var reinicio = false;

                foreach (var m in lista)
                {
                    if (m.EsEntrada)
                    {
                        if (paso.Cantidad.Valor <= 0m && paso.Capas.Count == 0 && paso != Vacio())
                        {
                            reinicio = true;
                        }

                        paso = paso.AplicarEntrada(C(m.Cantidad), new Dinero(m.Costo, "COP"));
                        continue;
                    }

                    var disponible = paso.Cantidad.Valor;

                    if (disponible <= 0m)
                    {
                        continue;
                    }

                    paso = paso.ConsumirSalida(C(Math.Min(m.Cantidad, disponible))).Restante;

                    if (paso.Cantidad.Valor == 0m)
                    {
                        reinicio = true;
                    }
                }

                if (reinicio)
                {
                    return true;
                }

                var valorEntradas = lista.Where(m => m.EsEntrada).Sum(m => m.Cantidad * m.Costo);

                return Math.Abs(valorEntradas - (costoSalidas + saldo.Valorizado().Monto)) < 0.05m;
            });
    }

    /// <summary>
    /// La propiedad que **distingue el método**. Con precios estrictamente crecientes, PEPS saca
    /// primero lo barato, así que su costo de ventas es menor o igual que el del promedio y su
    /// inventario final mayor o igual. Si esto no se cumpliera, lo implementado no sería PEPS por
    /// mucho que se llame así, y las cifras seguirían pareciendo razonables.
    /// </summary>
    [Property]
    public Property Con_precios_crecientes_PEPS_cuesta_menos_que_el_promedio()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<int>(),
            ArbMap.Default.ArbFor<int>(),
            ArbMap.Default.ArbFor<int>(),
            (bruto1, bruto2, brutoSalida) =>
            {
                var cantidad1 = (Math.Abs(bruto1) % 100) + 1;
                var cantidad2 = (Math.Abs(bruto2) % 100) + 1;
                var costo1 = (Math.Abs(bruto1) % 5_000) + 100;
                var costo2 = costo1 + (Math.Abs(bruto2) % 5_000) + 1; // estrictamente mayor
                var salida = (Math.Abs(brutoSalida) % (cantidad1 + cantidad2)) + 1;

                var peps = Vacio()
                    .AplicarEntrada(C(cantidad1), new Dinero(costo1, "COP"))
                    .AplicarEntrada(C(cantidad2), new Dinero(costo2, "COP"));

                var promedio = SaldoValorizado.Vacio(Unidades, "COP")
                    .AplicarEntrada(C(cantidad1), new Dinero(costo1, "COP"))
                    .AplicarEntrada(C(cantidad2), new Dinero(costo2, "COP"));

                var (restantePeps, costoPeps) = peps.ConsumirSalida(C(salida));
                var costoPromedio = (promedio.CostoDeSalida() * salida).Redondear();
                var restantePromedio = promedio.AplicarSalida(C(salida));

                // Margen de un peso por los redondeos de cada lado.
                return costoPeps.Monto <= costoPromedio.Monto + 1m
                       && restantePeps.Valorizado().Monto >= restantePromedio.Valorizado().Monto - 1m;
            });
    }

    /// <summary>
    /// Y el espejo: con precios **decrecientes** la relación se invierte. Sin esta, la propiedad
    /// anterior la cumpliría también un método que simplemente cobrara siempre barato.
    /// </summary>
    [Property]
    public Property Con_precios_decrecientes_PEPS_cuesta_mas_que_el_promedio()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<int>(),
            ArbMap.Default.ArbFor<int>(),
            ArbMap.Default.ArbFor<int>(),
            (bruto1, bruto2, brutoSalida) =>
            {
                var cantidad1 = (Math.Abs(bruto1) % 100) + 1;
                var cantidad2 = (Math.Abs(bruto2) % 100) + 1;
                var costo1 = (Math.Abs(bruto1) % 5_000) + 5_100;
                var costo2 = costo1 - ((Math.Abs(bruto2) % 5_000) + 1); // estrictamente menor
                var salida = (Math.Abs(brutoSalida) % (cantidad1 + cantidad2)) + 1;

                var peps = Vacio()
                    .AplicarEntrada(C(cantidad1), new Dinero(costo1, "COP"))
                    .AplicarEntrada(C(cantidad2), new Dinero(costo2, "COP"));

                var promedio = SaldoValorizado.Vacio(Unidades, "COP")
                    .AplicarEntrada(C(cantidad1), new Dinero(costo1, "COP"))
                    .AplicarEntrada(C(cantidad2), new Dinero(costo2, "COP"));

                var (_, costoPeps) = peps.ConsumirSalida(C(salida));
                var costoPromedio = (promedio.CostoDeSalida() * salida).Redondear();

                return costoPeps.Monto >= costoPromedio.Monto - 1m;
            });
    }

    /// <summary>
    /// Sacar todo de una vez cuesta exactamente lo mismo que sacarlo de a poco. Si no fuera así,
    /// el costo de ventas dependería de cómo se trocearon los despachos, que es un detalle
    /// operativo y no un hecho contable.
    /// </summary>
    [Property]
    public Property Trocear_una_salida_no_cambia_su_costo()
    {
        return Prop.ForAll(
            Gen.ListOf(Movimientos().Generator).ToArbitrary(),
            ArbMap.Default.ArbFor<int>(),
            (movimientos, brutoCorte) =>
            {
                var saldo = Vacio();

                foreach (var m in movimientos.Where(m => m.EsEntrada))
                {
                    saldo = saldo.AplicarEntrada(C(m.Cantidad), new Dinero(m.Costo, "COP"));
                }

                if (saldo.Cantidad.Valor < 2m)
                {
                    return true;
                }

                var total = saldo.Cantidad.Valor;
                var primera = (Math.Abs(brutoCorte) % (int)(total - 1m)) + 1;

                var deUnaVez = saldo.ConsumirSalida(C(total)).CostoTotal.Monto;

                var paso1 = saldo.ConsumirSalida(C(primera));
                var paso2 = paso1.Restante.ConsumirSalida(C(total - primera));
                var enDosPasos = paso1.CostoTotal.Monto + paso2.CostoTotal.Monto;

                return Math.Abs(deUnaVez - enDosPasos) < 0.05m;
            });
    }
}
