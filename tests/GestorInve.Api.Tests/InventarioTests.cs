using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorInve.Api.Autenticacion;
using GestorInve.Api.Catalogo;
using GestorInve.Api.Inventario;
using GestorInve.Inventario;

namespace GestorInve.Api.Tests;

/// <summary>
/// Kardex de punta a punta. Lo que se comprueba aquí no es que los endpoints respondan, sino
/// que **el libro cuadre**: que la suma firmada de los movimientos sea igual a las existencias
/// en toda circunstancia, incluida la concurrencia. Un descuadre en un ERP no se nota el día que
/// ocurre, sino meses después al cruzar inventario contra contabilidad.
/// </summary>
[Collection(ColeccionApi.Nombre)]
[Trait("Categoria", "Integracion")]
public class InventarioTests(ApiFixture api)
{
    private const string Clave = "Clave-Muy-Larga-2026";

    [Fact]
    public async Task Crear_una_bodega_la_deja_con_su_ubicacion_general()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();

        var bodega = await CrearBodegaAsync(cliente, "PRI", "Principal");

        var ubicacion = Assert.Single(bodega.Ubicaciones);
        Assert.Equal(Ubicacion.CodigoGeneral, ubicacion.Codigo);
    }

    [Fact]
    public async Task Una_entrada_fija_el_saldo_y_el_costo()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        var movimiento = await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);

        Assert.Equal(10m, movimiento.SaldoCantidadDespues);
        Assert.Equal(1000m, movimiento.SaldoCostoPromedioDespues);
        Assert.Equal(10_000m, movimiento.CostoTotal);

        var existencia = await ExistenciaAsync(cliente, escenario);
        Assert.Equal(10m, existencia.Cantidad);
        Assert.Equal(1000m, existencia.CostoPromedio);
        Assert.Equal(10_000m, existencia.Valorizado);
    }

    [Fact]
    public async Task Dos_entradas_a_distinto_costo_promedian()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 30m, 2000m);

        var existencia = await ExistenciaAsync(cliente, escenario);

        Assert.Equal(40m, existencia.Cantidad);
        Assert.Equal(1750m, existencia.CostoPromedio);
    }

    [Fact]
    public async Task Una_salida_sale_al_promedio_y_no_lo_cambia()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 2000m);
        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 5m, null);

        Assert.Equal(1500m, salida.CostoUnitario);
        Assert.Equal(7500m, salida.CostoTotal);
        Assert.Equal(1500m, salida.SaldoCostoPromedioDespues);
        Assert.Equal(15m, salida.SaldoCantidadDespues);
    }

    /// <summary>
    /// La política que eligió el usuario: sin existencias no se puede sacar, salvo con el
    /// permiso de ajuste, y entonces el movimiento queda **marcado** para que el descuadre sea
    /// visible y no silencioso.
    /// </summary>
    [Fact]
    public async Task Sin_existencias_la_salida_se_rechaza_con_409()
    {
        var (nit, correo) = await AltaAsync();
        var administrador = await ClienteAutenticadoAsync(nit, correo);
        var escenario = await EscenarioAsync(administrador);

        var soloMover = await ClienteSinAjustarAsync(nit, correo);

        using var respuesta = await soloMover.PostAsJsonAsync(
            "/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.Salida, 5m, null));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
    }

    [Fact]
    public async Task Con_permiso_de_ajuste_la_salida_forzada_queda_marcada()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        var forzada = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 5m, null);

        Assert.True(forzada.ForzadoNegativo);
        Assert.Equal(-5m, forzada.SaldoCantidadDespues);
    }

    /// <summary>
    /// **La prueba que justifica el bloqueo pesimista.** Varias salidas simultáneas de la misma
    /// celda: sin `FOR UPDATE` todas leerían el mismo saldo y el libro dejaría de cuadrar.
    /// </summary>
    [Fact]
    public async Task Salidas_simultaneas_sobre_la_misma_celda_dejan_el_libro_cuadrado()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 100m, 1000m);

        // 10 salidas de 5 en paralelo: deben quedar 50.
        var salidas = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var respuesta = await cliente.PostAsJsonAsync(
                "/inventario/movimientos",
                Peticion(escenario, TipoMovimiento.Salida, 5m, null));

            return respuesta.StatusCode;
        });

        var codigos = await Task.WhenAll(salidas);

        Assert.All(codigos, c => Assert.Equal(HttpStatusCode.Created, c));

        var existencia = await ExistenciaAsync(cliente, escenario);
        var kardex = await KardexAsync(cliente, escenario.VarianteId);

        Assert.Equal(50m, existencia.Cantidad);
        Assert.Equal(existencia.Cantidad, SumaFirmada(kardex));
        Assert.Equal(existencia.Cantidad, kardex[^1].SaldoCantidadDespues);
    }

    /// <summary>Tras una secuencia variada, el acumulado del libro debe ser la existencia.</summary>
    [Fact]
    public async Task Tras_entradas_salidas_traslado_y_reversa_el_libro_sigue_cuadrando()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);
        var destino = await CrearBodegaAsync(cliente, "SEC", "Secundaria");

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 100m, 1000m);
        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 50m, 1200m);
        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 30m, null);

        using var traslado = await cliente.PostAsJsonAsync(
            "/inventario/traslados",
            new PeticionTraslado(
                escenario.VarianteId,
                escenario.BodegaId,
                null,
                destino.Id,
                null,
                20m,
                null,
                "TRA-1",
                null));
        Assert.Equal(HttpStatusCode.Created, traslado.StatusCode);

        using var reversa = await cliente.PostAsJsonAsync(
            $"/inventario/movimientos/{salida.Id}/reversa",
            new PeticionReversa("corrección"));
        Assert.Equal(HttpStatusCode.Created, reversa.StatusCode);

        var kardex = await KardexAsync(cliente, escenario.VarianteId);
        var existencias = await cliente.GetFromJsonAsync<List<RespuestaExistencia>>(
            $"/inventario/existencias?varianteId={escenario.VarianteId}");

        // 100 + 50 - 30 - 20 (traslado) + 30 (reversa) = 130 en origen, 20 en destino.
        Assert.NotNull(existencias);
        Assert.Equal(130m, existencias.Single(e => e.BodegaId == escenario.BodegaId).Cantidad);
        Assert.Equal(20m, existencias.Single(e => e.BodegaId == destino.Id).Cantidad);
        Assert.Equal(150m, SumaFirmada(kardex));
        Assert.Equal(150m, existencias.Sum(e => e.Cantidad));
    }

    [Fact]
    public async Task El_traslado_conserva_el_costo_de_la_mercancia()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);
        var destino = await CrearBodegaAsync(cliente, "SEC", "Secundaria");

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1500m);

        using var respuesta = await cliente.PostAsJsonAsync(
            "/inventario/traslados",
            new PeticionTraslado(escenario.VarianteId, escenario.BodegaId, null, destino.Id, null, 4m, null, null, null));

        var movimientos = (await respuesta.Content.ReadFromJsonAsync<List<RespuestaMovimiento>>())!;

        Assert.Equal(2, movimientos.Count);
        Assert.All(movimientos, m => Assert.Equal(1500m, m.CostoUnitario));
        Assert.Equal(movimientos[0].TrasladoId, movimientos[1].TrasladoId);

        var existencias = await cliente.GetFromJsonAsync<List<RespuestaExistencia>>(
            $"/inventario/existencias?varianteId={escenario.VarianteId}");

        Assert.NotNull(existencias);
        Assert.Equal(1500m, existencias.Single(e => e.BodegaId == destino.Id).CostoPromedio);
    }

    [Fact]
    public async Task La_reversa_restituye_cantidad_y_costo_y_no_toca_el_original()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 4m, null);

        using var respuesta = await cliente.PostAsJsonAsync(
            $"/inventario/movimientos/{salida.Id}/reversa",
            new PeticionReversa(null));

        var reversa = (await respuesta.Content.ReadFromJsonAsync<RespuestaMovimiento>())!;

        Assert.Equal(TipoMovimiento.Entrada, reversa.Tipo);
        Assert.Equal(salida.Id, reversa.MovimientoReversadoId);
        Assert.Equal(1000m, reversa.CostoUnitario);
        Assert.Equal(10m, reversa.SaldoCantidadDespues);

        // El original sigue en el libro, intacto.
        var kardex = await KardexAsync(cliente, escenario.VarianteId);
        var original = kardex.Single(m => m.Id == salida.Id);
        Assert.Equal(4m, original.Cantidad);
        Assert.Equal(TipoMovimiento.Salida, original.Tipo);
    }

    [Fact]
    public async Task Un_movimiento_no_se_puede_reversar_dos_veces()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);
        var salida = await MoverAsync(cliente, escenario, TipoMovimiento.Salida, 4m, null);

        using var primera = await cliente.PostAsJsonAsync(
            $"/inventario/movimientos/{salida.Id}/reversa",
            new PeticionReversa(null));
        using var segunda = await cliente.PostAsJsonAsync(
            $"/inventario/movimientos/{salida.Id}/reversa",
            new PeticionReversa(null));

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task Un_producto_que_exige_lote_rechaza_el_movimiento_sin_perder_la_trazabilidad()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente, exigeLote: true);

        using var respuesta = await cliente.PostAsJsonAsync(
            "/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.Entrada, 10m, 1000m));

        Assert.Equal(HttpStatusCode.NotImplemented, respuesta.StatusCode);
    }

    [Fact]
    public async Task Una_entrada_sin_costo_y_sin_existencias_se_rechaza()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);

        using var respuesta = await cliente.PostAsJsonAsync(
            "/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.Entrada, 10m, null));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_inventario_de_otra_empresa_no_se_alcanza()
    {
        var clienteA = await ClienteDeEmpresaNuevaAsync();
        var clienteB = await ClienteDeEmpresaNuevaAsync();

        var escenarioB = await EscenarioAsync(clienteB);
        var movimientoB = await MoverAsync(clienteB, escenarioB, TipoMovimiento.Entrada, 10m, 1000m);

        // A no ve el movimiento, ni puede reversarlo, ni ve las existencias de B.
        using var reversa = await clienteA.PostAsJsonAsync(
            $"/inventario/movimientos/{movimientoB.Id}/reversa",
            new PeticionReversa(null));
        var kardexDeA = await KardexAsync(clienteA, escenarioB.VarianteId);
        var existenciasDeA = await clienteA.GetFromJsonAsync<List<RespuestaExistencia>>("/inventario/existencias");

        Assert.Equal(HttpStatusCode.NotFound, reversa.StatusCode);
        Assert.Empty(kardexDeA);
        Assert.Empty(existenciasDeA!);
    }

    [Fact]
    public async Task Sin_permiso_de_ajuste_no_se_puede_ajustar_ni_reversar()
    {
        var (nit, correo) = await AltaAsync();
        var administrador = await ClienteAutenticadoAsync(nit, correo);
        var escenario = await EscenarioAsync(administrador);
        var entrada = await MoverAsync(administrador, escenario, TipoMovimiento.Entrada, 10m, 1000m);

        var soloMover = await ClienteSinAjustarAsync(nit, correo);

        using var ajuste = await soloMover.PostAsJsonAsync(
            "/inventario/movimientos",
            Peticion(escenario, TipoMovimiento.AjustePositivo, 1m, 100m));
        using var reversa = await soloMover.PostAsJsonAsync(
            $"/inventario/movimientos/{entrada.Id}/reversa",
            new PeticionReversa(null));

        Assert.Equal(HttpStatusCode.Forbidden, ajuste.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reversa.StatusCode);
    }

    /// <summary>
    /// La inmutabilidad no depende de que la aplicación se porte bien: hay un disparador en la
    /// base que rechaza cualquier UPDATE o DELETE sobre el kardex. Se comprueba por SQL crudo,
    /// que es justo la vía por la que alguien «arreglaría» un movimiento a mano.
    /// </summary>
    [Fact]
    public async Task La_base_de_datos_rechaza_modificar_o_borrar_un_movimiento()
    {
        var cliente = await ClienteDeEmpresaNuevaAsync();
        var escenario = await EscenarioAsync(cliente);
        var movimiento = await MoverAsync(cliente, escenario, TipoMovimiento.Entrada, 10m, 1000m);

        // La empresa sale del propio token: con RLS, sin ponerla en la sesión no se ve la fila.
        var yo = await cliente.GetFromJsonAsync<RespuestaYo>("/auth/yo");

        await using var conexion = new Npgsql.NpgsqlConnection(ApiFixture.CadenaConexion);
        await conexion.OpenAsync();

        await using (var sesion = conexion.CreateCommand())
        {
            sesion.CommandText = "select set_config('gestorinve.empresa_id', @empresa, false)";
            sesion.Parameters.AddWithValue("@empresa", yo!.EmpresaId.ToString());
            await sesion.ExecuteNonQueryAsync();
        }

        await using var actualizar = conexion.CreateCommand();
        actualizar.CommandText = "update inventario.movimientos set cantidad = 999 where id = @id";
        actualizar.Parameters.AddWithValue("@id", movimiento.Id);

        var errorUpdate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => actualizar.ExecuteNonQueryAsync());
        Assert.Contains("inmutable", errorUpdate.MessageText, StringComparison.Ordinal);

        await using var borrar = conexion.CreateCommand();
        borrar.CommandText = "delete from inventario.movimientos where id = @id";
        borrar.Parameters.AddWithValue("@id", movimiento.Id);

        var errorDelete = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => borrar.ExecuteNonQueryAsync());
        Assert.Contains("inmutable", errorDelete.MessageText, StringComparison.Ordinal);

        // Y el movimiento sigue como estaba.
        var kardex = await KardexAsync(cliente, escenario.VarianteId);
        Assert.Equal(10m, kardex.Single().Cantidad);
    }

    private static decimal SumaFirmada(IEnumerable<RespuestaMovimiento> kardex) =>
        kardex.Sum(m => m.Cantidad * m.Tipo.Signo());

    private sealed record Escenario(Guid VarianteId, Guid BodegaId);

    private static PeticionMovimiento Peticion(Escenario escenario, TipoMovimiento tipo, decimal cantidad, decimal? costo) =>
        new(tipo, escenario.VarianteId, escenario.BodegaId, null, cantidad, costo, null, "DOC-1", null);

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

    private async Task<HttpClient> ClienteAutenticadoAsync(string nit, string correo)
    {
        using var anonimo = api.CreateClient();
        using var login = await anonimo.PostAsJsonAsync("/auth/login", new PeticionLogin(nit, correo, Clave));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var sesion = (await login.Content.ReadFromJsonAsync<RespuestaSesion>())!;
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.TokenAcceso);

        return cliente;
    }

    private async Task<HttpClient> ClienteDeEmpresaNuevaAsync()
    {
        var (nit, correo) = await AltaAsync();

        return await ClienteAutenticadoAsync(nit, correo);
    }

    /// <summary>Token de la misma empresa pero sin el permiso de ajuste.</summary>
    private async Task<HttpClient> ClienteSinAjustarAsync(string nit, string correo)
    {
        using var anonimo = api.CreateClient();
        using var login = await anonimo.PostAsJsonAsync("/auth/login", new PeticionLogin(nit, correo, Clave));
        var sesion = (await login.Content.ReadFromJsonAsync<RespuestaSesion>())!;

        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiFixture.TokenSinPermisos(sesion.TokenAcceso, Seguridad.Permiso.InventarioAjustar));

        return cliente;
    }

    private static async Task<RespuestaBodega> CrearBodegaAsync(HttpClient cliente, string codigo, string nombre)
    {
        using var respuesta = await cliente.PostAsJsonAsync("/inventario/bodegas", new PeticionBodega(codigo, nombre));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaBodega>())!;
    }

    /// <summary>Unidad, producto con su variante única y bodega: lo mínimo para mover inventario.</summary>
    private static async Task<Escenario> EscenarioAsync(HttpClient cliente, bool exigeLote = false)
    {
        using var unidadRespuesta = await cliente.PostAsJsonAsync(
            "/catalogo/unidades",
            new PeticionUnidad("UND", "Unidad", 0));
        Assert.Equal(HttpStatusCode.Created, unidadRespuesta.StatusCode);
        var unidad = (await unidadRespuesta.Content.ReadFromJsonAsync<RespuestaUnidad>())!;

        using var productoRespuesta = await cliente.PostAsJsonAsync(
            "/catalogo/productos",
            new PeticionProducto($"SKU-{Guid.NewGuid():N}"[..12], "Artículo", unidad.Id, null, null, null, exigeLote, false));
        Assert.Equal(HttpStatusCode.Created, productoRespuesta.StatusCode);
        var producto = (await productoRespuesta.Content.ReadFromJsonAsync<RespuestaProducto>())!;

        var bodega = await CrearBodegaAsync(cliente, "PRI", "Principal");

        return new Escenario(producto.Variantes.Single().Id, bodega.Id);
    }

    private static async Task<RespuestaMovimiento> MoverAsync(
        HttpClient cliente,
        Escenario escenario,
        TipoMovimiento tipo,
        decimal cantidad,
        decimal? costo)
    {
        using var respuesta = await cliente.PostAsJsonAsync(
            "/inventario/movimientos",
            Peticion(escenario, tipo, cantidad, costo));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return (await respuesta.Content.ReadFromJsonAsync<RespuestaMovimiento>())!;
    }

    private static async Task<RespuestaExistencia> ExistenciaAsync(HttpClient cliente, Escenario escenario)
    {
        var existencias = await cliente.GetFromJsonAsync<List<RespuestaExistencia>>(
            $"/inventario/existencias?varianteId={escenario.VarianteId}&bodegaId={escenario.BodegaId}");

        return existencias!.Single();
    }

    private static async Task<List<RespuestaMovimiento>> KardexAsync(HttpClient cliente, Guid varianteId) =>
        (await cliente.GetFromJsonAsync<List<RespuestaMovimiento>>($"/inventario/kardex?varianteId={varianteId}"))!;
}
