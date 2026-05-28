using System.Windows;

namespace EyeVrrf.App;

public partial class ProgressWindow : Window
{
    public ProgressWindow()
    {
        InitializeComponent();
    }

    public void UpdateProgress(double percent, string message)
    {
        ProgressBar.Value = Math.Clamp(percent, 0.0, 100.0);
        MessageText.Text = $"{ProgressBar.Value:F0}%  {message}";
    }
}
