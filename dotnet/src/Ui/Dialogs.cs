using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BackOffice.Ui;

/// <summary>Boîtes de dialogue minimales (Avalonia n'en fournit pas nativement).</summary>
internal static class Dialogs
{
    public static Task ShowInfoAsync(Window owner, string title, string message)
        => ShowAsync(owner, title, message, "OK", null);

    public static Task ShowWarningAsync(Window owner, string title, string message)
        => ShowAsync(owner, title, message, "OK", null);

    public static Task ShowErrorAsync(Window owner, string title, string message)
        => ShowAsync(owner, title, message, "OK", null);

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
        => await ShowAsync(owner, title, message, "Oui", "Non");

    private static async Task<bool> ShowAsync(
        Window owner, string title, string message, string okText, string? cancelText)
    {
        var confirmed = false;

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#141821")),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var okButton = new Button
        {
            Content = okText,
            MinWidth = 96,
            IsDefault = true,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        okButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        buttons.Children.Add(okButton);

        if (cancelText is not null)
        {
            var cancelButton = new Button
            {
                Content = cancelText,
                MinWidth = 96,
                IsCancel = true,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            cancelButton.Click += (_, _) =>
            {
                confirmed = false;
                dialog.Close();
            };
            buttons.Children.Add(cancelButton);
        }

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
        return confirmed;
    }
}
