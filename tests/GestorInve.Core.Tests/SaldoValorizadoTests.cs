using GestorInve.Core;

namespace GestorInve.Core.Tests;

public class SaldoValorizadoTests
{
    private static readonly Unidad Unidades = new("UND", 0);

    private static Cantidad C(decimal valor) => new(valor, Unidades);

    private static Dinero P(decimal monto) => new(monto, "COP");

    [Fact]
    public void Un_saldo_nuevo_esta_en_cero()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP");

        Assert.Equal(0m, saldo.Cantidad.Valor);
        Assert.Equal(0m, saldo.CostoPromedio.Monto);
    }

    [Fact]
    public void La_primera_entrada_fija_el_promedio_al_costo_de_compra()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP").AplicarEntrada(C(10m), P(1000m));

        Assert.Equal(10m, saldo.Cantidad.Valor);
        Assert.Equal(1000m, saldo.CostoPromedio.Monto);
    }

    /// <summary>El caso de libro: 10 a 1000 y 10 a 2000 dejan el promedio en 1500.</summary>
    [Fact]
    public void Dos_entradas_a_distinto_costo_promedian_ponderando_por_cantidad()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(10m), P(2000m));

        Assert.Equal(20m, saldo.Cantidad.Valor);
        Assert.Equal(1500m, saldo.CostoPromedio.Monto);
    }

    [Fact]
    public void El_promedio_pondera_de_verdad_cuando_las_cantidades_difieren()
    {
        // 30 a 1000 y 10 a 2000 → (30000 + 20000) / 40 = 1250
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(30m), P(1000m))
            .AplicarEntrada(C(10m), P(2000m));

        Assert.Equal(1250m, saldo.CostoPromedio.Monto);
    }

    /// <summary>Sacar mercancía no cambia lo que costó la que queda.</summary>
    [Fact]
    public void Una_salida_no_altera_el_promedio()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(10m), P(2000m));

        var despues = saldo.AplicarSalida(C(15m));

        Assert.Equal(5m, despues.Cantidad.Valor);
        Assert.Equal(1500m, despues.CostoPromedio.Monto);
    }

    [Fact]
    public void La_salida_sale_al_promedio_vigente()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarEntrada(C(10m), P(2000m));

        Assert.Equal(1500m, saldo.CostoDeSalida().Monto);
    }

    /// <summary>
    /// La trampa del método: con saldo cero o negativo, la fórmula ponderada divide por casi
    /// nada o cambia de signo. El promedio se reinicia al costo de la entrada.
    /// </summary>
    [Fact]
    public void Con_saldo_en_cero_la_entrada_reinicia_el_promedio()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(10m), P(1000m))
            .AplicarSalida(C(10m));

        Assert.Equal(0m, saldo.Cantidad.Valor);

        var reiniciado = saldo.AplicarEntrada(C(5m), P(3000m));

        Assert.Equal(5m, reiniciado.Cantidad.Valor);
        Assert.Equal(3000m, reiniciado.CostoPromedio.Monto);
    }

    [Fact]
    public void Con_saldo_negativo_la_entrada_tambien_reinicia_el_promedio()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(5m), P(1000m))
            .AplicarSalidaForzada(C(8m));

        Assert.Equal(-3m, saldo.Cantidad.Valor);

        var reiniciado = saldo.AplicarEntrada(C(10m), P(2500m));

        Assert.Equal(7m, reiniciado.Cantidad.Valor);
        Assert.Equal(2500m, reiniciado.CostoPromedio.Monto);
    }

    [Fact]
    public void Una_salida_mayor_que_el_saldo_se_rechaza()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP").AplicarEntrada(C(5m), P(1000m));

        var error = Assert.Throws<InvalidOperationException>(() => saldo.AplicarSalida(C(6m)));

        Assert.Contains("existencias", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_salida_forzada_si_puede_dejar_el_saldo_negativo()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP").AplicarEntrada(C(5m), P(1000m));

        var forzado = saldo.AplicarSalidaForzada(C(8m));

        Assert.Equal(-3m, forzado.Cantidad.Valor);
        Assert.Equal(1000m, forzado.CostoPromedio.Monto);
    }

    [Fact]
    public void Puede_salir_exactamente_todo_el_saldo()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(5m), P(1000m))
            .AplicarSalida(C(5m));

        Assert.Equal(0m, saldo.Cantidad.Valor);
    }

    [Fact]
    public void Las_cantidades_deben_ser_positivas()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP");

        Assert.Throws<ArgumentException>(() => saldo.AplicarEntrada(C(0m), P(100m)));
        Assert.Throws<ArgumentException>(() => saldo.AplicarEntrada(C(-1m), P(100m)));
        Assert.Throws<ArgumentException>(() => saldo.AplicarSalida(C(0m)));
        Assert.Throws<ArgumentException>(() => saldo.AplicarSalida(C(-1m)));
    }

    [Fact]
    public void Un_costo_negativo_se_rechaza()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP");

        Assert.Throws<ArgumentException>(() => saldo.AplicarEntrada(C(1m), P(-1m)));
    }

    [Fact]
    public void No_se_pueden_mezclar_unidades_ni_monedas()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP");

        Assert.Throws<InvalidOperationException>(() => saldo.AplicarEntrada(new Cantidad(1m, new Unidad("KG", 3)), P(10m)));
        Assert.Throws<InvalidOperationException>(() => saldo.AplicarEntrada(C(1m), new Dinero(10m, "USD")));
    }

    /// <summary>
    /// El promedio se guarda con más decimales que el dinero: redondear a dos en cada paso
    /// arrastra deriva, y en un kardex de miles de movimientos eso descuadra la valorización.
    /// </summary>
    [Fact]
    public void El_promedio_conserva_decimales_que_el_dinero_redondearia()
    {
        // 3 a 1000 y 1 a 1001 → 4000,75 / 4 = 1000,1875
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(3m), P(1000m))
            .AplicarEntrada(C(1m), P(1000.75m));

        Assert.Equal(1000.1875m, saldo.CostoPromedio.Monto);
    }

    [Fact]
    public void El_valor_total_es_cantidad_por_promedio()
    {
        var saldo = SaldoValorizado.Vacio(Unidades, "COP")
            .AplicarEntrada(C(10m), P(1500m));

        Assert.Equal(15000m, saldo.Valorizado().Monto);
    }

    /// <summary>
    /// `De` reconstruye el saldo guardado en la fila de existencias. Es la puerta por la que
    /// entran datos de la base al cálculo, así que valida lo que no puede ser cierto.
    /// </summary>
    [Fact]
    public void De_reconstruye_un_saldo_ya_conocido()
    {
        var saldo = SaldoValorizado.De(C(7m), P(1234.5m));

        Assert.Equal(7m, saldo.Cantidad.Valor);
        Assert.Equal(1234.5m, saldo.CostoPromedio.Monto);
        Assert.Equal(8641.5m, saldo.Valorizado().Monto);
    }

    [Fact]
    public void De_rechaza_un_costo_promedio_negativo()
    {
        var error = Assert.Throws<ArgumentException>(() => SaldoValorizado.De(C(1m), P(-0.01m)));

        Assert.Contains("negativo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void De_admite_un_saldo_negativo_porque_una_salida_forzada_lo_produce()
    {
        var saldo = SaldoValorizado.De(C(-3m), P(1000m));

        Assert.Equal(-3m, saldo.Cantidad.Valor);
    }
}
