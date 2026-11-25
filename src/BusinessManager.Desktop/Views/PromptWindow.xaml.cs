using System.Windows;

namespace BusinessManager.Desktop.Views;

public partial class PromptWindow : Window
{
    public PromptWindow(string message, string title, int maxLength)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        Input.MaxLength = maxLength;
    }

    public string Value => Input.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
