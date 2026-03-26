using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EmulatorEditor.ViewModels;
using Shared.Models;

namespace EmulatorEditor.Controls;

public partial class PropertiesPanel : UserControl
{
    private EmulatorObject? _obj;
    private bool _loading;
    private readonly Dictionary<string, CheckBox>  _restCmdCheckboxes  = new();
    private ObservableCollection<string>           _restCustomCmds     = new();

    public event Action? BeforeApply;
    public event Action? ObjectChanged;
    public event Action<EmulatorObject>? DeleteRequested;

    private ObservableCollection<SubscribeTopic> _subscribeTopics = new();
    private ObservableCollection<TopicItem> _publishTopics = new();
    private ObservableCollection<PropertyItem> _properties = new();

    private static readonly HashSet<string> _predefinedKeys =
        EditorViewModel.PredefinedProps.Select(p => p.Key).ToHashSet();

    // URL labels per protocol
    private static readonly Dictionary<string, string> _urlLabels = new()
    {
        ["rest"]      = "Base URL",
        ["websocket"] = "WebSocket URL  (ws:// or wss://)",
        ["opcua"]     = "Server URL  (opc.tcp://host:port)",
        ["grpc"]      = "Endpoint  (host:port)",
        ["ros2"]      = "ROS Master URI",
    };

    public PropertiesPanel()
    {
        InitializeComponent();
    }

    public void LoadObject(EmulatorObject? obj, IEnumerable<string> brokerKeys)
    {
        _obj = obj;

        if (obj == null)
        {
            EmptyText.Visibility = Visibility.Visible;
            EditPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;

        _loading = true;

        // Basic info
        MachineIdBox.Text = obj.MachineId;
        UnitIdBox.Text    = obj.UnitId;

        foreach (ComboBoxItem item in TypeCombo.Items)
        {
            if (item.Content?.ToString() == obj.Type) { TypeCombo.SelectedItem = item; break; }
        }

        // Protocol selector
        var protocol = string.IsNullOrEmpty(obj.Communication.Protocol) ? "mqtt" : obj.Communication.Protocol;
        ProtocolCombo.SelectedIndex = -1;
        foreach (ComboBoxItem item in ProtocolCombo.Items)
        {
            if (item.Tag?.ToString() == protocol) { ProtocolCombo.SelectedItem = item; break; }
        }
        if (ProtocolCombo.SelectedIndex == -1) ProtocolCombo.SelectedIndex = 0;
        ApplyProtocolSection(protocol);

        // MQTT fields
        BrokerCombo.Items.Clear();
        foreach (var key in brokerKeys) BrokerCombo.Items.Add(key);
        var brokerRef = string.IsNullOrEmpty(obj.Communication.BrokerRef) ? "local" : obj.Communication.BrokerRef;
        BrokerCombo.SelectedItem = brokerRef;

        _subscribeTopics = new ObservableCollection<SubscribeTopic>(obj.Communication.SubscribeTopics);
        SubscribeTopicList.ItemsSource = _subscribeTopics;

        _publishTopics = new ObservableCollection<TopicItem>(
            obj.Communication.PublishTopics.Select(t => new TopicItem { Value = t }));
        PublishTopicList.ItemsSource = _publishTopics;

        // URL section
        ConnectionUrlBox.Text  = obj.Communication.ConnectionUrl;
        RestListenPathBox.Text = $"/{obj.UnitId}";
        UpdateRestSection(obj.UnitId, obj.Communication.RestCommands);

        // Modbus fields
        ModbusHostBox.Text   = obj.Communication.ModbusHost;
        ModbusPortBox.Text   = obj.Communication.ModbusPort.ToString();
        ModbusUnitIdBox.Text = obj.Communication.ModbusUnitId.ToString();

        // XCP fields
        var xcp = obj.Communication.Xcp ?? new XcpConfig();
        UsesXcpCheck.IsChecked  = obj.Communication.UsesXcp;
        XcpPanel.Visibility     = obj.Communication.UsesXcp ? Visibility.Visible : Visibility.Collapsed;
        XcpPrefixBox.Text       = xcp.Prefix;
        XcpIdentifierBox.Text   = xcp.Identifier;
        XcpVersionBox.Text      = xcp.Version;
        XcpTargetBox.Text = obj.UnitId;
        var opts = xcp.OptionalCommands;
        XcpStart.IsChecked      = opts.Contains("start");
        XcpStop.IsChecked       = opts.Contains("stop");
        XcpPause.IsChecked      = opts.Contains("pause");
        XcpResume.IsChecked     = opts.Contains("resume");
        XcpAbort.IsChecked      = opts.Contains("abort");
        XcpEnd.IsChecked        = opts.Contains("end");

        // ── Properties ────────────────────────────────────────────────────────
        _properties = new ObservableCollection<PropertyItem>();

        foreach (var (key, propType, defVal) in EditorViewModel.PredefinedProps)
        {
            var isEnabled = obj.Properties.TryGetValue(key, out var saved);
            var item = new PropertyItem
            {
                Key          = key,
                Type         = propType,
                DefaultValue = isEnabled ? ValueToString(saved!) : defVal,
                IsEnabled    = isEnabled,
                IsPredefined = true
            };
            if (isEnabled && saved != null)
            {
                // Restore enum values and button config
                if (propType == "enum")
                    item.EnumValuesRaw = string.Join(", ", saved.EnumValues);
                else if (propType == "button")
                {
                    item.OnClickTopic   = saved.OnClickTopic;
                    item.OnClickPayload = saved.OnClickPayload;
                }
                item.IsReadOnly = saved.IsReadOnly;
            }
            _properties.Add(item);
        }

        foreach (var kv in obj.Properties.Where(kv => !_predefinedKeys.Contains(kv.Key)))
        {
            var item = new PropertyItem
            {
                Key          = kv.Key,
                Type         = kv.Value.Type,
                DefaultValue = ValueToString(kv.Value),
                IsEnabled    = true,
                IsPredefined = false,
                IsReadOnly   = kv.Value.IsReadOnly,
            };
            if (kv.Value.Type == "enum")
                item.EnumValuesRaw = string.Join(", ", kv.Value.EnumValues);
            else if (kv.Value.Type == "button")
            {
                item.OnClickTopic   = kv.Value.OnClickTopic;
                item.OnClickPayload = kv.Value.OnClickPayload;
            }
            _properties.Add(item);
        }

        PropertyList.ItemsSource = _properties;
        _loading = false;
    }

    // ── Protocol visibility ────────────────────────────────────────────────────
    private static readonly string[] RestCmdNames =
        ["start", "stop", "pause", "resume", "abort", "end", "status", "sync", "auto", "manual"];

    private void UpdateRestSection(string unitId, List<string>? enabledCmds)
    {
        if (RestListenHint == null || RestCmdGrid == null) return;

        var path = string.IsNullOrWhiteSpace(unitId) ? "/" : $"/{unitId.TrimStart('/')}";
        RestListenHint.Text = $"← http://localhost:5555{path}";

        // predefined 체크박스 재구성
        RestCmdGrid.Children.Clear();
        _restCmdCheckboxes.Clear();
        foreach (var cmd in RestCmdNames)
        {
            var cb = new CheckBox
            {
                Content    = cmd,
                IsChecked  = enabledCmds == null || enabledCmds.Contains(cmd),
                Margin     = new System.Windows.Thickness(0, 2, 8, 2),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize   = 11,
            };
            _restCmdCheckboxes[cmd] = cb;
            RestCmdGrid.Children.Add(cb);
        }

        // 커스텀 커맨드 (predefined에 없는 것들)
        _restCustomCmds = new ObservableCollection<string>(
            enabledCmds?.Where(c => !RestCmdNames.Contains(c)) ?? []);
        RestCustomCmdList.ItemsSource = _restCustomCmds;
    }

    private void AddRestCmd_Click(object s, RoutedEventArgs e)
    {
        var cmd = NewRestCmdBox.Text.Trim().ToLower();
        if (string.IsNullOrEmpty(cmd) || RestCmdNames.Contains(cmd) || _restCustomCmds.Contains(cmd)) return;
        _restCustomCmds.Add(cmd);
        NewRestCmdBox.Clear();
    }

    private void RemoveRestCmd_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is string cmd)
            _restCustomCmds.Remove(cmd);
    }

    private List<string> BuildRestCommands()
        => _restCmdCheckboxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .Concat(_restCustomCmds)
            .ToList();

    private void ApplyProtocolSection(string protocol)
    {
        MqttSection.Visibility         = protocol == "mqtt"   ? Visibility.Visible : Visibility.Collapsed;
        ModbusSection.Visibility       = protocol == "modbus" ? Visibility.Visible : Visibility.Collapsed;
        UrlSection.Visibility          = (protocol != "mqtt" && protocol != "modbus")
                                         ? Visibility.Visible : Visibility.Collapsed;
        RestListenPathPanel.Visibility = protocol == "rest"   ? Visibility.Visible : Visibility.Collapsed;

        if (_urlLabels.TryGetValue(protocol, out var label))
            ConnectionUrlLabel.Text = label;
    }

    private void ProtocolCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var protocol = (ProtocolCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mqtt";
        ApplyProtocolSection(protocol);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static string ValueToString(ObjectProperty prop) => prop.Value.ValueKind switch
    {
        JsonValueKind.String    => prop.Value.GetString() ?? "",
        JsonValueKind.True      => "true",
        JsonValueKind.False     => "false",
        JsonValueKind.Undefined => "",
        _                       => prop.Value.ToString()
    };

    // ── Apply / Flush ──────────────────────────────────────────────────────────
    private void Apply_Click(object s, RoutedEventArgs e)
    {
        BeforeApply?.Invoke();
        Flush();
    }

    private void Flush()
    {
        if (_obj == null || _loading) return;

        if (!ValidateProperties(out var err))
        {
            MessageBox.Show(err, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _obj.MachineId = MachineIdBox.Text;
        _obj.UnitId    = UnitIdBox.Text;
        _obj.Type      = (TypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _obj.Type;

        var protocol = (ProtocolCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mqtt";
        _obj.Communication.Protocol = protocol;

        switch (protocol)
        {
            case "mqtt":
                _obj.Communication.BrokerRef       = BrokerCombo.SelectedItem as string ?? "local";
                _obj.Communication.SubscribeTopics = _subscribeTopics.ToList();
                _obj.Communication.PublishTopics   = _publishTopics.Select(t => t.Value).ToList();
                _obj.Communication.UsesXcp         = UsesXcpCheck.IsChecked == true;
                _obj.Communication.Xcp             = _obj.Communication.UsesXcp
                    ? new XcpConfig
                    {
                        Prefix           = XcpPrefixBox.Text,
                        Identifier       = XcpIdentifierBox.Text,
                        Version          = XcpVersionBox.Text,
                        Target           = "",   // 런타임에 UnitId 사용
                        OptionalCommands = BuildXcpOptionalCommands()
                    }
                    : null;
                break;
            case "modbus":
                _obj.Communication.ModbusHost   = ModbusHostBox.Text;
                _obj.Communication.ModbusPort   = int.TryParse(ModbusPortBox.Text, out var p) ? p : 502;
                _obj.Communication.ModbusUnitId = int.TryParse(ModbusUnitIdBox.Text, out var u) ? u : 1;
                break;
            case "rest":
                _obj.Communication.ConnectionUrl  = ConnectionUrlBox.Text;
                _obj.Communication.RestListenPath = $"/{_obj.UnitId}";
                _obj.Communication.RestCommands   = BuildRestCommands();
                break;
            default:
                _obj.Communication.ConnectionUrl = ConnectionUrlBox.Text;
                break;
        }

        _obj.Properties.Clear();
        foreach (var prop in _properties.Where(p => p.IsEnabled))
        {
            _obj.Properties[prop.Key] = new ObjectProperty
            {
                Type           = prop.Type,
                Value          = SerializeValue(prop),
                EnumValues     = prop.Type == "enum"   ? prop.EnumValuesList : new(),
                OnClickTopic   = prop.Type == "button" ? prop.OnClickTopic   : "",
                OnClickPayload = prop.Type == "button" ? prop.OnClickPayload : "{}",
                IsReadOnly     = prop.IsReadOnly,
            };
        }

        ObjectChanged?.Invoke();
    }

    private bool ValidateProperties(out string error)
    {
        // Empty key check
        var emptyKey = _properties.FirstOrDefault(p => !p.IsPredefined && string.IsNullOrWhiteSpace(p.Key));
        if (emptyKey != null) { error = "Property name cannot be empty."; return false; }

        // Duplicate key check (across all, enabled or not)
        var allCustomKeys = _properties.Where(p => !p.IsPredefined).Select(p => p.Key).ToList();
        var allKeys = _predefinedKeys.Concat(allCustomKeys).ToList();
        var dup = allKeys.GroupBy(k => k).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) { error = $"Duplicate property name: \"{dup.Key}\""; return false; }

        error = "";
        return true;
    }

    private static JsonElement SerializeValue(PropertyItem p) => p.Type switch
    {
        "float"  => double.TryParse(p.DefaultValue, out var d)
                    ? JsonSerializer.Deserialize<JsonElement>(d.ToString())
                    : JsonSerializer.Deserialize<JsonElement>("0"),
        "int"    => int.TryParse(p.DefaultValue, out var i)
                    ? JsonSerializer.Deserialize<JsonElement>(i.ToString())
                    : JsonSerializer.Deserialize<JsonElement>("0"),
        "bool"   => JsonSerializer.Deserialize<JsonElement>(p.BoolDefaultValue ? "true" : "false"),
        "button" => JsonSerializer.Deserialize<JsonElement>("\"\""),
        "enum"   => JsonSerializer.Deserialize<JsonElement>($"\"{p.DefaultValue}\""),
        _        => JsonSerializer.Deserialize<JsonElement>($"\"{p.DefaultValue}\""),
    };

    // ── Event handlers ─────────────────────────────────────────────────────────
    private void TypeCombo_SelectionChanged(object s, SelectionChangedEventArgs e) { }

    private void AddSubscribeTopic_Click(object s, RoutedEventArgs e)
        => _subscribeTopics.Add(new SubscribeTopic { Topic = "topic/new" });

    private void RemoveSubscribeTopic_Click(object s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is SubscribeTopic t) _subscribeTopics.Remove(t);
    }

    private void AddParseField_Click(object s, RoutedEventArgs e)
    {
        // ParseFields가 ObservableCollection이므로 Add만 해도 ItemsControl이 자동 갱신됩니다.
        if ((s as Button)?.Tag is SubscribeTopic t)
            t.ParseFields.Add(new ParseField { Key = "field", Type = "string" });
    }

    private void RemoveParseField_Click(object s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is ParseField pf)
            foreach (var t in _subscribeTopics)
                t.ParseFields.Remove(pf);
    }

    private void AddPublishTopic_Click(object s, RoutedEventArgs e)
        => _publishTopics.Add(new TopicItem { Value = "topic/new" });

    private void RemovePublishTopic_Click(object s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is TopicItem t) _publishTopics.Remove(t);
    }

    private void AddProperty_Click(object s, RoutedEventArgs e)
    {
        var allKeys = _properties.Select(p => p.Key).ToHashSet();
        var idx = 1;
        while (allKeys.Contains($"Prop{idx}")) idx++;
        _properties.Add(new PropertyItem { Key = $"Prop{idx}", Type = "string", IsPredefined = false });
    }

    private void DeleteProperty_Click(object s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is PropertyItem p) _properties.Remove(p);
    }

    private void DeleteObject_Click(object s, RoutedEventArgs e)
    {
        if (_obj != null) DeleteRequested?.Invoke(_obj);
    }

    // ── UnitId → XcpTarget / RestListenPath 강제 동기화 ──────────────────────
    private void UnitIdBox_TextChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var unitId = UnitIdBox.Text;
        XcpTargetBox.Text      = unitId;
        RestListenPathBox.Text = $"/{unitId}";
        UpdateRestSection(unitId, null);
    }

    // ── XCP ────────────────────────────────────────────────────────────────────
    private void UsesXcpCheck_Changed(object s, RoutedEventArgs e)
    {
        if (_loading) return;
        XcpPanel.Visibility = UsesXcpCheck.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private List<string> BuildXcpOptionalCommands()
    {
        var cmds = new List<string>();
        if (XcpStart.IsChecked  == true) cmds.Add("start");
        if (XcpStop.IsChecked   == true) cmds.Add("stop");
        if (XcpPause.IsChecked  == true) cmds.Add("pause");
        if (XcpResume.IsChecked == true) cmds.Add("resume");
        if (XcpAbort.IsChecked  == true) cmds.Add("abort");
        if (XcpEnd.IsChecked    == true) cmds.Add("end");
        return cmds;
    }
}
