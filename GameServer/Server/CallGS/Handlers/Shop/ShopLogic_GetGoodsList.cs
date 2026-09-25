using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("ShopLogic_GetGoodsList")]
public class ShopLogic_GetGoodsList : CallGSHandler<JsonNode>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, JsonNode request)
    {
        var response = new JsonObject
        {
            ["tbGoodsList"] = new JsonArray(),
            ["tbList"] = new JsonArray()
        };

        return Task.FromResult(CallGSResult.Ok(response));
    }
}
