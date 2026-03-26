using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;

namespace Emulator.Services;

public record LogEntry(DateTime Time, string Level, string Source, string Message);

/// <summary>
/// 앱 전체 로그를 수집합니다.
/// Entries는 UI에 바인딩 가능한 ObservableCollection입니다.
/// Seq가 설정되어 있으면 CLEF 포맷으로 HTTP 전송합니다 (라이브러리 불필요).
/// </summary>
public class LogService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private string? _seqUrl;   // null이면 Seq 비활성화
    private string? _seqApiKey;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>Seq 서버 URL을 지정합니다. (예: "http://localhost:5341")</summary>
    public void ConfigureSeq(string url, string? apiKey = null)
    {
        _seqUrl    = url.TrimEnd('/');
        _seqApiKey = apiKey;
    }

    public void Init(string source, string message) => Add("INIT", source, message);
    public void Conn(string source, string message) => Add("CONN", source, message);
    public void Recv(string source, string message) => Add("RECV", source, message);
    public void Send(string source, string message) => Add("SEND", source, message);
    public void Info(string source, string message) => Add("INFO", source, message);
    public void Warn(string source, string message) => Add("WARN", source, message);
    public void Error(string source, string message) => Add("ERR",  source, message);

    private void Add(string level, string source, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, source, message);

        Debug.WriteLine($"[{entry.Time:HH:mm:ss.fff}] [{level,-4}] [{source}] {message}");
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                Entries.Add(entry);
            else
                dispatcher.Invoke(() => Entries.Add(entry));
        }
        catch { /* 앱 종료 중 race 방어 */ }
        if (_seqUrl is not null)
            _ = PostToSeqAsync(level, source, message, entry.Time);
    }

    private async Task PostToSeqAsync(string level, string source, string message, DateTime time)
    {
        try
        {
            // CLEF 포맷: @t=타임스탬프, @mt=메시지템플릿, @l=레벨, 나머지=프로퍼티
            var clef = $"{{" +
                       $"\"@t\":\"{time:O}\"," +
                       $"\"@mt\":\"[{{Level}}] [{{Source}}] {{Message}}\"," +
                       $"\"@l\":\"{ToSeqLevel(level)}\"," +
                       $"\"Level\":\"{level}\"," +
                       $"\"Source\":\"{EscapeJson(source)}\"," +
                       $"\"Message\":\"{EscapeJson(message)}\"" +
                       $"}}";

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_seqUrl}/api/events/raw");
            req.Content = new StringContent(clef, Encoding.UTF8, "application/vnd.serilog.clef");

            if (!string.IsNullOrEmpty(_seqApiKey))
                req.Headers.Add("X-Seq-ApiKey", _seqApiKey);

            await _http.SendAsync(req);
        }
        catch
        {
            // Seq가 꺼져 있거나 연결 불가 시 조용히 무시
        }
    }

    private static string ToSeqLevel(string level) => level switch
    {
        "ERR"  => "Error",
        "WARN" => "Warning",
        _      => "Information"
    };

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
