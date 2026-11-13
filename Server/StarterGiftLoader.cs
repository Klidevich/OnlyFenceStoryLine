using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace CustomQuests.E17.Server;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class StarterGiftLoader(TemplateTable templates, TimeUtil time) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // SPT 4.1.6 CreateProfileService deep-clones the selected TemplateSide,
        // then assigns its Dialogues BEFORE its first Save/Load. The letter is
        // part of creation itself: there is no login/load/save issuance hook,
        // and no access to existing profiles or their starting inventories.
        // Repeating this loader is idempotent; retrying a save cannot append mail.
        StarterGift.AddToTemplates(templates.Profiles.Values, time.GetTimeStamp());
        return Task.CompletedTask;
    }
}
