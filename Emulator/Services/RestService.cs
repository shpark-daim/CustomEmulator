using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Emulator.Services;
public sealed class RestService : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly HttpClient   _outbound = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<string, Func<string, Task<string?>>> _handlers    = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<Task<string?>>>          _getHandlers = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int  Port      { get; }
    public bool IsRunning { get; private set; }

    public RestService(int port)
    {
        Port = port;
        // localhost 바인딩은 관리자 권한 불필요
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public Task StartAsync()
    {
        if (IsRunning) return Task.CompletedTask;
        _listener.Start();
        IsRunning = true;
        _cts  = new CancellationTokenSource();
        _loop = ListenLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        IsRunning = false;
        if (_loop is not null)
            try { await _loop; } catch { }
    }

    /// <param name="path">등록할 경로 (예: /robot/1)</param>
    /// <param name="handler">POST 수신 시 호출할 콜백 (payload = JSON body). 반환값은 HTTP 응답 body로 사용됩니다.</param>
    public void RegisterPath(string path, Func<string, Task<string?>> handler)
        => _handlers[Normalize(path)] = handler;

    /// <param name="path">등록할 경로 (예: /robot/1/status)</param>
    /// <param name="handler">GET 수신 시 호출할 콜백. 반환값은 HTTP 응답 body로 사용됩니다.</param>
    public void RegisterGetPath(string path, Func<Task<string?>> handler)
        => _getHandlers[Normalize(path)] = handler;

    public void UnregisterPath(string path)
    {
        var key = Normalize(path);
        _handlers.Remove(key);
        _getHandlers.Remove(key);
    }

    public async Task PostAsync(string url, string jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            await _outbound.PostAsync(url, content);
        }
        catch { /* 네트워크 오류 무시 */ }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // WaitAsync: 취소 시 OperationCanceledException 발생 (.NET 6+)
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = HandleAsync(ctx);   // 각 요청은 별도 Task로 처리
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (!_listener.IsListening) { break; }
            catch { /* 일시적 오류 무시 */ }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path   = Normalize(ctx.Request.Url?.AbsolutePath ?? "/");
            var method = ctx.Request.HttpMethod;
            string? responseJson = null;
            bool found = false;

            if (method == "POST" && _handlers.TryGetValue(path, out var postHandler))
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                try { responseJson = await postHandler(body); } catch { }
                found = true;
            }
            else if (method == "GET" && _getHandlers.TryGetValue(path, out var getHandler))
            {
                try { responseJson = await getHandler(); } catch { }
                found = true;
            }

            if (found)
            {
                ctx.Response.StatusCode = 200;
                var bytes = Encoding.UTF8.GetBytes(responseJson ?? "{\"ok\":true}");
                ctx.Response.ContentType     = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            else
            {
                ctx.Response.StatusCode = method is "POST" or "GET" ? 404 : 405;
            }
        }
        catch { ctx.Response.StatusCode = 500; }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    private static string Normalize(string p) => "/" + p.Trim('/');

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _outbound.Dispose();
        _listener.Close();
    }
}
