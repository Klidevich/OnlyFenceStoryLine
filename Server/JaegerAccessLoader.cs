using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace CustomQuests.E17.Server;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class JaegerAccessLoader(TemplateTable templates) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Native ResetTrader uses this flag for Jaeger after unlockedByDefault.
        // Updating templates affects future USEC/BEAR profiles, not their loadouts.
        JaegerAccess.OpenInTemplates(templates.Profiles.Values);
        return Task.CompletedTask;
    }
}
