using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Emulator.Protocols;
using Emulator.Services;
using Shared.Models;

namespace Emulator.ViewModels;

public class PropertyViewModel : INotifyPropertyChanged
{
    private object? _value;

    public string Key { get; set; } = "";
    public string Type { get; set; } = "string";

    public object? Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public List<string> EnumValues { get; set; } = new();

    public string OnClickTopic { get; set; } = "";
    public string OnClickPayload { get; set; } = "{}";

    public bool IsReadOnly { get; set; } = false;

    public bool IsBool   => Type == "bool"   && !IsReadOnly;
    public bool IsEnum   => Type == "enum"   && !IsReadOnly;
    public bool IsButton => Type == "button";
    public bool IsNotButton => Type != "button";
    public bool IsText   => (!IsBool && !IsEnum && !IsButton) || IsReadOnly && Type != "button";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public record ObjectInfoData(
    string UnitId,
    string Name,
    string Type,
    string Protocol,
    string? RestListenPath,
    string? RestCallbackUrl,
    int RestPort,
    IReadOnlyList<string> RestCommands,
    string? BrokerAddress,
    IReadOnlyList<string> SubscribeTopics,
    IReadOnlyList<string> PublishTopics,
    bool UsesXcp,
    string? XcpPrefix,
    string? XcpIdentifier,
    string? XcpVersion,
    string? XcpTarget
)
{
    public bool IsRest => Protocol == "rest";
    public bool IsMqtt => Protocol == "mqtt";
    public bool HasRestCallback => !string.IsNullOrWhiteSpace(RestCallbackUrl);

    /// <summary>연결정보 다이얼로그용 — 등록된 전체 엔드포인트 문자열 목록</summary>
    public IReadOnlyList<string> RestEndpoints =>
        IsRest
            ? new[] { $"GET  http://localhost:{RestPort}{RestListenPath}/status" }
              .Concat(RestCommands.Select(c => $"POST http://localhost:{RestPort}{RestListenPath}/cmd/{c}"))
              .ToList()
            : [];
}

public class ObjectViewModel : INotifyPropertyChanged
{
    private bool _isConnected;
    private double _progress;
    private readonly EmulatorObject _model;

    // MQTT 전용
    private MqttService? _mqtt;
    private string? _mqttBrokerAddress;
    private CommandChannel? _channel;

    // REST 전용
    private RestService? _restService;
    private string _restListenPath = "";
    private int _restListenPort;
    private List<string> _restRegisteredPaths = [];
    private volatile string _lastStatusJson = "{}";

    private readonly RobotCommandHandler _commandHandler;

    // 로거
    private readonly LogService? _logger;
    private Action<string, string>? _onMqttPublished;

    public string Name => _model.MachineId;
    public string UnitId => _model.UnitId;
    public string Type => _model.Type;
    public string Protocol => string.IsNullOrEmpty(_model.Communication.Protocol)
                              ? "mqtt" : _model.Communication.Protocol;
    public bool IsMqtt => Protocol == "mqtt";
    public bool IsRest => Protocol == "rest";

    public double CanvasX => _model.Position.X;
    public double CanvasY => _model.Position.Y;

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(ConnectLabel));
            OnPropertyChanged(nameof(ConnectTip));
        }
    }

    public double Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProgressVisible));
        }
    }

    public bool IsProgressVisible => _progress > 0 && _progress < 100;

    public SolidColorBrush StatusBrush => IsConnected
        ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))
        : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));

    public string ConnectLabel => "⏻";
    public string ConnectTip => Protocol switch
    {
        "mqtt" => IsConnected ? "Disconnect MQTT" : "Connect MQTT",
        "rest" => IsConnected
            ? $"Stop REST listener (port {_restListenPort})"
            : $"Start REST listener (port {_restListenPort})",
        _ => $"{Protocol.ToUpper()} — 미지원"
    };

    public ObservableCollection<PropertyViewModel> Properties { get; } = new();

    public ObjectViewModel(EmulatorObject model, LogService? logger = null)
    {
        _model = model;
        _logger = logger;
        _commandHandler = new RobotCommandHandler(model.UnitId);

        _logger?.Init(Name, $"로봇 초기화 — UnitId={UnitId}, Type={Type}, Protocol={Protocol}");

        foreach (var kv in model.Properties)
        {
            Properties.Add(new PropertyViewModel
            {
                Key            = kv.Key,
                Type           = kv.Value.Type,
                Value          = ResolveDefaultValue(kv.Key, kv.Value),
                EnumValues     = ResolveEnumValues(kv.Key, kv.Value),
                OnClickTopic   = kv.Value.OnClickTopic,
                OnClickPayload = kv.Value.OnClickPayload,
                IsReadOnly     = kv.Value.IsReadOnly,
            });
        }
    }

    public async Task ConnectMqttAsync(MqttService mqtt)
    {
        _logger?.Info(Name, $"MQTT 연결 시작 → {mqtt.BrokerAddress}");

        _mqtt = mqtt;
        _mqttBrokerAddress = mqtt.BrokerAddress;
        _channel = new CommandChannel();

        if (!mqtt.IsConnected)
            await mqtt.ConnectAsync();

        _logger?.Conn(Name, $"MQTT 연결 완료 ← {mqtt.BrokerAddress}");

        var comm = _model.Communication;

        if (comm.UsesXcp && comm.Xcp != null)
        {
            foreach (var topic in GetXcpSubscribeTopics())
            {
                var t = topic;
                await mqtt.SubscribeAsync(t, payload =>
                {
                    _logger?.Recv(Name, $"MqttRecv: {t} {payload}");
                    _channel.Post(() => ProcessMqttCommandAsync(t, payload, Enumerable.Empty<ParseField>()));
                    return Task.CompletedTask;
                });
            }
        }
        else
        {
            foreach (var sub in comm.SubscribeTopics)
            {
                var topic = sub.Topic;
                var fields = sub.ParseFields;
                await mqtt.SubscribeAsync(topic, payload =>
                {
                    _logger?.Recv(Name, $"MqttRecv: {topic} {payload}");
                    _channel.Post(() => ProcessMqttCommandAsync(topic, payload, fields));
                    return Task.CompletedTask;
                });
            }
        }

        // SEND 로그 — 이 VM의 publish topic으로 나가는 메시지만 캡처
        var myTopics = comm.UsesXcp && comm.Xcp != null
            ? new HashSet<string> { GetXcpStatusTopic() }
            : new HashSet<string>(comm.PublishTopics);

        _onMqttPublished = (topic, payload) =>
        {
            if (myTopics.Contains(topic))
                _logger?.Send(Name, $"MqttSend: {topic} {payload}");
        };
        _mqtt.MessagePublished += _onMqttPublished;

        IsConnected = true;
    }

    public Task ConnectRestAsync(RestService restService, int port)
    {
        _restService = restService;
        _restListenPath = _model.Communication.RestListenPath;
        _restListenPort = port;
        _channel = new CommandChannel();
        _restRegisteredPaths.Clear();
        _lastStatusJson = "{}";

        // 커맨드별 경로 등록: {basePath}/cmd/{command}
        var enabledCmds = _model.Communication.RestCommands;
        if (enabledCmds is null) return Task.CompletedTask;

        foreach (var cmd in enabledCmds)
        {
            var c = cmd;
            var path = $"{_restListenPath}/cmd/{c}";

            Func<string, Task<string?>> postHandler = payload =>
            {
                var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var ch = _channel;
                if (ch == null) tcs.TrySetResult(null);
                else ch.Post(async () => { await ProcessRestCmdAsync(c, payload); tcs.TrySetResult(null); });
                return tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => (string?)null);
            };

            restService.RegisterPath(path, postHandler);
            _restRegisteredPaths.Add(path);

            // status는 GET으로도 접근 가능
            if (c == "status")
            {
                restService.RegisterGetPath(path, () => postHandler("{}"));
                // GET은 UnregisterPath에서 같이 제거됨 (path 동일)
            }
        }


        IsConnected = true;
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        // 채널 종료 (남은 항목 처리 후 루프 중지)
        _channel?.Complete();
        _channel = null;

        if (IsRest)
        {
            foreach (var path in _restRegisteredPaths)
                _restService?.UnregisterPath(path);
            _restRegisteredPaths.Clear();
            _restService = null;
            IsConnected = false;
        }
        else
        {
            if (_mqtt == null) return;
            var comm = _model.Communication;
            var topics = comm.UsesXcp && comm.Xcp != null
                ? GetXcpSubscribeTopics()
                : comm.SubscribeTopics.Select(s => s.Topic);
            foreach (var t in topics)
                try { await _mqtt.UnsubscribeAsync(t); } catch { }

            // SEND 이벤트 해제
            if (_onMqttPublished != null)
            {
                _mqtt.MessagePublished -= _onMqttPublished;
                _onMqttPublished = null;
            }
            _mqtt = null;
            IsConnected = false;
            _logger?.Info(Name, "MQTT 연결 해제");
        }
    }

    public async Task PublishButtonAsync(PropertyViewModel pvm)
    {
        // "Complete" 버튼은 외부 publish 없이 채널을 통해 직접 RCP 완료 처리
        if (pvm.Key == "Complete" && _channel != null)
        {
            _channel.Post(() =>
            {
                var ctx = BuildRcpContext("complete-btn");
                return _commandHandler.HandleCompleteButtonAsync(ctx);
            });
            return;
        }

        var payload = string.IsNullOrWhiteSpace(pvm.OnClickPayload) ? "{}" : pvm.OnClickPayload;

        if (IsMqtt)
        {
            if (_mqtt == null || !_mqtt.IsConnected) return;
            if (!string.IsNullOrWhiteSpace(pvm.OnClickTopic))
                await _mqtt.PublishAsync(pvm.OnClickTopic, payload);
            else
                foreach (var t in _model.Communication.PublishTopics)
                    await _mqtt.PublishAsync(t, payload);
        }
        else if (IsRest)
        {
            if (_restService == null) return;
            var url = !string.IsNullOrWhiteSpace(pvm.OnClickTopic)
                      ? pvm.OnClickTopic
                      : _model.Communication.ConnectionUrl;
            await _restService.PostAsync(url, payload);
        }
    }

    public async Task PublishBoolAsync(PropertyViewModel pvm)
    {
        var boolVal = pvm.Value is bool b && b;
        var payload = $"{{\"{pvm.Key}\":{(boolVal ? "true" : "false")}}}";

        if (IsMqtt && _mqtt?.IsConnected == true)
        {
            foreach (var t in _model.Communication.PublishTopics)
                await _mqtt.PublishAsync(t, payload);
        }
        // REST: bool은 외부로 전송하지 않음 — 완료 시점에 status에 반영됨

        // BoolChanged 이벤트를 채널에 post (순차 처리)
        if (_channel != null)
        {
            var key = pvm.Key;
            var val = boolVal;
            _channel.Post(() =>
            {
                var ctx = BuildRcpContext($"bool/{key}");
                return _commandHandler.HandleBoolChangedAsync(key, val, ctx);
            });
        }
    }

    private async Task ProcessMqttCommandAsync(string topic, string payload, IEnumerable<ParseField> fields)
    {
        var ctx = BuildRcpContext(topic);
        var cmdType = XcpHelper.ParseCommandType(topic);

        if (cmdType == null)
        {
            await RcpHandler.OnMessageReceivedAsync(ctx);
            return;
        }

        switch (cmdType.Value)
        {
            case XcpCommandType.sync:
                await _commandHandler.HandleSyncAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpSyncCommand) ?? new RcpSyncCommand(0), ctx);
                break;
            case XcpCommandType.start:
                await _commandHandler.HandleStartAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpStartCommand) ?? new RcpStartCommand("", ""), ctx);
                break;
            case XcpCommandType.stop:
                await _commandHandler.HandleStopAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpStopCommand) ?? new RcpStopCommand(), ctx);
                break;
            case XcpCommandType.pause:
                await _commandHandler.HandlePauseAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpPauseCommand) ?? new RcpPauseCommand(), ctx);
                break;
            case XcpCommandType.resume:
                await _commandHandler.HandleResumeAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpResumeCommand) ?? new RcpResumeCommand(), ctx);
                break;
            case XcpCommandType.abort:
                await _commandHandler.HandleAbortAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpAbortCommand) ?? new RcpAbortCommand(), ctx);
                break;
            case XcpCommandType.end:
                await _commandHandler.HandleEndAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpEndCommand) ?? new RcpEndCommand(), ctx);
                break;
            case XcpCommandType.status: await _commandHandler.HandleStatusAsync(ctx); break;
            case XcpCommandType.auto: await _commandHandler.HandleAutoAsync(ctx); break;
            case XcpCommandType.manual: await _commandHandler.HandleManualAsync(ctx); break;
            default: await RcpHandler.OnMessageReceivedAsync(ctx); break;
        }
    }

    private async Task ProcessRestCommandAsync(string payload)
    {
        var parseFields = _model.Communication.SubscribeTopics.FirstOrDefault()?.ParseFields;
        if (parseFields is { Count: > 0 })
        {
            try
            {
                var doc = JsonDocument.Parse(payload);
                foreach (var field in parseFields)
                {
                    if (!doc.RootElement.TryGetProperty(field.Key, out var elem)) continue;
                    var targetKey = string.IsNullOrEmpty(field.MapsTo) ? field.Key : field.MapsTo;
                    var prop = Properties.FirstOrDefault(p => p.Key == targetKey);
                    if (prop == null) continue;
                    var val = ParseElement(elem, field.Type);
                    Application.Current.Dispatcher.Invoke(() => prop.Value = val);
                }
            }
            catch { }
        }
        else
        {
            try
            {
                var doc = JsonDocument.Parse(payload);
                foreach (var prop in Properties)
                    if (doc.RootElement.TryGetProperty(prop.Key, out var elem))
                    {
                        var val = ParseElement(elem, prop.Type);
                        Application.Current.Dispatcher.Invoke(() => prop.Value = val);
                    }
            }
            catch { }
        }

        var ctx = BuildRcpContext(_restListenPath);
        await RcpHandler.OnMessageReceivedAsync(ctx);
    }

    private async Task ProcessRestCmdAsync(string cmd, string payload)
    {
        var ctx = BuildRcpContext($"{_restListenPath}/cmd/{cmd}");
        switch (cmd)
        {
            case "start":
                await _commandHandler.HandleStartAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpStartCommand) ?? new RcpStartCommand("", ""), ctx);
                break;
            case "stop":
                await _commandHandler.HandleStopAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpStopCommand) ?? new RcpStopCommand(), ctx);
                break;
            case "pause":
                await _commandHandler.HandlePauseAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpPauseCommand) ?? new RcpPauseCommand(), ctx);
                break;
            case "resume":
                await _commandHandler.HandleResumeAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpResumeCommand) ?? new RcpResumeCommand(), ctx);
                break;
            case "abort":
                await _commandHandler.HandleAbortAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpAbortCommand) ?? new RcpAbortCommand(), ctx);
                break;
            case "end":
                await _commandHandler.HandleEndAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpEndCommand) ?? new RcpEndCommand(), ctx);
                break;
            case "status": await _commandHandler.HandleStatusAsync(ctx); break;
            case "sync":
                await _commandHandler.HandleSyncAsync(
                    JsonSerializer.Deserialize(payload, RcpContext.Default.RcpSyncCommand) ?? new RcpSyncCommand(0), ctx);
                break;
            case "auto": await _commandHandler.HandleAutoAsync(ctx); break;
            case "manual": await _commandHandler.HandleManualAsync(ctx); break;
        }
    }

    private RcpHandlerContext BuildRcpContext(string topic)
    {
        var snapshot = Properties.ToDictionary(p => p.Key, p => p.Value);

        var ctx = new RcpHandlerContext
        {
            Name = Name,
            Type = Type,
            Topic = topic,
            Properties = snapshot,
        };

        ctx.SetPropertyCallback = (key, value) =>
        {
            var pvm = Properties.FirstOrDefault(p => p.Key == key);
            if (pvm == null) return false;
            Application.Current.Dispatcher.Invoke(() => pvm.Value = value);
            return true;
        };

        ctx.PublishCallback = async jsonPayload =>
        {
            _lastStatusJson = jsonPayload;   // REST 응답 / GET /status 용으로 캡처

            if (IsMqtt && _mqtt?.IsConnected == true)
            {
                var comm = _model.Communication;
                if (comm.UsesXcp && comm.Xcp != null)
                    await _mqtt.PublishAsync(GetXcpStatusTopic(), jsonPayload);
                else
                    foreach (var t in comm.PublishTopics)
                        await _mqtt.PublishAsync(t, jsonPayload);
            }
            else if (IsRest && _restService != null)
                _ = _restService.PostAsync(_model.Communication.ConnectionUrl, jsonPayload); // 독립 이벤트 전송
        };

        ctx.SetProgressCallback = value => Application.Current.Dispatcher.Invoke(() => Progress = value);
        ctx.LogCallback = (cmd, seq) => _logger?.Info(Name, $"cmd={cmd} seq={seq}");
        ctx.EnqueueCallback = work => _channel?.Post(work);
        ctx.GetCurrentCallback = key => Application.Current.Dispatcher.Invoke(
            () => Properties.FirstOrDefault(p => p.Key == key)?.Value);
        return ctx;
    }

    private static object? ParseElement(JsonElement elem, string type) => type switch
    {
        "float" => elem.ValueKind == JsonValueKind.Number
                       ? elem.GetDouble()
                       : double.TryParse(elem.GetString(), out var d) ? d : 0.0,
        "int" => elem.ValueKind == JsonValueKind.Number
                       ? elem.GetInt32()
                       : int.TryParse(elem.GetString(), out var i) ? i : 0,
        "bool" => elem.ValueKind == JsonValueKind.True ||
                   (elem.ValueKind == JsonValueKind.String &&
                    bool.TryParse(elem.GetString(), out var b) && b),
        _ => elem.ValueKind == JsonValueKind.String ? elem.GetString() ?? "" : elem.ToString()
    };

    private static object? ResolveDefaultValue(string key, ObjectProperty prop)
    {
        if (prop.Type == "enum")
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var v = prop.Value.GetString();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return key switch
            {
                "Mode" => RcpDefaults.ModeDefault,
                "State" => RcpDefaults.StateDefault,
                _ => ""
            };
        }

        return prop.Type switch
        {
            "float" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : 0.0,
            "int" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32() : 0,
            "bool" => prop.Value.ValueKind == JsonValueKind.True,
            "button" => null,
            _ => prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : ""
        };
    }

    private static List<string> ResolveEnumValues(string key, ObjectProperty prop)
    {
        if (prop.Type != "enum") return new();
        if (prop.EnumValues.Count > 0) return prop.EnumValues;

        return key switch
        {
            "Mode" => RcpDefaults.ModeValues.ToList(),
            "State" => RcpDefaults.StateValues.ToList(),
            _ => new()
        };
    }

    private string GetXcpStatusTopic() => XcpHelper.StatusTopic(_model.Communication.Xcp!, _model.UnitId);
    private IEnumerable<string> GetXcpSubscribeTopics() => XcpHelper.CmdTopics(_model.Communication.Xcp!, _model.UnitId);

    public ObjectInfoData GetInfoData()
    {
        var comm = _model.Communication;
        var usesXcp = comm.UsesXcp && comm.Xcp != null;

        var subTopics = usesXcp
            ? GetXcpSubscribeTopics().ToList()
            : comm.SubscribeTopics.Select(s => s.Topic).ToList();

        var pubTopics = usesXcp
            ? [GetXcpStatusTopic()]
            : comm.PublishTopics.ToList();

        // 연결 여부 무관하게 config 기반으로 표시 (_restListenPort는 연결 전 0이므로 config에서 읽음)
        var restPath = IsRest
            ? (string.IsNullOrEmpty(comm.RestListenPath) ? $"/{UnitId}" : comm.RestListenPath)
            : null;

        return new ObjectInfoData(
            UnitId: UnitId,
            Name: Name,
            Type: Type,
            Protocol: Protocol,
            RestListenPath: restPath,
            RestCallbackUrl: IsRest ? comm.ConnectionUrl : null,
            RestPort: IsRest ? (_restListenPort > 0 ? _restListenPort : 5555) : 0,
            RestCommands: IsRest ? (IReadOnlyList<string>)(comm.RestCommands ?? []) : [],
            BrokerAddress: IsMqtt ? _mqttBrokerAddress : null,
            SubscribeTopics: subTopics,
            PublishTopics: pubTopics,
            UsesXcp: usesXcp,
            XcpPrefix: comm.Xcp?.Prefix,
            XcpIdentifier: comm.Xcp?.Identifier,
            XcpVersion: comm.Xcp?.Version,
            XcpTarget: comm.Xcp?.Target is { Length: > 0 } t ? t : UnitId
        );
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
