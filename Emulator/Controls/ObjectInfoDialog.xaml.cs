using System.Windows;
using Emulator.ViewModels;

namespace Emulator.Controls;

public partial class ObjectInfoDialog : Window
{
    public ObjectInfoDialog(ObjectInfoData data)
    {
        InitializeComponent();
        Title       = $"{data.UnitId} 연결정보";
        DataContext = data;
    }
}
