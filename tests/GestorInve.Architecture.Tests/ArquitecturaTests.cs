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
    public void Un_modulo_no_depende_de_otros_modulos_ni_de_la_api(string modulo)
    {
        string[] prohibidos =
        [
            .. Modulos.Where(m => m != modulo).Select(m => $"GestorInve.{m}"),
            "GestorInve.Api",
            "GestorInve.Worker",
        ];

        var resultado = Types.InAssembly(Modulo(modulo)).ShouldNot().HaveDependencyOnAny(prohibidos).GetResult();

        Assert.True(resultado.IsSuccessful, Fallos(resultado));
    }

    [Fact]
    public void Core_no_expone_float_ni_double()
    {
        var infractores = Core.GetTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(UsaFlotante)
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
            .ToList();

        Assert.True(infractores.Count == 0, "Usan float/double: " + string.Join(", ", infractores));
    }

    public static TheoryData<string> ModulosData() => [.. Modulos];

    private static bool UsaFlotante(MemberInfo miembro)
    {
        static bool Es(Type t) => t == typeof(float) || t == typeof(double);

        return miembro switch
        {
            FieldInfo f => Es(f.FieldType),
            PropertyInfo p => Es(p.PropertyType),
            MethodBase m => m.GetParameters().Any(p => Es(p.ParameterType))
                            || (m is MethodInfo mi && Es(mi.ReturnType)),
            _ => false,
        };
    }

    private static string Fallos(TestResult resultado) =>
        resultado.FailingTypeNames is null ? string.Empty : "Violaciones: " + string.Join(", ", resultado.FailingTypeNames);
}
