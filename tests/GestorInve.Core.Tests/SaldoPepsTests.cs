using GestorInve.Core;

namespace GestorInve.Core.Tests;

/// <summary>
/// Casos de libro de PEPS, contrastados contra el promedio ponderado. La comparación es lo
/// importante: un método que diera los mismos números que el promedio no sería PEPS por mucho
/// que se llame así, y el error pasaría inadvertido porque las cifras seguirían siendo
/// plausibles.
/// </summary>
public class SaldoPepsTests
{
    private static readonly Unidad Unidades = new("UND", 0);

    private static SaldoPeps Vacio() => SaldoPeps.Vacio(Unidades, "COP");

    private static Cantidad C(decimal valor) => new(valor, Unidades);

    private static Dinero P(decimal monto) => new(monto, "COP");

    [Fact]
    public void Una_salida_consume_la_capa_mas_antigua_primero()
    {
        var saldo = Vacio()
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(10m), P(2000m));

        var (restante, costo) = saldo.ConsumirSalida(C(6m));

        // 6 × 1.000: la capa vieja entera no se agota, y la nueva no se toca.
        Assert.Equal(6000m, costo.Monto);
        Assert.Equal(14m, restante.Cantidad.Valor);
        Assert.Equal(2, restante.Capas.Count);
        Assert.Equal(4m, restante.Capas[0].Cantidad.Valor);
        Assert.Equal(1000m, restante.Capas[0].CostoUnitario.Monto);
    }

    [Fact]
    public void Una_salida_que_abarca_varias_capas_cobra_el_ponderado_de_lo_consumido()
    {
        var saldo = Vacio()
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(20m), P(2000m));

        var (restante, costo) = saldo.ConsumirSalida(C(30m));

        // 10 × 1.000 + 20 × 2.000 = 50.000 exactos. Lo que importa es el total: el unitario
        // (1.666,666…) se deriva de él y el redondeo no puede comerse pesos del inventario.
        Assert.Equal(50_000m, costo.Monto);
        Assert.Equal(0m, restante.Cantidad.Valor);
        Assert.Empty(restante.Capas);
    }

    /// <summary>
    /// El caso que separa los dos métodos, con precios crecientes: PEPS saca primero lo barato,
    /// así que **el inventario final queda más valorizado** que con el promedio. Esa diferencia
    /// es la razón de ofrecer los dos métodos, porque cambia el impuesto.
    /// </summary>
    [Fact]
    public void Con_precios_crecientes_PEPS_deja_mas_valor_en_el_inventario_que_el_promedio()
    {
        var peps = Vacio()
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(10m), P(3000m));

        var promedio = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(10m), P(3000m));

        var (pepsRestante, costoPeps) = peps.ConsumirSalida(C(10m));
        var promedioRestante = promedio.AplicarSalida(C(10m));
        var costoPromedio = promedio.CostoDeSalida() * 10m;

        // PEPS saca las 10 baratas: 10.000 de costo y 30.000 en inventario.
        Assert.Equal(10_000m, costoPeps.Monto);
        Assert.Equal(30_000m, pepsRestante.Valorizado().Monto);

        // El promedio saca a 2.000: 20.000 de costo y 20.000 en inventario.
        Assert.Equal(20_000m, costoPromedio.Monto);
        Assert.Equal(20_000m, promedioRestante.Valorizado().Monto);

        Assert.True(pepsRestante.Valorizado().Monto > promedioRestante.Valorizado().Monto);
        Assert.True(costoPeps.Monto < costoPromedio.Monto);
    }

    [Fact]
    public void El_costo_implicito_es_el_valor_entre_la_cantidad()
    {
        var saldo = Vacio()
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(30m), P(2000m));

        // (10.000 + 60.000) / 40 = 1.750
        Assert.Equal(70_000m, saldo.Valorizado().Monto);
        Assert.Equal(1750m, saldo.CostoImplicito().Monto);
    }

    [Fact]
    public void Una_salida_forzada_agota_las_capas_y_valora_el_resto_al_ultimo_costo()
    {
        var saldo = Vacio().AplicarEntrada(C(10m), P(1500m));

        var (restante, costo) = saldo.ConsumirSalida(C(14m));

        // 10 de la capa más 4 al último costo conocido: no se inventa una capa negativa, que no
        // representaría ninguna mercancía.
        Assert.Equal(21_000m, costo.Monto);
        Assert.Equal(-4m, restante.Cantidad.Valor);
        Assert.Empty(restante.Capas);
    }

    [Fact]
    public void Con_saldo_negativo_la_entrada_reinicia_las_capas()
    {
        var (negativo, _) = Vacio()
            .AplicarEntrada(C(10m), P(1500m))
            .ConsumirSalida(C(14m));

        var reiniciado = negativo.AplicarEntrada(C(5m), P(900m));

        // La entrada **no** paga la deuda a su costo: el saldo negativo ya quedó asentado, y lo
        // que entra ahora empieza de cero. Si arrastrase el estado anterior, la valorización
        // quedaría absurda sin que nada avisara.
        Assert.Equal(5m, reiniciado.Cantidad.Valor);
        Assert.Equal(900m, reiniciado.CostoImplicito().Monto);
        Assert.Equal(4500m, reiniciado.Valorizado().Monto);
    }

    [Fact]
    public void Agotar_el_saldo_y_volver_a_entrar_tambien_reinicia()
    {
        var (agotado, _) = Vacio().AplicarEntrada(C(10m), P(1500m)).ConsumirSalida(C(10m));

        var despues = agotado.AplicarEntrada(C(2m), P(700m));

        Assert.Equal(700m, Assert.Single(despues.Capas).CostoUnitario.Monto);
        Assert.Equal(2m, despues.Cantidad.Valor);
    }

    [Fact]
    public void Las_capas_y_la_cantidad_registrada_tienen_que_cuadrar()
    {
        var saldo = Vacio().AplicarEntrada(C(10m), P(1000m));

        Assert.True(saldo.Cuadra());

        // Si las capas y la existencia se separan —por un error de persistencia, no por la
        // aritmética— tiene que poder verse en vez de pasar inadvertido.
        var torcido = SaldoPeps.De(saldo.Capas, C(7m), "COP");

        Assert.False(torcido.Cuadra());
    }

    [Fact]
    public void Un_saldo_negativo_cuadra_solo_si_no_quedan_capas()
    {
        var (negativo, _) = Vacio().AplicarEntrada(C(5m), P(100m)).ConsumirSalida(C(8m));

        Assert.True(negativo.Cuadra());
        Assert.False(SaldoPeps.De([new CapaCosto(C(1m), P(100m))], C(-3m), "COP").Cuadra());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Una_cantidad_no_positiva_se_rechaza(int valor)
    {
        var saldo = Vacio().AplicarEntrada(C(10m), P(1000m));

        Assert.Throws<ArgumentException>(() => saldo.AplicarEntrada(C(valor), P(100m)));
        Assert.Throws<ArgumentException>(() => saldo.ConsumirSalida(C(valor)));
    }

    [Fact]
    public void Un_costo_negativo_se_rechaza() =>
        Assert.Throws<ArgumentException>(() => Vacio().AplicarEntrada(C(1m), P(-1m)));
}
