using System.Windows;

namespace Emulator.Controls;

public partial class SettingsDialog : Window
{
    public LocalSettings Result { get; } = new();

    public SettingsDialog(LocalSettings current)
    {
        InitializeComponent();
        Owner               = Application.Current.MainWindow;
        RestPortBox.Text       = current.RestPort.ToString();
        MqttBrokerHostBox.Text = current.MqttBrokerHost;
        MqttBrokerPortBox.Text = current.MqttBrokerPort.ToString();
    }

    private void Confirm_Click(object s, RoutedEventArgs e)
    {
        Result.RestPort       = int.TryParse(RestPortBox.Text,       out var p) ? p : 5555;
        Result.MqttBrokerHost = MqttBrokerHostBox.Text.Trim();
        Result.MqttBrokerPort = int.TryParse(MqttBrokerPortBox.Text, out var mp) ? mp : 1883;
        DialogResult = true;
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
