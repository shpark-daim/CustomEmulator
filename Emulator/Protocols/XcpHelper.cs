using Shared.Models;

namespace Emulator.Protocols;

public static class XcpHelper
{
    public static XcpCommandType? ParseCommandType(string topic)
    {
        var tokens = topic.Split('/');
        if (tokens.Length < 6 || tokens[4] != "cmd") return null;
        return Enum.TryParse<XcpCommandType>(tokens[5], ignoreCase: true, out var t) ? t : null;
    }

    public static string StatusTopic(XcpConfig xcp, string unitId)
    {
        var target = string.IsNullOrEmpty(xcp.Target) ? unitId : xcp.Target;
        return $"{xcp.Prefix}/{xcp.Identifier}/{xcp.Version}/{target}/{Rcp.TypeStatus}";
    }

    public static IEnumerable<string> CmdTopics(XcpConfig xcp, string unitId)
    {
        var target    = string.IsNullOrEmpty(xcp.Target) ? unitId : xcp.Target;
        var baseTopic = $"{xcp.Prefix}/{xcp.Identifier}/{xcp.Version}/{target}/cmd";
        var fixedCmds = new[] { "status", "sync", "auto", "manual" };
        var optional  = xcp.OptionalCommands ?? Enumerable.Empty<string>();
        return fixedCmds.Concat(optional).Distinct().Select(cmd => $"{baseTopic}/{cmd}");
    }
}
