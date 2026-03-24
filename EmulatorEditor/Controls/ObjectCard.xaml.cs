using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shared.Models;

namespace EmulatorEditor.Controls;

public partial class ObjectCard : UserControl
{
    public EmulatorObject ObjectData { get; private set; }

    public event Action<ObjectCard>? Selected;
    public event Action<ObjectCard, double, double>? Moved;

    private Point _dragStart;
    private bool _isDragging;

    public ObjectCard(EmulatorObject obj)
    {
        InitializeComponent();
        ObjectData = obj;

        Canvas.SetLeft(this, obj.Position.X);
        Canvas.SetTop(this, obj.Position.Y);

        Loaded += (_, _) => RefreshProperties();
    }

    public void SetSelected(bool selected)
    {
        CardBorder.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA))
            : new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
        CardBorder.BorderThickness = new Thickness(selected ? 2 : 1);
    }

    public void RefreshTitle()
    {
        TitleText.Text = ObjectData.MachineId;
        UnitIdText.Text = ObjectData.UnitId;
        TypeText.Text = ObjectData.Type;
    }

    public void RefreshProperties()
    {
        RefreshTitle();
        PropList.ItemsSource = ObjectData.Properties
            .Where(kv => kv.Value.Type != "button")
            .Select(kv => $"{kv.Key}: {PropValueToString(kv.Value)}")
            .ToList();
    }

    private static string PropValueToString(ObjectProperty prop) => prop.Value.ValueKind switch
    {
        JsonValueKind.String    => prop.Value.GetString() ?? "",
        JsonValueKind.True      => "true",
        JsonValueKind.False     => "false",
        JsonValueKind.Undefined => "",
        _                       => prop.Value.ToString()
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragStart = e.GetPosition(Parent as UIElement);
        _isDragging = true;
        CaptureMouse();
        Selected?.Invoke(this);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging) return;

        var pos = e.GetPosition(Parent as UIElement);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        _dragStart = pos;

        var newX = Canvas.GetLeft(this) + dx;
        var newY = Canvas.GetTop(this) + dy;
        Canvas.SetLeft(this, newX);
        Canvas.SetTop(this, newY);
        Moved?.Invoke(this, newX, newY);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _isDragging = false;
        ReleaseMouseCapture();
    }
}
