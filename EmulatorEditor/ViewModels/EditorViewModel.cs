using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Win32;
using Shared;
using Shared.Models;

namespace EmulatorEditor.ViewModels;

public class EditorViewModel : INotifyPropertyChanged
{
    private EmulatorObject? _selectedObject;
    private EmulatorConfig _config = CreateDefaultConfig();
    private int _nextObjIdx = 1;

    // ── Undo ──────────────────────────────────────────────────────────────────
    private readonly List<string> _undoStack = new();
    public bool CanUndo => _undoStack.Count > 0;

    public void PushUndo()
    {
        if (_undoStack.Count >= 50) _undoStack.RemoveAt(0);
        _undoStack.Add(JsonSerializer.Serialize(_config));
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var json = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _config = JsonSerializer.Deserialize<EmulatorConfig>(json)!;
        SelectedObject = null;
        OnPropertyChanged(nameof(Objects));
    }

    // ── Clipboard ─────────────────────────────────────────────────────────────
    private EmulatorObject? _clipboard;
    public bool HasClipboard => _clipboard != null;

    public void CopyObject(EmulatorObject obj)
    {
        _clipboard = JsonSerializer.Deserialize<EmulatorObject>(JsonSerializer.Serialize(obj));
    }

    /// <summary>Returns the new pasted object, or null if clipboard is empty.</summary>
    public EmulatorObject? PasteObject()
    {
        if (_clipboard == null) return null;

        // Deep copy
        var obj = JsonSerializer.Deserialize<EmulatorObject>(JsonSerializer.Serialize(_clipboard))!;

        // Assign IDs the same way AddObject does
        var idx = _nextObjIdx++;
        obj.UnitId    = $"unit_{idx:D3}";
        obj.MachineId = $"{_clipboard.Type} {idx}";

        // Offset position so it doesn't perfectly overlap the original
        obj.Position = new Position { X = _clipboard.Position.X + 24, Y = _clipboard.Position.Y + 24 };

        _config.Objects.Add(obj);
        OnPropertyChanged(nameof(Objects));
        return obj;
    }

    // ── Standard ──────────────────────────────────────────────────────────────
    public ObservableCollection<EmulatorObject> Objects => new(_config.Objects);

    public EmulatorObject? SelectedObject
    {
        get => _selectedObject;
        set { _selectedObject = value; OnPropertyChanged(); }
    }

    public EmulatorConfig Config => _config;

    private static EmulatorConfig CreateDefaultConfig()
    {
        var config = new EmulatorConfig();
        config.Brokers["local"] = new BrokerConfig { Host = "localhost", Port = 1883 };
        return config;
    }

    public void AddObject()
    {
        var idx = _nextObjIdx++;
        var obj = new EmulatorObject
        {
            UnitId    = $"unit_{idx:D3}",
            MachineId = $"Robot {idx}",
            Type      = "Robot",
            Position  = new Position { X = 50 + (idx - 1) * 20, Y = 50 + (idx - 1) * 20 },
            Properties = CreateDefaultProperties("Robot")
        };
        _config.Objects.Add(obj);
        OnPropertyChanged(nameof(Objects));
    }

    // (Key, Type, DefaultValue) — order preserved in UI
    public static readonly (string Key, string Type, string Default)[] PredefinedProps =
    [
        ("Mode",            "enum",   ""),
        ("State",           "enum",   ""),
        ("Seq",             "int",    "0"),
        ("EventSeq",        "int",    "0"),
        ("CompleteReason",  "string", ""),
        ("JobId",           "string", ""),
        ("RecipeId",        "string", ""),
        ("ProductResultOk", "bool",   "false"),
        ("InfiniteRun",     "bool",   "false"),
        ("Complete",        "button", ""),
    ];

    public static Dictionary<string, ObjectProperty> CreateDefaultProperties(string type)
    {
        var result = new Dictionary<string, ObjectProperty>();
        foreach (var (key, propType, defVal) in PredefinedProps)
            result[key] = Prop(propType, defVal);
        return result;
    }

    private static ObjectProperty Prop(string type, string val)
    {
        var elem = type switch
        {
            "float" or "int" => string.IsNullOrEmpty(val)
                ? JsonSerializer.Deserialize<JsonElement>("0")
                : JsonSerializer.Deserialize<JsonElement>(val),
            "bool"           => JsonSerializer.Deserialize<JsonElement>(
                                    string.IsNullOrEmpty(val) ? "false" : val),
            "button"         => JsonSerializer.Deserialize<JsonElement>("\"\""),
            _                => JsonSerializer.Deserialize<JsonElement>($"\"{val}\""),
        };
        return new ObjectProperty { Type = type, Value = elem };
    }

    public void RemoveObject(EmulatorObject obj)
    {
        _config.Objects.Remove(obj);
        if (SelectedObject == obj) SelectedObject = null;
        OnPropertyChanged(nameof(Objects));
    }

    public void UpdateObjectPosition(EmulatorObject obj, double x, double y)
    {
        obj.Position.X = x;
        obj.Position.Y = y;
    }

    public void NewConfig()
    {
        _config = CreateDefaultConfig();
        _nextObjIdx = 1;
        _undoStack.Clear();
        SelectedObject = null;
        OnPropertyChanged(nameof(Objects));
    }

    public void LoadConfig()
    {
        var dlg = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        _config = ConfigSerializer.Load(dlg.FileName);
        _nextObjIdx = _config.Objects.Count + 1;
        _undoStack.Clear();
        SelectedObject = null;
        OnPropertyChanged(nameof(Objects));
    }

    public void SaveConfig()
    {
        var dlg = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = "config.json" };
        if (dlg.ShowDialog() != true) return;
        ConfigSerializer.Save(_config, dlg.FileName);
    }

    public void AutoSave()
    {
        try
        {
            var layoutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layout");
            Directory.CreateDirectory(layoutDir);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmm");
            ConfigSerializer.Save(_config, Path.Combine(layoutDir, $"layout_{stamp}.json"));
        }
        catch { /* silently ignore save errors on exit */ }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
