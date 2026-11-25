using System.Windows;
using BusinessManager.Desktop.Views;

namespace BusinessManager.Desktop.Services;

public interface IDialogService
{
    bool Confirm(string message, string title = "Please confirm");

    void ShowError(string message, string title = "Business Manager");

    void ShowInfo(string message, string title = "Business Manager");

    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    string? Prompt(string message, string title, int maxLength);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;

    public bool Confirm(string message, string title = "Please confirm") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message, string title = "Business Manager") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message, string title = "Business Manager") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? Prompt(string message, string title, int maxLength)
    {
        var dialog = new PromptWindow(message, title, maxLength) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
