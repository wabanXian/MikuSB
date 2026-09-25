using MikuSB.Util;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Globalization;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("DLCLogic_CheckOpenAct")]
public class DLCLogic_CheckOpenAct : CallGSHandler
{
    private const uint GroupId = AttrIds.Dlc.Gid;
    private const uint ActIdSid = AttrIds.Dlc.ActIdSid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        if (UseProgressionMode())
            return Task.FromResult(CallGSResult.Ok("{\"bOpen\":false}"));

        var now = DateTime.Now;
        var act = ResolveCurrent(GameData.DlcActivityData.Values, now);
        if (act == null)
        {
            return Task.FromResult(CallGSResult.Ok("{\"bOpen\":false}"));
        }

        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        SetAttr(player, ActIdSid, act.Id, sync);

        var response = new JsonObject
        {
            ["bOpen"] = true,
            ["nId"] = act.Id,
            ["nStartTime"] = ToUnixSeconds(ParseConfigTime(act.EnterStartTime)),
            ["nEndTime"] = ToUnixSeconds(ParseConfigTime(act.CloseEndTime))
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }


    private static bool UseProgressionMode() =>
        string.Equals(ConfigManager.Config.ServerOption.GameMode, "Progression", StringComparison.OrdinalIgnoreCase);

    private static DlcActivityExcel? ResolveCurrent(IEnumerable<DlcActivityExcel> configs, DateTime now)
    {
        var parsed = configs
            .Select(x => new
            {
                Config = x,
                Start = ParseConfigTime(x.EnterStartTime),
                End = ParseConfigTime(x.CloseEndTime)
            })
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .OrderBy(x => x.Start)
            .ToList();

        var current = parsed.FirstOrDefault(x => x.Start <= now && now < x.End);
        if (current != null)
            return current.Config;

        var latestStarted = parsed.LastOrDefault(x => x.Start <= now && x.End > x.Start);
        return latestStarted?.Config;
    }

    private static DateTime? ParseConfigTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().Trim('[', ']');
        if (normalized.Length != 12)
            return null;

        return DateTime.TryParseExact(
            normalized,
            "yyyyMMddHHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }

    private static long ToUnixSeconds(DateTime? value)
    {
        return value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : 0L;
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val != value)
        {
            attr.Val = value;
            player.Attributes.SyncTo(sync, attr);
        }
    }
}

