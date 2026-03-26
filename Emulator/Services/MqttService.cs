using MQTTnet;
using MQTTnet.Client;
using Shared.Models;

namespace Emulator;

public class MqttService : IAsyncDisposable
{
    private readonly BrokerConfig _broker;
    private readonly IMqttClient _client;
    private readonly Dictionary<string, Func<string, Task>> _handlers = new();

    public bool   IsConnected    => _client.IsConnected;
    public string BrokerAddress  => $"{_broker.Host}:{_broker.Port}";

    public event Action<string, string>? MessagePublished;

    public MqttService(BrokerConfig broker)
    {
        _broker = broker;
        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceived;
    }

    public async Task ConnectAsync()
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_broker.Host, _broker.Port)
            .WithCleanSession()
            .Build();
        await _client.ConnectAsync(options);
    }

    public async Task DisconnectAsync()
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync();
    }

    public async Task SubscribeAsync(string topic, Func<string, Task> handler)
    {
        _handlers[topic] = handler;
        if (!_client.IsConnected)
            await ConnectAsync();
        await _client.SubscribeAsync(new MqttTopicFilterBuilder()
            .WithTopic(topic).Build());
    }

    public async Task UnsubscribeAsync(string topic)
    {
        _handlers.Remove(topic);
        if (_client.IsConnected)
            await _client.UnsubscribeAsync(topic);
    }

    public async Task PublishAsync(string topic, string payload)
    {
        if (!_client.IsConnected) return;
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();
        await _client.PublishAsync(msg);
        MessagePublished?.Invoke(topic, payload);
    }

    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic   = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.ConvertPayloadToString();

        if (_handlers.TryGetValue(topic, out var handler))
            await handler(payload);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _client.Dispose();
    }
}
