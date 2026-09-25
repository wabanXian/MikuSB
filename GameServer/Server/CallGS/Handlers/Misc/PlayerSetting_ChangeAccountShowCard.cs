using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.Proto;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_ChangeAccountShowCard")]
public class PlayerSetting_ChangeAccountShowCard : CallGSHandler<ChangeAccountShowCardParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChangeAccountShowCardParam req)
    {
        var player = context.Connection.Player!;
        if (req == null || req.Index <= 0 || req.Index > 5)
            return Task.FromResult(CallGSResult.NoResponse());

        var card = player.CharacterManager.GetCharacterByGUID(req.ItemId);
        if (card == null)
            return Task.FromResult(CallGSResult.Ok("{}"));

        var slot = req.Index <= 3
            ? (int)ProfileShowItemTypeEnum.SHOWITEM_CARD1 + req.Index - 1
            : (int)ProfileShowItemTypeEnum.SHOWITEM_CARD4 + req.Index - 4;

        player.SetShowItem(slot, card.Guid);
        DatabaseHelper.SaveDatabaseType(player.Data);

        var sync = new NtfSyncPlayer();
        sync.ShowItems.AddRange(player.Data.ShowItems);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class ChangeAccountShowCardParam
{
    [JsonPropertyName("nIndex")]
    public int Index { get; set; }

    [JsonPropertyName("nItemID")]
    public uint ItemId { get; set; }
}
