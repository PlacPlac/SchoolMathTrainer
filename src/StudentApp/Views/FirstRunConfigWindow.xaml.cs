using System.Windows;
using Microsoft.Win32;
using SharedCore.Services;

namespace StudentApp.Views;

public partial class FirstRunConfigWindow : Window
{
    private readonly SharedDataFolderSettingsService _settingsService = new();

    public FirstRunConfigWindow()
    {
        InitializeComponent();
    }

    private void OnLoadFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Soubor od paní učitelky (*.smtcfg)|*.smtcfg|Všechny soubory (*.*)|*.*",
            Title = "Načíst soubor od paní učitelky",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = _settingsService.ImportFromFile(dialog.FileName);
        if (!result.IsValid || string.IsNullOrWhiteSpace(result.StudentId))
        {
            DiagnosticLogService.Log("StudentApp", $"First run config import failed. Message: {result.Message}");
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(result.Message)
                ? "Soubor se nepodařilo načíst nebo neobsahuje žáka. Zkus to prosím znovu."
                : result.Message;
            return;
        }

        DiagnosticLogService.Log("StudentApp", $"First run config imported for class '{result.ClassId}', student '{result.StudentId}'.");
        DialogResult = true;
        Close();
    }
}
