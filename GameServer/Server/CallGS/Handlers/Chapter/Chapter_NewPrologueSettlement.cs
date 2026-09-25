using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_NewPrologueSettlement")]
public class Chapter_NewPrologueSettlement : CallGSHandler<JsonNode>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        var (payload, sync) = await Chapter_DealLevelSettlement.HandleNewPrologueSettlementAsync(context.Player, request);
        return CallGSResult.Ok(payload, sync);
    }
}
