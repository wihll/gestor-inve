using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorInve.Api.Autenticacion;
using GestorInve.Api.Catalogo;

namespace GestorInve.Api.Tests;

/// <summary>
/// Catálogo de punta a punta. La pieza clave es el aislamiento **a través de la API**: hasta
/// ahora se había comprobado el RLS en la base y la autenticación por separado, pero nunca que
/// una petición autenticada de una empresa no alcance los datos de otra. Es el camino que de
/// verdad recorre un cliente, y el que convierte una fuga en un incidente.
/// </summary>
[Collection(ColeccionApi.Nombre)]
[Trait("Categoria", "Integracion")]
public class CatalogoTests(ApiFixture api)
{
    private const string Clave = "Clave-Muy-Larga-2026";

    [Fact]
    public async Task Crear_un_producto_sin_atributos_devuelve_su_variante_unica()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var unidad = await CrearUnidadAsync(cliente, "UND", 0);

        var producto = await CrearProductoAsync(cliente, "MART-001", "Martillo", unidad.Id);

        var variante = Assert.Single(producto.Variantes);
        Assert.Equal("MART-001", variante.Sku);
        Assert.Empty(variante.Atributos);
    }

    [Fact]
    public async Task Un_producto_con_atributos_recibe_sus_variantes()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var unidad = await CrearUnidadAsync(cliente, "UND", 0);

        var producto = await CrearProductoAsync(
            cliente,
            "CAM-POLO",
            "Camiseta Polo",
            unidad.Id,
            ["talla", "color"]);

        Assert.Empty(producto.Variantes);

        using var respuesta = await cliente.PostAsJsonAsync(
            $"/catalogo/productos/{producto.Id}/variantes",
            new PeticionVariante("CAM-POLO-M-ROJO", new() { ["talla"] = "M", ["color"] = "rojo" }));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var leido = await cliente.GetFromJsonAsync<RespuestaProducto>($"/catalogo/productos/{producto.Id}");
        var variante = Assert.Single(leido!.Variantes);
        Assert.Equal("M", variante.Atributos["talla"]);
    }

    /// <summary>
    /// El fallo que hay que impedir: el token de la empresa A pidiendo el producto de la B.
    /// Responde 404 y no 403, para no confirmar que ese identificador existe en otro cliente.
    /// </summary>
    [Fact]
    public async Task Con_el_token_de_una_empresa_no_se_alcanza_el_producto_de_otra()
    {
        var clienteA = await ClienteDeEmpresaNuevaAsync();
        var clienteB = await ClienteDeEmpresaNuevaAsync();

        var unidadB = await CrearUnidadAsync(clienteB, "UND", 0);
        var productoB = await CrearProductoAsync(clienteB, "SECRETO-B", "Producto de B", unidadB.Id);

        using var porId = await clienteA.GetAsync(new Uri($"/catalogo/productos/{productoB.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, porId.StatusCode);

        // Tampoco debe aparecer en el listado de A.
        var listaDeA = await clienteA.GetFromJsonAsync<List<RespuestaProducto>>("/catalogo/productos");
        Assert.DoesNotContain(listaDeA!, p => p.Id == productoB.Id);
        Assert.DoesNotContain(listaDeA!, p => p.Sku == "SECRETO-B");
    }

    [Fact]
    public async Task Tampoco_se_puede_escribir_sobre_la_variante_de_otra_empresa()
    {
        var clienteA = await ClienteDeEmpresaNuevaAsync();
        var clienteB = await ClienteDeEmpresaNuevaAsync();

        var unidadB = await CrearUnidadAsync(clienteB, "UND", 0);
        var productoB = await CrearProductoAsync(clienteB, "VAR-B", "Producto de B", unidadB.Id);
        var varianteB = productoB.Variantes.Single();

        using var limites = await clienteA.PutAsJsonAsync(
            $"/catalogo/variantes/{varianteB.Id}/limites",
            new PeticionLimites(1m, 10m));
        using var codigo = await clienteA.PostAsJsonAsync(
            $"/catalogo/variantes/{varianteB.Id}/codigos",
            new PeticionCodigo("7702001234567"));
        using var variante = await clienteA.PostAsJsonAsync(
            $"/catalogo/productos/{productoB.Id}/variantes",
            new PeticionVariante("INTRUSA", []));

        Assert.Equal(HttpStatusCode.NotFound, limites.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, codigo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, variante.StatusCode);
    }

    [Fact]
    public async Task Un_codigo_de_barras_de_otra_empresa_no_se_resuelve()
    {
        var clienteA = await ClienteDeEmpresaNuevaAsync();
        var clienteB = await ClienteDeEmpresaNuevaAsync();

        var unidadB = await CrearUnidadAsync(clienteB, "UND", 0);
        var productoB = await CrearProductoAsync(clienteB, "COD-B", "Producto de B", unidadB.Id);
        var codigo = $"77{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

        using var alta = await clienteB.PostAsJsonAsync(
            $"/catalogo/variantes/{productoB.Variantes.Single().Id}/codigos",
            new PeticionCodigo(codigo));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        // B lo resuelve; A no, aunque conozca el código exacto.
        using var desdeB = await clienteB.GetAsync(new Uri($"/catalogo/codigos/{codigo}", UriKind.Relative));
        using var desdeA = await clienteA.GetAsync(new Uri($"/catalogo/codigos/{codigo}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, desdeB.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, desdeA.StatusCode);
    }

    /// <summary>El mismo código de artículo puede repetirse entre clientes del mismo despliegue.</summary>
    [Fact]
    public async Task El_mismo_SKU_puede_existir_en_dos_empresas()
    {
        var clienteA = await ClienteDeEmpresaNuevaAsync();
        var clienteB = await ClienteDeEmpresaNuevaAsync();

        var unidadA = await CrearUnidadAsync(clienteA, "UND", 0);
        var unidadB = await CrearUnidadAsync(clienteB, "UND", 0);

        var enA = await CrearProductoAsync(clienteA, "COMPARTIDO", "Artículo de A", unidadA.Id);
        var enB = await CrearProductoAsync(clienteB, "COMPARTIDO", "Artículo de B", unidadB.Id);

        Assert.NotEqual(enA.Id, enB.Id);
        Assert.Equal("COMPARTIDO", enA.Sku);
        Assert.Equal("COMPARTIDO", enB.Sku);
    }

    [Fact]
    public async Task Dentro_de_una_empresa_el_SKU_no_se_repite()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var unidad = await CrearUnidadAsync(cliente, "UND", 0);
        await CrearProductoAsync(cliente, "UNICO", "Primero", unidad.Id);

        using var repetido = await cliente.PostAsJsonAsync(
            "/catalogo/productos",
            new PeticionProducto("UNICO", "Segundo", unidad.Id, null, null, null, false, false));

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }

    [Fact]
    public async Task Sin_permiso_de_escritura_no_se_puede_crear()
    {
        var (nit, correo) = await AltaAsync();
        var sinPermisos = await ClienteConPermisosAsync(nit, correo);

        using var respuesta = await sinPermisos.PostAsJsonAsync(
            "/catalogo/unidades",
            new PeticionUnidad("UND", "Unidad", 0));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Sin_token_el_catalogo_no_responde()
    {
        using var anonimo = api.CreateClient();

        using var listado = await anonimo.GetAsync(new Uri("/catalogo/productos", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, listado.StatusCode);
    }

    [Fact]
    public async Task Una_unidad_sin_decimales_y_otra_con_decimales_conviven()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();

        var piezas = await CrearUnidadAsync(cliente, "UND", 0);
        var kilos = await CrearUnidadAsync(cliente, "KG", 3);

        Assert.Equal(0, piezas.Decimales);
        Assert.Equal(3, kilos.Decimales);

        var unidades = await cliente.GetFromJsonAsync<List<RespuestaUnidad>>("/catalogo/unidades");
        Assert.Equal(2, unidades!.Count);
    }

    [Fact]
    public async Task Una_categoria_puede_colgar_de_otra_pero_no_formar_un_ciclo()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();

        var abuela = await CrearCategoriaAsync(cliente, "Aseo", null);
        var madre = await CrearCategoriaAsync(cliente, "Detergentes", abuela.Id);
        var nieta = await CrearCategoriaAsync(cliente, "Líquidos", madre.Id);

        Assert.Equal(madre.Id, nieta.PadreId);

        // Colgar la abuela de su nieta cerraría el árbol en bucle.
        using var ciclo = await cliente.PutAsJsonAsync(
            $"/catalogo/categorias/{abuela.Id}/padre",
            new PeticionCategoria("Aseo", nieta.Id));

        Assert.Equal(HttpStatusCode.Conflict, ciclo.StatusCode);
    }

    [Fact]
    public async Task Una_variante_repetida_se_rechaza_con_409()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var unidad = await CrearUnidadAsync(cliente, "UND", 0);
        var producto = await CrearProductoAsync(cliente, "CAM", "Camiseta", unidad.Id, ["talla"]);

        using var primera = await cliente.PostAsJsonAsync(
            $"/catalogo/productos/{producto.Id}/variantes",
            new PeticionVariante("CAM-M", new() { ["talla"] = "M" }));
        using var repetida = await cliente.PostAsJsonAsync(
            $"/catalogo/productos/{producto.Id}/variantes",
            new PeticionVariante("CAM-M-BIS", new() { ["talla"] = "M" }));

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
    }

    [Fact]
    public async Task Una_variante_con_atributos_que_no_cuadran_se_rechaza_con_400()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var unidad = await CrearUnidadAsync(cliente, "UND", 0);
        var producto = await CrearProductoAsync(cliente, "CAM2", "Camiseta", unidad.Id, ["talla", "color"]);

        using var incompleta = await cliente.PostAsJsonAsync(
            $"/catalogo/productos/{producto.Id}/variantes",
            new PeticionVariante("CAM2-M", new() { ["talla"] = "M" }));

        Assert.Equal(HttpStatusCode.BadRequest, incompleta.StatusCode);
    }

    [Fact]
    public async Task Un_producto_con_una_unidad_inexistente_se_rechaza()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();

        using var respuesta = await cliente.PostAsJsonAsync(
            "/catalogo/productos",
            new PeticionProducto("X", "Sin unidad", Guid.CreateVersion7(), null, null, null, false, false));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    private async Task<(string Nit, string Correo)> AltaAsync()
    {
        var nit = ApiFixture.NitNuevo();
        var correo = $"admin-{Guid.NewGuid():N}@empresa.co";

        using var cliente = api.CreateClient();
        using var alta = await cliente.PostAsJsonAsync(
            "/empresas",
            new PeticionAlta(nit, "Pyme de Pruebas S.A.S.", correo, "Administradora", Clave));

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        return (nit, correo);
    }

    /// <summary>Cliente autenticado como administrador de una empresa recién creada.</summary>
    private async Task<HttpClient> ClienteDeEmpresaNuevaAsync()
    {
        var (nit, correo) = await AltaAsync();

        using var anonimo = api.CreateClient();
        using var login = await anonimo.PostAsJsonAsync("/auth/login", new PeticionLogin(nit, correo, Clave));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var sesion = (await login.Content.ReadFromJsonAsync<RespuestaSesion>())!;

        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.TokenAcceso);

        return cliente;
    }

    /// <summary>
    /// Token con la empresa correcta pero **sin reclamaciones de permiso**, para comprobar que
    /// las políticas se aplican de verdad y no basta con estar autenticado.
    /// </summary>
    private async Task<HttpClient> ClienteConPermisosAsync(string nit, string correo)
    {
        using var anonimo = api.CreateClient();
        using var login = await anonimo.PostAsJsonAsync("/auth/login", new PeticionLogin(nit, correo, Clave));
        var sesion = (await login.Content.ReadFromJsonAsync<RespuestaSesion>())!;

        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiFixture.TokenSinPermisos(sesion.TokenAcceso));

        return cliente;
    }

    private static async Task<RespuestaUnidad> CrearUnidadAsync(HttpClient cliente, string codigo, int decimales)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            "/catalogo/unidades",
            new PeticionUnidad(codigo, codigo == "KG" ? "Kilogramo" : "Unidad", decimales));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaUnidad>())!;
    }

    private static async Task<RespuestaCategoria> CrearCategoriaAsync(HttpClient cliente, string nombre, Guid? padre)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            "/catalogo/categorias",
            new PeticionCategoria(nombre, padre));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaCategoria>())!;
    }

    private static async Task<RespuestaProducto> CrearProductoAsync(
        HttpClient cliente,
        string sku,
        string nombre,
        Guid unidadId,
        IReadOnlyList<string>? atributos = null)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            "/catalogo/productos",
            new PeticionProducto(sku, nombre, unidadId, null, null, atributos, false, false));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaProducto>())!;
    }
}
