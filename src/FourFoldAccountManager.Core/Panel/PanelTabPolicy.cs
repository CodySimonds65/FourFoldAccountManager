using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Panel;

public static class PanelTabPolicy
{
    // Settings files may be old or hand-edited; drop whatever is not a single-account tab instead of
    // rejecting the file. An account keeps its first tab.
    public static IReadOnlyList<PanelTab> Normalize(IEnumerable<PanelTab?>? tabs)
    {
        var seen = new HashSet<Guid>();
        var result = new List<PanelTab>();
        foreach (var tab in tabs ?? [])
        {
            if (tab is { Layout: PanelLayout.OneByOne, AccountId: { } accountId } &&
                accountId != Guid.Empty && seen.Add(accountId))
            {
                result.Add(PanelTab.ForAccount(accountId));
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }

    public static int ClampActive(int activeTab, int tabCount) =>
        tabCount == 0 ? 0 : Math.Clamp(activeTab, 0, tabCount - 1);

    public static Guid? AccountIdAt(PanelSettings settings, int index)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return index >= 0 && index < settings.Tabs.Count ? settings.Tabs[index].AccountId : null;
    }

    public static IReadOnlyList<Guid> AccountIds(PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Tabs.Select(tab => tab.AccountId).OfType<Guid>().ToArray();
    }

    // Adds the account as the last tab and selects it. An account that already has a tab is left alone.
    public static PanelSettings Add(PanelSettings settings, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        if (AccountIds(settings).Contains(accountId))
        {
            return settings;
        }

        var tabs = settings.Tabs.Append(PanelTab.ForAccount(accountId)).ToArray();
        return settings with { Tabs = Array.AsReadOnly(tabs), ActiveTab = tabs.Length - 1 };
    }

    // Closing the active tab keeps its index, which selects the tab to its right, or the last tab.
    public static PanelSettings Close(PanelSettings settings, int index)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (index < 0 || index >= settings.Tabs.Count)
        {
            return settings;
        }

        var tabs = settings.Tabs.Where((_, position) => position != index).ToArray();
        var active = index < settings.ActiveTab ? settings.ActiveTab - 1 : settings.ActiveTab;
        return settings with { Tabs = Array.AsReadOnly(tabs), ActiveTab = ClampActive(active, tabs.Length) };
    }

    public static PanelSettings Select(PanelSettings settings, int index)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with { ActiveTab = ClampActive(index, settings.Tabs.Count) };
    }

    // direction is +1 for the next tab and -1 for the previous one. Both wrap around.
    public static PanelSettings Step(PanelSettings settings, int direction)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var count = settings.Tabs.Count;
        return count == 0
            ? settings
            : settings with { ActiveTab = ((settings.ActiveTab + direction) % count + count) % count };
    }

    public static PanelSettings RemoveAccount(PanelSettings settings, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        for (var index = 0; index < settings.Tabs.Count; index++)
        {
            if (settings.Tabs[index].AccountId == accountId)
            {
                return Close(settings, index);
            }
        }

        return settings;
    }
}
