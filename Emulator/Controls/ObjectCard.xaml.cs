using System.Windows;
using System.Windows.Controls;
using Emulator.ViewModels;

namespace Emulator.Controls;

public partial class ObjectCard : UserControl
{
    public event Func<ObjectViewModel, Task>? ConnectToggleRequested;

    public ObjectCard()
    {
        InitializeComponent();
    }

    private async void ConnectToggle_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is ObjectViewModel vm && ConnectToggleRequested != null)
            await ConnectToggleRequested(vm);
    }

    private async void PropertyButton_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is PropertyViewModel pvm && DataContext is ObjectViewModel vm)
        {
            try { await vm.PublishButtonAsync(pvm); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Publish Error"); }
        }
    }

    /// <summary>
    /// bool 체크박스 클릭 → 값 토글(TwoWay 바인딩이 먼저 업데이트) → MQTT/REST 발행.
    /// 발행 payload: {"Key": true} 또는 {"Key": false}
    /// </summary>
    private void ShowInfo_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is ObjectViewModel vm)
            new ObjectInfoDialog(vm.GetInfoData()) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async void BoolCheckBox_Click(object s, RoutedEventArgs e)
    {
        if (s is CheckBox cb && cb.Tag is PropertyViewModel pvm && DataContext is ObjectViewModel vm)
        {
            try { await vm.PublishBoolAsync(pvm); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Publish Error"); }
        }
    }
}
