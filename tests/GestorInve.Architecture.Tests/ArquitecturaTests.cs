using System.Reflection;
using GestorInve.Core;
using NetArchTest.Rules;

namespace GestorInve.Architecture.Tests;

public class ArquitecturaTests
{
    private static readonly string[] Modulos =
    [
        "Catalogo", "Inventario", "Compras", "Ventas",
        "Terceros", "Contabilidad", "FacturaElectronica", "Seguridad",
    ];

    /// <summary>
    /// Dependencias **declaradas** entre módulos. Un ERP real las necesita (el inventario no
    /// puede validar una variante sin conocer el catálogo), pero cada una debe ser una decisión
    /// visible: una dependencia que no esté aquí rompe la compilación de las pruebas.
    /// Ver ADR 0006.
    /// </summary>
    private static readonly Dictionary<string, string[]> DependenciasPermitidas = new(StringComparer.Ordinal)
    {
        ["Inventario"] = ["Catalogo"],
    };

    private static readonly Assembly Core = typeof(Dinero).Assembly;

    private static Assembly Modulo(string nombre) => Assembly.Load($"GestorInve.{nombre}");

    [Fact]
    public void Core_no_depende_de_modulos_ni_de_la_api()
    {
        string[] prohibidos = [.. Modulos.Select(m => $"GestorInve.{m}"), "GestorInve.Api", "GestorInve.Worker"];

        var resultado = Types.InAssembly(Core).ShouldNot().HaveDependencyOnAny(prohibidos).GetResult();

        Assert.True(resultado.IsSuccessful, Fallos(resultado));
    }

    [Theory]
    [MemberData(nameof(ModulosData))]
    public void Un_modulo_solo_depende_de_los_modulos_que_declara(string modulo)
    {
        var permitidos = DependenciasPermitidas.TryGetValue(modulo, out var declarados) ? declarados : [];

        string[] prohibidos =
        [
            .. Modulos.Where(m => m != modulo && !permitidos.Contains(m, StringComparer.Ordinal))
                .Select(m => $"GestorInve.{m}"),
            "GestorInve.Api",
            "GestorInve.Worker",
        ];

        var resultado = Types.InAssembly(Modulo(modulo)).ShouldNot().HaveDependencyOnAny(prohibidos).GetResult();

        Assert.True(resultado.IsSuccessful, Fallos(resultado));
    }

    /// <summary>
    /// El grafo de dependencias declaradas no puede tener ciclos. Dos módulos que se necesitan
    /// mutuamente no se pueden razonar, probar ni desplegar por separado: dejan de ser módulos.
    /// </summary>
    [Fact]
    public void Las_dependencias_declaradas_no_forman_ciclos()
    {
        foreach (var (modulo, _) in DependenciasPermitidas)
        {
            var visitados = new HashSet<string>(StringComparer.Ordinal);
            var pila = new Stack<string>([modulo]);

            while (pila.Count > 0)
            {
                var actual = pila.Pop();

                if (!DependenciasPermitidas.TryGetValue(actual, out var siguientes))
                {
                    continue;
                }

                foreach (var siguiente in siguientes)
                {
                    Assert.NotEqual(modulo, siguiente);

                    if (visitados.Add(siguiente))
                    {
                        pila.Push(siguiente);
                    }
                }
            }
        }
    }

    /// <summary>Cada dependencia declarada tiene que nombrar módulos que existen.</summary>
    [Fact]
    public void Las_dependencias_declaradas_nombran_modulos_reales()
    {
        foreach (var (modulo, dependencias) in DependenciasPermitidas)
        {
            Assert.Contains(modulo, Modulos);

            foreach (var dependencia in dependencias)
            {
                Assert.Contains(dependencia, Modulos);
            }
        }
    }

    /// <summary>
    /// El dominio no debe saber cómo se guarda: ni EF Core, ni Npgsql, ni el proyecto de
    /// persistencia. Si Core arrastra el ORM, las reglas de negocio dejan de ser comprobables
    /// sin base de datos y el modelo acaba dictado por el esquema.
    /// </summary>
    [Fact]
    public void Core_no_depende_del_ORM_ni_de_la_persistencia()
    {
        string[] prohibidos =
        [
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "GestorInve.Persistencia",
        ];

        var resultado = Types.InAssembly(Core).ShouldNot().HaveDependencyOnAny(prohibidos).GetResult();

        Assert.True(resultado.IsSuccessful, Fallos(resultado));
    }

    [Fact]
    public void Core_no_expone_float_ni_double()
    {
        var infractores = MiembrosConFlotante(Core).ToList();

        Assert.True(infractores.Count == 0, "Usan float/double: " + string.Join(", ", infractores));
    }

    [Theory]
    [MemberData(nameof(ModulosData))]
    public void Un_modulo_no_expone_float_ni_double(string modulo)
    {
        var infractores = MiembrosConFlotante(Modulo(modulo)).ToList();

        Assert.True(infractores.Count == 0, "Usan float/double: " + string.Join(", ", infractores));
    }

    /// <summary>
    /// El detector es la única defensa de la regla «nunca float» de CONSTRAINTS.md:
    /// si deja pasar un flotante envuelto, la regla se puede violar con CI verde.
    /// </summary>
    [Theory]
    [InlineData(nameof(CebosFlotantes.Directo))]
    [InlineData(nameof(CebosFlotantes.Arreglo))]
    [InlineData(nameof(CebosFlotantes.ListaGenerica))]
    [InlineData(nameof(CebosFlotantes.Anidado))]
    [InlineData(nameof(CebosFlotantes.Nulable))]
    [InlineData(nameof(CebosFlotantes.Simple))]
    [InlineData(nameof(CebosFlotantes.Tupla))]
    [InlineData(nameof(CebosFlotantes.DevuelveArreglo))]
    [InlineData(nameof(CebosFlotantes.RecibeLista))]
    public void El_detector_encuentra_los_flotantes_envueltos(string miembro)
    {
        var detectados = MiembrosConFlotante(typeof(CebosFlotantes)).ToList();

        Assert.Contains($"{nameof(CebosFlotantes)}.{miembro}", detectados);
    }

    [Theory]
    [InlineData(nameof(CebosFlotantes.Exacto))]
    [InlineData(nameof(CebosFlotantes.Cantidades))]
    public void El_detector_no_marca_los_tipos_exactos(string miembro)
    {
        var detectados = MiembrosConFlotante(typeof(CebosFlotantes)).ToList();

        Assert.DoesNotContain($"{nameof(CebosFlotantes)}.{miembro}", detectados);
    }

    public static TheoryData<string> ModulosData() => [.. Modulos];

    private static IEnumerable<string> MiembrosConFlotante(Assembly ensamblado) =>
        ensamblado.GetTypes().SelectMany(MiembrosConFlotante);

    private static IEnumerable<string> MiembrosConFlotante(Type tipo) =>
        tipo.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(UsaFlotante)
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}");

    private static bool UsaFlotante(MemberInfo miembro) => miembro switch
    {
        FieldInfo f => ContieneFlotante(f.FieldType),
        PropertyInfo p => ContieneFlotante(p.PropertyType),
        MethodBase m => m.GetParameters().Any(p => ContieneFlotante(p.ParameterType))
                        || (m is MethodInfo mi && ContieneFlotante(mi.ReturnType)),
        _ => false,
    };

    /// <summary>
    /// Recorre el tipo hasta el fondo: arreglos, punteros, <c>ref</c>, argumentos
    /// genéricos y <c>Nullable&lt;T&gt;</c>. Un <c>double</c> envuelto sigue siendo un <c>double</c>.
    /// </summary>
    private static bool ContieneFlotante(Type tipo)
    {
        if (tipo == typeof(float) || tipo == typeof(double))
        {
            return true;
        }

        if (tipo.HasElementType)
        {
            return ContieneFlotante(tipo.GetElementType()!);
        }

        return tipo.IsGenericType && tipo.GetGenericArguments().Any(ContieneFlotante);
    }

    private static string Fallos(TestResult resultado) =>
        resultado.FailingTypeNames is null ? string.Empty : "Violaciones: " + string.Join(", ", resultado.FailingTypeNames);

    /// <summary>Fixture: cada miembro es una forma de colar un flotante (o de no colarlo).</summary>
    internal sealed class CebosFlotantes
    {
        public double Directo { get; set; }

        public double[]? Arreglo { get; set; }

        public IReadOnlyList<double>? ListaGenerica { get; set; }

        public Dictionary<string, List<float>>? Anidado { get; set; }

        public double? Nulable { get; set; }

        public float Simple { get; set; }

        public (decimal Monto, double Tasa) Tupla { get; set; }

        public decimal Exacto { get; set; }

        public IReadOnlyList<decimal>? Cantidades { get; set; }

        public static double[] DevuelveArreglo() => [];

        public static void RecibeLista(IEnumerable<double> tasas) => _ = tasas;
    }
}
