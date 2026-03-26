using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EmulatorEditor.ViewModels;

public class PropertyItem : INotifyPropertyChanged
{
    private string _key = "";
    private string _type = "string";
    private string _defaultValue = "";
    private bool _isEnabled = true;
    private bool _isPredefined;
    private string _enumValuesRaw = "";     // comma-separated; used when Type == "enum"
    private string _onClickTopic = "";      // used when Type == "button"
    private string _onClickPayload = "{}";  // used when Type == "button"
    private bool _isReadOnly = false;

    public string Key
    {
        get => _key;
        set { _key = value; OnPropertyChanged(); }
    }

    public string Type
    {
        get => _type;
        set
        {
            _type = value;
            // Reset DefaultValue for custom props when type changes
            if (!_isPredefined)
            {
                _defaultValue = value switch
                {
                    "int"   => "0",
                    "float" => "0.0",
                    "bool"  => "false",
                    _       => ""
                };
                OnPropertyChanged(nameof(DefaultValue));
                OnPropertyChanged(nameof(BoolDefaultValue));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBool));
            OnPropertyChanged(nameof(IsEnum));
            OnPropertyChanged(nameof(IsButton));
            OnPropertyChanged(nameof(IsValueEditable));
            OnPropertyChanged(nameof(IsNotBool));
        }
    }

    public string DefaultValue
    {
        get => _defaultValue;
        set { _defaultValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(BoolDefaultValue)); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public bool IsPredefined
    {
        get => _isPredefined;
        set { _isPredefined = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNameEditable)); }
    }

    // Enum values stored as comma-separated string for easy TextBox binding
    public string EnumValuesRaw
    {
        get => _enumValuesRaw;
        set { _enumValuesRaw = value; OnPropertyChanged(); }
    }

    // Parsed enum values list
    public List<string> EnumValuesList =>
        _enumValuesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // Button on-click config
    public string OnClickTopic
    {
        get => _onClickTopic;
        set { _onClickTopic = value; OnPropertyChanged(); }
    }

    public string OnClickPayload
    {
        get => _onClickPayload;
        set { _onClickPayload = value; OnPropertyChanged(); }
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set { _isReadOnly = value; OnPropertyChanged(); }
    }

    // ── Computed helpers ───────────────────────────────────────────────────────
    public bool IsNameEditable  => !_isPredefined;
    public bool IsBool          => Type == "bool";
    public bool IsEnum          => Type == "enum";
    public bool IsButton        => Type == "button";
    public bool IsNotBool       => Type != "bool";
    public bool IsValueEditable => Type != "bool" && Type != "button" && Type != "enum";

    public bool BoolDefaultValue
    {
        get => _defaultValue == "true";
        set { _defaultValue = value ? "true" : "false"; OnPropertyChanged(); OnPropertyChanged(nameof(DefaultValue)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TopicItem : INotifyPropertyChanged
{
    private string _value = "";
    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
