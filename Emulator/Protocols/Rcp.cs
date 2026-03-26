using System.Net.NetworkInformation;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emulator.Protocols;

public class Rcp
{
    public const string PrefixDefault = "xflow";
    public const string Identifier = "rcp";
    public const string Version = "v1";
    public const string BroadcastId = "*";
    public const string TypeStatus = "status";
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RcpStatus))]
[JsonSerializable(typeof(RcpAutoCommand))]
[JsonSerializable(typeof(RcpStartCommand))]
[JsonSerializable(typeof(RcpStatusCommand))]
[JsonSerializable(typeof(RcpSyncCommand))]
[JsonSerializable(typeof(RcpManualCommand))]
[JsonSerializable(typeof(RcpStopCommand))]
[JsonSerializable(typeof(RcpPauseCommand))]
[JsonSerializable(typeof(RcpResumeCommand))]
[JsonSerializable(typeof(RcpAbortCommand))]
[JsonSerializable(typeof(RcpEndCommand))]
public partial class RcpContext : JsonSerializerContext
{
    static RcpContext()
    {
        OptionsWithRelaxedEscaping = new(Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
    public static JsonSerializerOptions OptionsWithRelaxedEscaping { get; }
}

internal static class RcpDefaults
{
    public static IReadOnlyList<string> ModeValues { get; } = Enum.GetNames<RcpMode>();
    public static IReadOnlyList<string> StateValues { get; } = Enum.GetNames<RcpWorkingState>();
    public const string ModeDefault = nameof(RcpMode.M);
    public const string StateDefault = nameof(RcpWorkingState.I);
}

public sealed class RcpHandlerContext
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyDictionary<string, object?> Properties { get; init; }

    internal Func<string, object?, bool>? SetPropertyCallback;
    internal Func<string, Task>? PublishCallback;
    internal Action<double>? SetProgressCallback;
    internal Action<string, long>? LogCallback;
    internal Action<Func<Task>>? EnqueueCallback;
    internal Func<string, object?>? GetCurrentCallback;

    public object? Get(string key) => Properties.TryGetValue(key, out var v) ? v : null;
    /// <summary>스냅샷이 아닌 현재 PropertyViewModel의 실시간 값을 읽습니다.</summary>
    public object? GetCurrent(string key) => GetCurrentCallback?.Invoke(key);
    public void Set(string key, object? value) => SetPropertyCallback?.Invoke(key, value);
    public Task PublishAsync(string json) => PublishCallback?.Invoke(json) ?? Task.CompletedTask;
    public void SetProgress(double value) => SetProgressCallback?.Invoke(Math.Clamp(value, 0, 100));
    public void LogSeq(string cmd, long seq) => LogCallback?.Invoke(cmd, seq);
    public void Enqueue(Func<Task> work) => EnqueueCallback?.Invoke(work);
}

public class RobotCommandHandler
{
    private RcpStatus _status;
    private readonly List<int> _errorCodes = new();
    private int _lastProgress = 0;

    public RobotCommandHandler(string unitId = "")
    {
        _status = new RcpStatus(unitId, 0, 0, RcpMode.M, RcpWorkingState.I, []);
    }

    // ── 각 커맨드 핸들러 (public — ObjectViewModel에서 직접 호출) ──────────────

    public async Task HandleSyncAsync(RcpSyncCommand cmd, RcpHandlerContext ctx)
    {
        // sequence가 더 높을 때만 업데이트, 항상 현재 상태 응답
        if (cmd.Sequence > _status.Sequence)
            _status = _status with { Sequence = cmd.Sequence, EventSeq = cmd.Sequence };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("sync", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    public async Task HandleStatusAsync(RcpHandlerContext ctx)
    {
        _status = _status with { Sequence = _status.Sequence + 1 };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("status", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    public async Task HandleAutoAsync(RcpHandlerContext ctx)
    {
        if (_status.Mode == RcpMode.A) return;
        var nextSeq = _status.Sequence + 1;
        _status = _status with { Mode = RcpMode.A, Sequence = nextSeq, EventSeq = nextSeq };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("auto", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    public async Task HandleManualAsync(RcpHandlerContext ctx)
    {
        if (_status.Mode == RcpMode.M) return;
        var nextSeq = _status.Sequence + 1;
        _status = _status with { Mode = RcpMode.M, Sequence = nextSeq, EventSeq = nextSeq };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("manual", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    public async Task HandleStartAsync(RcpStartCommand cmd, RcpHandlerContext ctx)
    {
        if (_status.Mode != RcpMode.A) return;
        if (cmd.RefSeq != 0 && cmd.RefSeq != _status.EventSeq) return;
        if (_status.JobId != null) return;
        if (_status.WorkingState != RcpWorkingState.I) return;

        _lastProgress = 0;
        var nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.R,
            JobId = cmd.JobId,
            RecipeId = cmd.RecipeId,
            CompletionReason = null,
            ProductResult = null,
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        ctx.SetProgress(0);
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("start", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());

        // progress 루프 백그라운드 실행 — 채널이 즉시 다음 명령(stop/pause/abort) 처리 가능
        _ = Task.Run(() => RunProgressLoopAsync(ctx));
    }

    public async Task HandleStopAsync(RcpStopCommand cmd, RcpHandlerContext ctx)
    {
        if (_status.Mode != RcpMode.A) return;
        if (cmd.RefSeq != 0 && cmd.RefSeq != _status.EventSeq) return;

        var nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.S,
            CompletionReason = null,
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("stop", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
        // background loop이 S를 감지하고 HandleCompletedInternalAsync(reason="Stopped")를 enqueue
    }

    public async Task HandlePauseAsync(RcpPauseCommand cmd, RcpHandlerContext ctx)
    {
        if (_status.Mode != RcpMode.A) return;
        if (_status.WorkingState != RcpWorkingState.R) return;
        if (cmd.RefSeq != 0 && cmd.RefSeq != _status.EventSeq) return;

        // Phase 1: P — loop이 이걸 감지하고 break (progress bar는 현재 위치 유지)
        var nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.P,
            CompletionReason = null,
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("pause", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());

        await Task.Delay(1000);

        // Phase 2: C/"Paused"
        nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.C,
            CompletionReason = "Paused",
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("pause-complete", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    public async Task HandleResumeAsync(RcpResumeCommand cmd, RcpHandlerContext ctx)
    {
        if (_status.WorkingState != RcpWorkingState.C || _status.CompletionReason != "Paused") return;
        if (cmd.RefSeq != 0 && cmd.RefSeq != _status.EventSeq) return;

        var nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.R,
            CompletionReason = null,
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("resume", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());

        // _lastProgress 위치부터 이어서 진행
        _ = Task.Run(() => RunProgressLoopAsync(ctx));
    }

    public async Task HandleAbortAsync(RcpAbortCommand cmd, RcpHandlerContext ctx)
    {
        if (cmd.RefSeq != 0 && cmd.RefSeq != _status.EventSeq) return;

        var nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.A,
            CompletionReason = null,
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("abort", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());

        await Task.Delay(1000);

        nextSeq = _status.Sequence + 1;
        var abortProductResultOk = ctx.GetCurrent("ProductResultOk") is bool ab && ab;
        _status = _status with
        {
            WorkingState = RcpWorkingState.C,
            CompletionReason = "Aborted",
            ProductResult = abortProductResultOk ? "Ok" : "Ng",
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("abort-complete", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    public async Task HandleEndAsync(RcpEndCommand cmd, RcpHandlerContext ctx)
    {
        if (cmd.RefSeq != 0 && cmd.RefSeq != _status.EventSeq) return;
        if (_status.WorkingState != RcpWorkingState.C) return;

        var nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.I,
            CompletionReason = null,
            JobId = null,
            RecipeId = null,
            ProductResult = null,
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        ctx.SetProgress(0);
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("end", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    public async Task HandleBoolChangedAsync(string key, bool newValue, RcpHandlerContext ctx)
    {
        await Task.CompletedTask;
    }

    private async Task RunProgressLoopAsync(RcpHandlerContext ctx)
    {
        const int stepDelayMs = 50;

        while (true)
        {
            // R이 아니면 즉시 종료 (P/S/A/C 등)
            if (_status.WorkingState != RcpWorkingState.R) break;

            _lastProgress++;
            ctx.SetProgress(_lastProgress);
            await Task.Delay(stepDelayMs);

            if (_lastProgress >= 99)
            {
                bool infinite = ctx.GetCurrent("InfiniteRun") is bool b && b;
                if (!infinite) break;
                _lastProgress = 0;
                ctx.SetProgress(0);
            }
        }

        // R(자연완료) 또는 S(Stop)일 때만 Complete 처리
        if (_status.WorkingState is RcpWorkingState.R or RcpWorkingState.S)
            ctx.Enqueue(() => HandleCompletedInternalAsync(ctx));
    }

    /// <summary>UI의 Complete 버튼 클릭 — R 상태에서 즉시 완료 처리</summary>
    public Task HandleCompleteButtonAsync(RcpHandlerContext ctx)
    {
        if (_status.WorkingState != RcpWorkingState.R) return Task.CompletedTask;
        return HandleCompletedInternalAsync(ctx);
    }

    private async Task HandleCompletedInternalAsync(RcpHandlerContext ctx)
    {
        var reason = _status.WorkingState == RcpWorkingState.S ? "Stopped" : "Done";
        var productResultOk = ctx.GetCurrent("ProductResultOk") is bool b && b;
        var nextSeq = _status.Sequence + 1;
        _status = _status with
        {
            WorkingState = RcpWorkingState.C,
            CompletionReason = reason,
            ProductResult = reason == "Done" && productResultOk ? "Ok" : "Ng",
            Sequence = nextSeq,
            EventSeq = nextSeq,
        };
        if (reason == "Done") ctx.SetProgress(100);
        SyncPropertiesToCtx(ctx);
        ctx.LogSeq("complete", _status.Sequence);
        await ctx.PublishAsync(BuildStatusJson());
    }

    private void SyncPropertiesToCtx(RcpHandlerContext ctx)
    {
        ctx.Set("Seq", (int)_status.Sequence);
        ctx.Set("EventSeq", (int)_status.EventSeq);
        ctx.Set("State", _status.WorkingState.ToString());
        ctx.Set("Mode", _status.Mode.ToString());
        ctx.Set("ErrorCodes", _errorCodes.ToArray());
        ctx.Set("JobId", _status.JobId);
        ctx.Set("RecipeId", _status.RecipeId);
        ctx.Set("CompleteReason", _status.CompletionReason);

    }

    private string BuildStatusJson()
        => JsonSerializer.Serialize(
            _status with { ErrorCodes = _errorCodes },
            RcpContext.OptionsWithRelaxedEscaping);
}

