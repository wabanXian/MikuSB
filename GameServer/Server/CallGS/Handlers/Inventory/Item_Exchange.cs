using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

[CallGSApi("Item_Exchange")]
public class Item_Exchange : CallGSHandler<JsonNode>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        var response = new JsonObject
        {
            ["tbReward"] = new JsonArray(),
            ["tbAward"] = new JsonArray()
        };

        return Task.FromResult(CallGSResult.Ok(response));
    }
}
