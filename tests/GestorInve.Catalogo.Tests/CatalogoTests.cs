using GestorInve.Catalogo;

namespace GestorInve.Catalogo.Tests;

public class VarianteTests
{
    private static readonly Guid Unidad = Guid.CreateVersion7();

    private static Variante VarianteUnica() =>
        Producto.Crear("mart-001", "Martillo", Unidad).Variantes.Single();

    [Fact]
    public void Un_codigo_de_barras_se_normaliza()
    {
        var variante = VarianteUnica();

        var codigo = variante.AgregarCodigo("  7 702 001 234567 ");

        Assert.Equal("7702001234567", codigo.Codigo);
    }

    [Fact]
    public void El_mismo_codigo_no_se_repite_en_la_variante()
    {
        var variante = VarianteUnica();
        variante.AgregarCodigo("7702001234567");

        Assert.Throws<InvalidOperationException>(() => variante.AgregarCodigo("7702001234567"));
    }

    [Fact]
    public void Una_variante_puede_tener_varios_codigos()
    {
        var variante = VarianteUnica();

        variante.AgregarCodigo("7702001234567");
        variante.AgregarCodigo("0000123456789");

        Assert.Equal(2, variante.Codigos.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public void Un_codigo_vacio_se_rechaza(string codigo)
    {
        Assert.ThrowsAny<ArgumentException>(() => VarianteUnica().AgregarCodigo(codigo));
    }

    [Fact]
    public void Los_limites_de_stock_se_guardan()
    {
        var variante = VarianteUnica();

        variante.FijarLimitesDeStock(10m, 100m);

        Assert.Equal(10m, variante.StockMinimo);
        Assert.Equal(100m, variante.StockMaximo);
    }

    /// <summary>Un mínimo por encima del máximo dispararía a la vez la alerta de falta y la de exceso.</summary>
    [Fact]
    public void Un_minimo_mayor_que_el_maximo_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => VarianteUnica().FijarLimitesDeStock(100m, 10m));
    }

    [Fact]
    public void Unos_limites_negativos_se_rechazan()
    {
        Assert.Throws<ArgumentException>(() => VarianteUnica().FijarLimitesDeStock(-1m, null));
        Assert.Throws<ArgumentException>(() => VarianteUnica().FijarLimitesDeStock(null, -1m));
    }

    [Fact]
    public void Los_limites_pueden_quedar_sin_definir()
    {
        var variante = VarianteUnica();

        variante.FijarLimitesDeStock(null, null);

        Assert.Null(variante.StockMinimo);
        Assert.Null(variante.StockMaximo);
    }
}

public class CategoriaTests
{
    [Fact]
    public void Una_categoria_raiz_no_tiene_padre()
    {
        Assert.Null(new Categoria("Aseo").PadreId);
    }

    [Fact]
    public void Una_subcategoria_apunta_a_su_padre()
    {
        var padre = new Categoria("Aseo");

        var hija = new Categoria("Detergentes", padre);

        Assert.Equal(padre.Id, hija.PadreId);
    }

    [Fact]
    public void Una_categoria_no_puede_ser_su_propio_padre()
    {
        var categoria = new Categoria("Aseo");

        Assert.Throws<InvalidOperationException>(() => categoria.MoverBajo(categoria, []));
    }

    /// <summary>
    /// El ciclo es el fallo que importa: una categoría que es su propia antecesora cuelga
    /// cualquier recorrido del árbol, como pintar el menú o totalizar por categoría.
    /// </summary>
    [Fact]
    public void Mover_una_categoria_bajo_su_propia_descendiente_se_rechaza()
    {
        var abuela = new Categoria("Aseo");
        var madre = new Categoria("Detergentes", abuela);
        var nieta = new Categoria("Líquidos", madre);

        var error = Assert.Throws<InvalidOperationException>(() => abuela.MoverBajo(nieta, [madre, abuela]));

        Assert.Contains("ciclo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mover_a_la_raiz_quita_el_padre()
    {
        var hija = new Categoria("Detergentes", new Categoria("Aseo"));

        hija.MoverARaiz();

        Assert.Null(hija.PadreId);
    }

    [Fact]
    public void El_nombre_se_recorta()
    {
        Assert.Equal("Aseo", new Categoria("  Aseo  ").Nombre);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_nombre_vacio_se_rechaza(string nombre)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Categoria(nombre));
    }
}

public class UnidadMedidaTests
{
    [Fact]
    public void El_codigo_se_normaliza_y_expone_la_unidad_del_dominio()
    {
        var unidad = new UnidadMedida(" kg ", "Kilogramo", 3);

        Assert.Equal("KG", unidad.Codigo);
        Assert.Equal(3, unidad.Decimales);
        Assert.Equal("KG", unidad.ComoUnidad().Codigo);
        Assert.Equal(3, unidad.ComoUnidad().Decimales);
    }

    [Fact]
    public void Una_unidad_de_piezas_no_admite_decimales()
    {
        var unidad = new UnidadMedida("UND", "Unidad", 0);

        Assert.Equal(0, unidad.ComoUnidad().Decimales);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void Unos_decimales_fuera_de_rango_se_rechazan(int decimales)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnidadMedida("KG", "Kilogramo", decimales));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_nombre_vacio_se_rechaza(string nombre)
    {
        Assert.ThrowsAny<ArgumentException>(() => new UnidadMedida("KG", nombre, 3));
    }
}
