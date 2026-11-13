#nullable enable
namespace CustomQuests.E17;

// Uses unscaled time. Each live lobby session must actually reach the main menu first.
internal sealed class MenuStartDelay
{
    object? session;
    float? enteredAt;
    float retryAt;

    internal bool ShouldStart(object current, bool lobby, bool menuVisible, bool available, float now)
    {
        if (!lobby || !ReferenceEquals(session, current))
        {
            session = lobby ? current : null;
            enteredAt = null;
            retryAt = 0;
        }
        if (!lobby) return false;
        if (menuVisible && !enteredAt.HasValue) enteredAt = now;
        if (!available || !enteredAt.HasValue || now - enteredAt.Value < 5f || now < retryAt) return false;
        retryAt = now + 5f; // Native QuestOps deduplicates in-flight requests; failed starts may retry.
        return true;
    }
}
