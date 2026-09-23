using Android.OS;
using Android.Widget;

namespace Permoda.Pay.Maui.Services;

public sealed class AndroidToastService : IToastService
{
    public void Show(string message, ToastLevel level = ToastLevel.Info)
    {
        var duration = level == ToastLevel.Error ? ToastLength.Long : ToastLength.Short;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var context = Android.App.Application.Context;
            Toast.MakeText(context, message, duration)?.Show();
        });
    }

    public void ShowInfo(string title, string message) => Show($"{title}: {message}", ToastLevel.Info);

    public void ShowSuccess(string title, string message) => Show($"{title}: {message}", ToastLevel.Success);

    public void ShowError(string title, string message) => Show($"{title}: {message}", ToastLevel.Error);
}