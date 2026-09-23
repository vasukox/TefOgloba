namespace Permoda.Pay.Maui.Tests.Pos;

/// <summary>
/// Regla de denominación del monto de activación: solo múltiplos de $10.000.
/// <para>
/// La lógica vive en ActivateTabViewModel, que arrastra sesión, handlers y navegación de MAUI —
/// montarlo en un test unitario obligaría a falsificar media app para verificar una operación de
/// aritmética. Se prueba la regla en sí, que es lo que puede romperse al tocarla, y su redacción
/// queda fijada por el propio mensaje del ViewModel.
/// </para>
/// </summary>
public sealed class ActivationAmountStepTests
{
    private const long Step = 10_000;
    private const long Min = 30_000;
    private const long Max = 1_000_000;

    private static bool IsValidDenomination(long amount) =>
        amount >= Min && amount <= Max && amount % Step == 0;

    /// <summary>El valor válido más cercano, que es lo que el mensaje le ofrece al cajero.</summary>
    private static long Nearest(long amount) =>
        Math.Clamp((long)Math.Round((double)amount / Step) * Step, Min, Max);

    [Theory]
    [InlineData(30_000)]
    [InlineData(40_000)]
    [InlineData(50_000)]
    [InlineData(120_000)]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    public void Denominaciones_redondas_dentro_del_rango_se_aceptan(long amount)
    {
        Assert.True(IsValidDenomination(amount));
    }

    [Theory]
    [InlineData(35_000)]    // múltiplo de 10 pesos, pero NO de 10.000
    [InlineData(30_010)]
    [InlineData(45_500)]
    [InlineData(99_999)]
    public void Montos_que_no_son_multiplos_de_diez_mil_se_rechazan(long amount)
    {
        Assert.False(IsValidDenomination(amount));
    }

    /// <summary>
    /// El tope del rango manda sobre el redondeo: sugerir un valor que después el propio campo
    /// rechaza por fuera de rango sería mandar al cajero a un callejón.
    /// </summary>
    [Theory]
    [InlineData(35_000, 40_000)]
    [InlineData(34_999, 30_000)]
    [InlineData(499_999, 500_000)]
    [InlineData(999_999, 1_000_000)]
    [InlineData(28_000, 30_000)]     // por debajo del mínimo: se sube al mínimo
    [InlineData(1_500_000, 1_000_000)] // por encima del máximo: se baja al máximo
    public void El_valor_sugerido_es_el_mas_cercano_y_siempre_dentro_del_rango(long typed, long expected)
    {
        var suggestion = Nearest(typed);

        Assert.Equal(expected, suggestion);
        Assert.True(IsValidDenomination(suggestion));
    }

    /// <summary>
    /// El rango sigue mandando aparte de la denominación: 20.000 es múltiplo redondo pero está
    /// por debajo del mínimo que acepta Ogloba.
    /// </summary>
    [Theory]
    [InlineData(10_000)]
    [InlineData(20_000)]
    [InlineData(1_010_000)]
    public void Multiplos_redondos_fuera_del_rango_se_rechazan(long amount)
    {
        Assert.Equal(0, amount % Step);
        Assert.False(IsValidDenomination(amount));
    }
}
