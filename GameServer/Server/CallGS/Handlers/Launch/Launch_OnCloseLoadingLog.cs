namespace MikuSB.GameServer.Server.CallGS.Handlers.Launch;

[CallGSApi("Launch_OnCloseLoadingLog")]
public class Launch_OnCloseLoadingLog : CallGSHandler
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        return Task.FromResult(CallGSResult.NoResponse());
    }
}
