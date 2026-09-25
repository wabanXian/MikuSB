namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_LevelFail")]
public class Chapter_LevelFail : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.Ok("{}"));
    }
}
