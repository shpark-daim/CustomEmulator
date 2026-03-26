using System.Text.Json.Serialization;

namespace Emulator.Protocols;

public abstract record RcpCommand;

public record RcpTaskCommand([property: JsonPropertyOrder(-1)] long RefSeq) : RcpCommand;

public record RcpStatusCommand() : RcpCommand;

public record RcpSyncCommand(long Sequence) : RcpCommand;

public record RcpAutoCommand() : RcpCommand;

public record RcpManualCommand() : RcpCommand;

public record RcpStartCommand(string JobId, string RecipeId, long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpStopCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpPauseCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpResumeCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpAbortCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpEndCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpCompletedCommand() : RcpCommand();