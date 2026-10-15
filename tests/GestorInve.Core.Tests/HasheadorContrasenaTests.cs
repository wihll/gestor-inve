using GestorInve.Core.Seguridad;

namespace GestorInve.Core.Tests;

public class HasheadorContrasenaTests
{
    private readonly HasheadorContrasena hasheador = new();

    [Fact]
    public void Una_contrasena_valida_se_verifica()
    {
        var hash = hasheador.Hashear("Clave-Muy-Larga-2026");

        Assert.True(hasheador.Verificar("Clave-Muy-Larga-2026", hash));
    }

    [Fact]
    public void Una_contrasena_incorrecta_no_se_verifica()
    {
        var hash = hasheador.Hashear("Clave-Muy-Larga-2026");

        Assert.False(hasheador.Verificar("clave-muy-larga-2026", hash));
        Assert.False(hasheador.Verificar("otra cosa", hash));
        Assert.False(hasheador.Verificar(string.Empty, hash));
    }

    /// <summary>Sin sal por contraseña, dos usuarios con la misma clave comparten hash y una tabla precalculada los rompe a la vez.</summary>
    [Fact]
    public void Dos_hashes_de_la_misma_contrasena_son_distintos()
    {
        var uno = hasheador.Hashear("Clave-Muy-Larga-2026");
        var otro = hasheador.Hashear("Clave-Muy-Larga-2026");

        Assert.NotEqual(uno, otro);
        Assert.True(hasheador.Verificar("Clave-Muy-Larga-2026", uno));
        Assert.True(hasheador.Verificar("Clave-Muy-Larga-2026", otro));
    }

    /// <summary>El formato lleva algoritmo e iteraciones para poder endurecerlo o migrar a Argon2id sin invalidar las claves existentes.</summary>
    [Fact]
    public void El_hash_declara_algoritmo_e_iteraciones()
    {
        var partes = hasheador.Hashear("Clave-Muy-Larga-2026").Split('$');

        Assert.Equal(4, partes.Length);
        Assert.Equal("pbkdf2-sha512", partes[0]);
        Assert.True(int.Parse(partes[1], System.Globalization.CultureInfo.InvariantCulture) >= 210_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("corta")]
    [InlineData("12345678901")]
    public void Se_rechaza_una_contrasena_que_no_cumple_la_politica(string candidata)
    {
        Assert.ThrowsAny<ArgumentException>(() => hasheador.Hashear(candidata));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-formato")]
    [InlineData("pbkdf2-sha512$210000$solo-tres-partes")]
    [InlineData("desconocido$210000$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha512$no-numero$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha512$210000$!!!$aGFzaA==")]
    public void Un_hash_con_formato_invalido_no_verifica_y_no_revienta(string hash)
    {
        Assert.False(hasheador.Verificar("Clave-Muy-Larga-2026", hash));
    }

    /// <summary>Verificar siempre cuesta lo mismo: un atajo rápido delataría qué usuarios existen.</summary>
    [Fact]
    public void Verificar_un_hash_ausente_consume_trabajo_equivalente()
    {
        var hash = hasheador.Hashear("Clave-Muy-Larga-2026");

        var conUsuario = Medir(() => hasheador.Verificar("Clave-Equivocada-2026", hash));
        var sinUsuario = Medir(() => hasheador.VerificarContraFantasma("Clave-Equivocada-2026"));

        // Margen amplio a propósito: solo se comprueba que no hay un atajo inmediato.
        Assert.InRange(sinUsuario.TotalMilliseconds, conUsuario.TotalMilliseconds / 4, conUsuario.TotalMilliseconds * 4);
    }

    private static TimeSpan Medir(Action accion)
    {
        accion();

        var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < 3; i++)
        {
            accion();
        }

        return System.Diagnostics.Stopwatch.GetElapsedTime(inicio);
    }
}
