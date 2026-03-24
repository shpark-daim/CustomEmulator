using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shared.Models;

public class CommunicationConfig
{
    /// <summary>mqtt | rest | websocket | opcua | modbus | ros2 | grpc</summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "mqtt";

    // ── MQTT ────────────────────────────────────────────────
    [JsonPropertyName("broker_ref")]
    public string BrokerRef { get; set; } = "local";

    [JsonPropertyName("subscribe_topics")]
    public List<SubscribeTopic> SubscribeTopics { get; set; } = [];

    [JsonPropertyName("publish_topics")]
    public List<string> PublishTopics { get; set; } = [];

    // ── URL-based (REST, WebSocket, OPC-UA, gRPC, ROS2) ────
    [JsonPropertyName("connection_url")]
    public string ConnectionUrl { get; set; } = "";

    /// <summary>REST only — inbound path this object listens on  e.g. "/robot/1"</summary>
    [JsonPropertyName("rest_listen_path")]
    public string RestListenPath { get; set; } = "";

    // ── XCP ────────────────────────────────────────────────
    [JsonPropertyName("uses_xcp")]
    public bool UsesXcp { get; set; } = false;

    [JsonPropertyName("xcp")]
    public XcpConfig? Xcp { get; set; }

    // ── Modbus TCP ──────────────────────────────────────────
    [JsonPropertyName("modbus_host")]
    public string ModbusHost { get; set; } = "localhost";

    [JsonPropertyName("modbus_port")]
    public int ModbusPort { get; set; } = 502;

    [JsonPropertyName("modbus_unit_id")]
    public int ModbusUnitId { get; set; } = 1;
}
