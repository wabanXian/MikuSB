namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("FavMailLog")]
public class FavMailLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
