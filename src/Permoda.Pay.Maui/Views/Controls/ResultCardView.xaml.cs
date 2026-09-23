namespace Permoda.Pay.Maui.Views.Controls;

public partial class ResultCardView : ContentView
{
    public ResultCardView()
    {
        InitializeComponent();
    }

    /// <summary>Anima la entrada de la tarjeta de resultado (escala + fundido + deslizamiento).</summary>
    public async Task PlayAsync()
    {
        CardBorder.Opacity = 0;
        CardBorder.Scale = 0.85;
        CardBorder.TranslationY = 24;

        await Task.WhenAll(
            CardBorder.FadeToAsync(1, 250, Easing.CubicOut),
            CardBorder.ScaleToAsync(1, 380, Easing.SpringOut),
            CardBorder.TranslateToAsync(0, 0, 320, Easing.SpringOut));
    }
}
