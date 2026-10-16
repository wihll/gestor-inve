using System.Text.Json.Nodes;
using GestorInve.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Persistencia.Tests;

[Collection(ColeccionBaseDatos.Nombre)]
[Trait("Categoria", "Integracion")]
public class AuditoriaTests(BaseDatosFixture bd)
{
    private static readonly DateTimeOffset Momento = new(2026, 3, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Al_insertar_se_registran_fecha_y_usuario_con_el_reloj_inyectado()
    {
        var empresa = await bd.CrearEmpresaAsync("Auditada");

        await using var contexto = bd.Crear(empresa, new RelojFijo(Momento), "contadora");
        contexto.Add(new RegistroAuditoria("Producto", "7", "alta"));
        await contexto.SaveChangesAsync();

        await using var lectura = bd.Crear(empresa);
        var registro = await lectura.Set<RegistroAuditoria>().SingleAsync(r => r.ClaveEntidad == "7");

        Assert.Equal(Momento, registro.CreadoEn);
        Assert.Equal("contadora", registro.CreadoPor);
        Assert.Null(registro.ActualizadoEn);
        Assert.Null(registro.ActualizadoPor);
    }

    [Fact]
    public async Task Al_modificar_se_registran_fecha_y_usuario_de_la_actualizacion()
    {
        var empresa = await bd.CrearEmpresaAsync("Renombrable");
        var despues = Momento.AddDays(1);

        await using var contexto = bd.Crear(empresa, new RelojFijo(despues), "gerente");
        var ficha = await contexto.Set<Empresa>().SingleAsync();
        ficha.Renombrar("Renombrada S.A.S.");
        await contexto.SaveChangesAsync();

        await using var lectura = bd.Crear(empresa);
        var actualizada = await lectura.Set<Empresa>().SingleAsync();

        Assert.Equal("Renombrada S.A.S.", actualizada.Nombre);
        Assert.Equal(despues, actualizada.ActualizadoEn);
        Assert.Equal("gerente", actualizada.ActualizadoPor);
    }

    [Fact]
    public async Task El_interceptor_sella_la_empresa_e_ignora_lo_que_traiga_la_entidad()
    {
        var empresa = await bd.CrearEmpresaAsync("Sellada");

        await using var contexto = bd.Crear(empresa);
        var registro = new RegistroAuditoria("Producto", "9", "alta");
        Assert.Equal(Guid.Empty, registro.EmpresaId);

        contexto.Add(registro);
        await contexto.SaveChangesAsync();

        Assert.Equal(empresa, registro.EmpresaId);
    }

    [Fact]
    public async Task Sin_empresa_en_el_contexto_no_se_puede_insertar_una_entidad_de_empresa()
    {
        await using var contexto = bd.Crear(null);
        contexto.Add(new RegistroAuditoria("Producto", "1", "alta"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => contexto.SaveChangesAsync());

        Assert.Contains("sin empresa en el contexto", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>jsonb</c> guarda el documento analizado, no el texto: normaliza espacios y ordena claves,
    /// así que se compara la estructura. Lo que no puede perderse son los decimales de los importes
    /// (<c>jsonb</c> los guarda como <c>numeric</c>, sin pasar por coma flotante).
    /// </summary>
    [Fact]
    public async Task El_jsonb_de_datos_conserva_la_estructura_y_los_decimales()
    {
        var empresa = await bd.CrearEmpresaAsync("Jsonb");
        const string Datos = """{"antes":{"costo":1500.50},"despues":{"costo":1600.75}}""";

        await using var contexto = bd.Crear(empresa);
        contexto.Add(new RegistroAuditoria("Producto", "json", "cambio-costo", Datos));
        await contexto.SaveChangesAsync();

        await using var lectura = bd.Crear(empresa);
        var registro = await lectura.Set<RegistroAuditoria>().SingleAsync(r => r.ClaveEntidad == "json");

        var guardado = JsonNode.Parse(registro.Datos!)!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Datos), guardado));
        Assert.Equal(1500.50m, guardado["antes"]!["costo"]!.GetValue<decimal>());
        Assert.Equal(1600.75m, guardado["despues"]!["costo"]!.GetValue<decimal>());
    }
}
