using GestorInve.Seguridad;

namespace GestorInve.Seguridad.Tests;

public class TokenRefrescoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duracion = TimeSpan.FromDays(14);

    [Fact]
    public void El_valor_en_claro_no_queda_guardado()
    {
        var (token, valor) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);

        Assert.NotEqual(valor, token.HashToken);
        Assert.DoesNotContain(valor, token.HashToken, StringComparison.Ordinal);
        Assert.Equal(TokenRefresco.Hashear(valor), token.HashToken);
    }

    [Fact]
    public void Dos_emisiones_dan_valores_distintos()
    {
        var (_, uno) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);
        var (_, otro) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);

        Assert.NotEqual(uno, otro);
    }

    [Fact]
    public void El_valor_es_apto_para_URL_y_tiene_entropia_suficiente()
    {
        var (_, valor) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);

        Assert.DoesNotContain('+', valor);
        Assert.DoesNotContain('/', valor);
        Assert.DoesNotContain('=', valor);
        Assert.True(valor.Length >= 40, $"Longitud inesperada: {valor.Length}");
    }

    [Fact]
    public void Un_token_recien_emitido_es_utilizable()
    {
        var (token, _) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);

        Assert.True(token.EsUtilizable(Ahora));
    }

    [Fact]
    public void Un_token_usado_deja_de_ser_utilizable_y_apunta_a_su_reemplazo()
    {
        var (token, _) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);
        var reemplazo = Guid.CreateVersion7();

        token.Usar(Ahora, reemplazo);

        Assert.False(token.EsUtilizable(Ahora));
        Assert.Equal(Ahora, token.UsadoEn);
        Assert.Equal(reemplazo, token.ReemplazadoPorId);
    }

    [Fact]
    public void Usar_dos_veces_el_mismo_token_es_un_error_de_programacion()
    {
        var (token, _) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);
        token.Usar(Ahora, Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(() => token.Usar(Ahora, Guid.CreateVersion7()));
    }

    [Fact]
    public void Un_token_revocado_no_es_utilizable()
    {
        var (token, _) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);

        token.Revocar(Ahora);

        Assert.False(token.EsUtilizable(Ahora));
        Assert.Equal(Ahora, token.RevocadoEn);
    }

    [Fact]
    public void Revocar_dos_veces_conserva_la_primera_fecha()
    {
        var (token, _) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);

        token.Revocar(Ahora);
        token.Revocar(Ahora.AddHours(1));

        Assert.Equal(Ahora, token.RevocadoEn);
    }

    [Fact]
    public void Un_token_caducado_no_es_utilizable()
    {
        var (token, _) = TokenRefresco.Emitir(Guid.CreateVersion7(), Ahora, Duracion);

        Assert.True(token.EsUtilizable(Ahora + Duracion - TimeSpan.FromSeconds(1)));
        Assert.False(token.EsUtilizable(Ahora + Duracion));
    }

    [Fact]
    public void La_rotacion_conserva_la_familia_para_poder_revocarla_completa()
    {
        var usuario = Guid.CreateVersion7();
        var (primero, _) = TokenRefresco.Emitir(usuario, Ahora, Duracion);

        var (segundo, _) = TokenRefresco.Emitir(usuario, Ahora.AddMinutes(10), Duracion, primero.FamiliaId);

        Assert.Equal(primero.FamiliaId, segundo.FamiliaId);
        Assert.NotEqual(primero.Id, segundo.Id);
    }

    [Fact]
    public void Un_inicio_de_sesion_nuevo_abre_una_familia_distinta()
    {
        var usuario = Guid.CreateVersion7();
        var (uno, _) = TokenRefresco.Emitir(usuario, Ahora, Duracion);
        var (otro, _) = TokenRefresco.Emitir(usuario, Ahora, Duracion);

        Assert.NotEqual(uno.FamiliaId, otro.FamiliaId);
    }

    [Fact]
    public void Hashear_exige_un_valor()
    {
        Assert.ThrowsAny<ArgumentException>(() => TokenRefresco.Hashear(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => TokenRefresco.Hashear("   "));
    }
}
