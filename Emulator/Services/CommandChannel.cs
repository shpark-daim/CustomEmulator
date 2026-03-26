using System.Threading.Channels;

namespace Emulator.Services;

public sealed class CommandChannel : IAsyncDisposable
{
    private readonly Channel<Func<Task>> _channel =
        Channel.CreateUnbounded<Func<Task>>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly Task _loop;

    public CommandChannel()
    {
        _loop = ProcessLoopAsync();
    }

    public void Post(Func<Task> action) => _channel.Writer.TryWrite(action);
    public void Complete() => _channel.Writer.TryComplete();

    private async Task ProcessLoopAsync()
    {
        await foreach (var action in _channel.Reader.ReadAllAsync())
        {
            try   { await action(); }
            catch { /* 개별 명령 오류 — 무시하고 다음 항목 처리 */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try { await _loop; } catch { }
    }
}