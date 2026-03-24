using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Shared.Models;

public class SubscribeTopic
{
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = "";

    /// <summary>
    /// ObservableCollection으로 선언해야 WPF ItemsControl이
    /// Add/Remove 시 ItemsSource 리셋 없이 자동으로 갱신됩니다.
    /// </summary>
    [JsonPropertyName("parse_fields")]
    public ObservableCollection<ParseField> ParseFields { get; set; } = [];
}
