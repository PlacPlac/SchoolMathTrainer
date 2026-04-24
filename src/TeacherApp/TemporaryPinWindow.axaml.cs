using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TeacherApp;

public partial class TemporaryPinWindow : Window
{
    private readonly string _temporaryPin;

    public TemporaryPinWindow()
        : this(string.Empty, string.Empty, string.Empty)
    {
    }

    public TemporaryPinWindow(string studentName, string loginCode, string temporaryPin)
    {
        _temporaryPin = temporaryPin?.Trim() ?? string.Empty;
        InitializeComponent();

        StudentNameTextBlock.Text = $"Žák: {studentName.Trim()}";
        LoginCodeTextBlock.Text = $"LoginCode: {loginCode.Trim()}";
        TemporaryPinTextBlock.Text = _temporaryPin;
        StatusTextBlock.Text = string.Empty;
    }

    private async void OnCopyPinClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null || string.IsNullOrWhiteSpace(_temporaryPin))
            {
                StatusTextBlock.Text = "PIN se nepodařilo zkopírovat do schránky.";
                return;
            }

            await clipboard.SetTextAsync(_temporaryPin);
            StatusTextBlock.Text = "PIN byl zkopírován do schránky.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            StatusTextBlock.Text = "PIN se nepodařilo zkopírovat do schránky.";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
