using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EmulatorEditor.Controls;
using EmulatorEditor.ViewModels;
using Shared.Models;

namespace EmulatorEditor;

public partial class MainWindow : Window
{
    private readonly EditorViewModel _vm = new();
    private readonly List<ObjectCard> _cards = new();
    private ObjectCard? _selectedCard;

    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "?";
        VersionText.Text = $"v{version}";

        PropertiesPanel.BeforeApply     += () => _vm.PushUndo();
        PropertiesPanel.ObjectChanged   += OnObjectChanged;
        PropertiesPanel.DeleteRequested += OnDeleteRequested;

        KeyDown += OnWindowKeyDown;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _vm.AutoSave();
    }

    // ── Keyboard shortcuts ────────────────────────────────
    private void OnWindowKeyDown(object s, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        // Don't intercept text editing shortcuts when a text box has focus
        if (Keyboard.FocusedElement is TextBox) return;

        switch (e.Key)
        {
            case Key.C:
                if (_selectedCard != null)
                {
                    _vm.CopyObject(_selectedCard.ObjectData);
                    e.Handled = true;
                }
                break;

            case Key.V:
                _vm.PushUndo();
                var pasted = _vm.PasteObject();
                if (pasted != null)
                {
                    AddCard(pasted);
                    ObjectList.ItemsSource = null;
                    ObjectList.ItemsSource = _vm.Objects;
                    SelectCard(_cards.Last());
                }
                e.Handled = true;
                break;

            case Key.Z:
                _vm.Undo();
                RefreshCanvas();
                ObjectList.ItemsSource = null;
                ObjectList.ItemsSource = _vm.Objects;
                e.Handled = true;
                break;
        }
    }

    // ── Toolbar ───────────────────────────────────────────
    private void New_Click(object s, RoutedEventArgs e)
    {
        _vm.NewConfig();
        RefreshCanvas();
        ObjectList.ItemsSource = null;
        ObjectList.ItemsSource = _vm.Objects;
    }

    private void Load_Click(object s, RoutedEventArgs e)
    {
        _vm.LoadConfig();
        RefreshCanvas();
        ObjectList.ItemsSource = null;
        ObjectList.ItemsSource = _vm.Objects;
    }

    private void Save_Click(object s, RoutedEventArgs e) => _vm.SaveConfig();

    // ── Object Palette ────────────────────────────────────
    private void AddObject_Click(object s, RoutedEventArgs e)
    {
        _vm.PushUndo();
        _vm.AddObject();
        var obj = _vm.Config.Objects.Last();
        AddCard(obj);
        ObjectList.ItemsSource = null;
        ObjectList.ItemsSource = _vm.Objects;
        SelectCard(_cards.Last());
    }

    private void ObjectList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (ObjectList.SelectedItem is EmulatorObject obj)
        {
            var card = _cards.FirstOrDefault(c => c.ObjectData == obj);
            if (card != null) SelectCard(card);
        }
    }

    private void Canvas_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        SelectCard(null);
    }

    // ── Card management ───────────────────────────────────
    private void AddCard(EmulatorObject obj)
    {
        var card = new ObjectCard(obj);
        card.Selected += SelectCard;
        card.Moved += (c, x, y) => _vm.UpdateObjectPosition(c.ObjectData, x, y);
        EditorCanvas.Children.Add(card);
        _cards.Add(card);
    }

    private void SelectCard(ObjectCard? card)
    {
        _selectedCard?.SetSelected(false);
        _selectedCard = card;
        card?.SetSelected(true);

        PropertiesPanel.LoadObject(card?.ObjectData, _vm.Config.Brokers.Keys);

        if (card != null)
            ObjectList.SelectedItem = card.ObjectData;
    }

    private void RefreshCanvas()
    {
        EditorCanvas.Children.Clear();
        _cards.Clear();
        _selectedCard = null;
        foreach (var obj in _vm.Config.Objects)
            AddCard(obj);
        PropertiesPanel.LoadObject(null, Enumerable.Empty<string>());
    }

    private void OnObjectChanged()
    {
        _selectedCard?.RefreshProperties();
        ObjectList.ItemsSource = null;
        ObjectList.ItemsSource = _vm.Objects;
    }

    private void OnDeleteRequested(EmulatorObject obj)
    {
        _vm.PushUndo();
        var card = _cards.FirstOrDefault(c => c.ObjectData == obj);
        if (card != null)
        {
            EditorCanvas.Children.Remove(card);
            _cards.Remove(card);
        }
        _vm.RemoveObject(obj);
        SelectCard(null);
        ObjectList.ItemsSource = null;
        ObjectList.ItemsSource = _vm.Objects;
    }
}
