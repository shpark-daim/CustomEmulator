using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Emulator.Services;
using Shared;
using Shared.Models;

namespace Emulator.ViewModels;

public class EmulatorViewModel : INotifyPropertyChanged
{
    private EmulatorConfig? _config;

    /// <summary>앱 전체 로그 — UI에서 바인딩 가능</summary>
    public LogService Logger { get; } = new();

    /// <summary>Seq 서버 URL을 지정합니다. 기본 포트는 5341입니다.</summary>
    public void ConfigureSeq(string url = "http://localhost:5341", string? apiKey = null)
        => Logger.ConfigureSeq(url, apiKey);

    // MQTT: 브로커 키별 서비스 (여러 오브젝트가 공유)
    private readonly Dictionary<string, MqttService> _mqttServices = new();

    // REST: 단일 HTTP 서버 (여러 오브젝트가 공유)
    private RestService? _restService;

    public int    RestPort           { get; set; } = 5555;
    public string MqttBrokerHostOverride { get; set; } = "";
    public int    MqttBrokerPortOverride { get; set; } = 0;

    public ObservableCollection<ObjectViewModel> Objects { get; } = new();

    public void LoadConfig(string path)
    {
        _config = ConfigSerializer.Load(path);
        Objects.Clear();

        // 기존 서비스 정리
        foreach (var svc in _mqttServices.Values)
            _ = svc.DisconnectAsync();
        _mqttServices.Clear();

        if (_restService != null)
        {
            _ = _restService.StopAsync();
            _ = _restService.DisposeAsync().AsTask();
            _restService = null;
        }

        Logger.Init("System", $"Config 로드 완료 — 로봇 {_config.Objects.Count}대");

        foreach (var obj in _config.Objects)
            Objects.Add(new ObjectViewModel(obj, Logger));
    }

    public async Task ConnectAllAsync()
    {
        foreach (var obj in Objects)
        {
            try { await ConnectObjectAsync(obj); }
            catch (NotSupportedException) { /* 미지원 프로토콜 — 무시 */ }
        }
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var obj in Objects)
            await obj.DisconnectAsync();

        foreach (var (_, svc) in _mqttServices)
            try { await svc.DisconnectAsync(); } catch { }
        _mqttServices.Clear();

        if (_restService != null)
        {
            await _restService.StopAsync();
            await _restService.DisposeAsync();
            _restService = null;
        }
    }

    public async Task ConnectObjectAsync(ObjectViewModel objVm)
    {
        if (_config == null) return;

        var model = _config.Objects.FirstOrDefault(o => o.MachineId == objVm.Name);
        if (model == null) return;

        var protocol = string.IsNullOrEmpty(model.Communication.Protocol)
                       ? "mqtt" : model.Communication.Protocol;

        switch (protocol)
        {
            case "mqtt":
                await ConnectMqttObject(objVm, model);
                break;

            case "rest":
                await ConnectRestObject(objVm);
                break;

            default:
                throw new NotSupportedException(
                    $"\"{objVm.Name}\" uses protocol '{protocol.ToUpper()}' — 아직 지원되지 않습니다.");
        }
    }

    private async Task ConnectMqttObject(ObjectViewModel objVm, EmulatorObject model)
    {
        var brokerRef = model.Communication.BrokerRef;
        if (!_config!.Brokers.TryGetValue(brokerRef, out var brokerCfg))
            throw new InvalidOperationException(
                $"Broker '{brokerRef}' not found in config (object: \"{objVm.Name}\").");

        if (!string.IsNullOrEmpty(MqttBrokerHostOverride))
            brokerCfg.Host = MqttBrokerHostOverride;
        if (MqttBrokerPortOverride > 0)
            brokerCfg.Port = MqttBrokerPortOverride;

        if (!_mqttServices.TryGetValue(brokerRef, out var mqtt))
        {
            mqtt = new MqttService(brokerCfg);
            _mqttServices[brokerRef] = mqtt;
        }

        await objVm.ConnectMqttAsync(mqtt);
    }

    private async Task ConnectRestObject(ObjectViewModel objVm)
    {
        // REST 서버가 없으면 시작
        if (_restService == null)
        {
            _restService = new RestService(RestPort);
            await _restService.StartAsync();
        }

        await objVm.ConnectRestAsync(_restService, RestPort);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
