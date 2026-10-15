using GestorInve.Seguridad;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestorInve.Persistencia.Tests;

/// <summary>
/// El aislamiento entre empresas es la garantía de seguridad central del sistema: una fuga
/// cruzada expone los inventarios y la facturación de otro cliente. Se prueba contra
/// PostgreSQL real y en las dos capas (filtro de EF y RLS).
/// </summary>
[Collection(ColeccionBaseDatos.Nombre)]
[Trait("Categoria", "Integracion")]
public class AislamientoMultiempresaTests(BaseDatosFixture bd)
{
    [Fact]
    public async Task El_filtro_global_solo_devuelve_las_filas_de_la_empresa_actual()
    {
        var (empresaA, empresaB) = await DosEmpresasConUnRegistroCadaUnaAsync();

        await using var contextoA = bd.Crear(empresaA);
        var registros = await contextoA.Set<RegistroAuditoria>().ToListAsync();

        Assert.All(registros, r => Assert.Equal(empresaA, r.EmpresaId));
        Assert.Contains(registros, r => r.Accion == $"alta-{empresaA}");
        Assert.DoesNotContain(registros, r => r.Accion == $"alta-{empresaB}");
    }

    /// <summary>
    /// La prueba que de verdad importa: si el aislamiento viviera solo en el filtro de EF,
    /// un <c>IgnoreQueryFilters()</c> (o cualquier SQL crudo) bastaría para leer otra empresa.
    /// </summary>
    [Fact]
    public async Task RLS_oculta_las_filas_de_otra_empresa_aunque_se_ignore_el_filtro_de_EF()
    {
        var (empresaA, empresaB) = await DosEmpresasConUnRegistroCadaUnaAsync();

        await using var contextoA = bd.Crear(empresaA);
        var registros = await contextoA.Set<RegistroAuditoria>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(registros);
        Assert.All(registros, r => Assert.Equal(empresaA, r.EmpresaId));
        Assert.DoesNotContain(registros, r => r.EmpresaId == empresaB);
    }

    [Fact]
    public async Task RLS_tambien_se_aplica_al_SQL_crudo()
    {
        var (empresaA, empresaB) = await DosEmpresasConUnRegistroCadaUnaAsync();

        await using var contextoA = bd.Crear(empresaA);
        var deOtraEmpresa = await contextoA.Database
            .SqlQuery<int>($"select count(*)::int as \"Value\" from auditoria.registros where empresa_id = {empresaB}")
            .SingleAsync();

        Assert.Equal(0, deOtraEmpresa);
    }

    [Fact]
    public async Task RLS_impide_escribir_a_nombre_de_otra_empresa()
    {
        var empresaA = await bd.CrearEmpresaAsync("A");
        var empresaB = await bd.CrearEmpresaAsync("B");

        await using var contextoA = bd.Crear(empresaA);

        // Inserción directa, saltándose el interceptor que sella la empresa.
        var escritura = () => contextoA.Database.ExecuteSqlAsync(
            $"""
            insert into auditoria.registros
                (id, empresa_id, entidad, clave_entidad, accion, creado_en, creado_por)
            values ({Guid.CreateVersion7()}, {empresaB}, 'Producto', '1', 'intruso', now(), 'atacante')
            """);

        var error = await Assert.ThrowsAsync<PostgresException>(escritura);
        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task Sin_empresa_en_la_sesion_no_se_ve_ninguna_fila()
    {
        await DosEmpresasConUnRegistroCadaUnaAsync();

        await using var sinEmpresa = bd.Crear(null);
        var registros = await sinEmpresa.Set<RegistroAuditoria>().IgnoreQueryFilters().ToListAsync();

        Assert.Empty(registros);
    }

    [Fact]
    public async Task Una_empresa_no_ve_la_ficha_de_otra_empresa()
    {
        var empresaA = await bd.CrearEmpresaAsync("A");
        var empresaB = await bd.CrearEmpresaAsync("B");

        await using var contextoA = bd.Crear(empresaA);
        var empresas = await contextoA.Set<Empresa>().IgnoreQueryFilters().ToListAsync();

        Assert.Equal([empresaA], empresas.Select(e => e.Id));
        Assert.DoesNotContain(empresas, e => e.Id == empresaB);
    }

    private async Task<(Guid EmpresaA, Guid EmpresaB)> DosEmpresasConUnRegistroCadaUnaAsync()
    {
        var empresaA = await bd.CrearEmpresaAsync("A");
        var empresaB = await bd.CrearEmpresaAsync("B");

        foreach (var empresa in new[] { empresaA, empresaB })
        {
            await using var contexto = bd.Crear(empresa);
            contexto.Add(new RegistroAuditoria("Producto", "1", $"alta-{empresa}"));
            await contexto.SaveChangesAsync();
        }

        return (empresaA, empresaB);
    }
}
