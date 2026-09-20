namespace FourFoldAccountManager.Core.Models;

public sealed record AccountProfile(Guid Id, string Label, bool IsFavorite, int SortOrder)
{
    public static AccountProfile Create(string label, int sortOrder = 0) =>
        new(Guid.NewGuid(), AccountProfileRules.NormalizeLabel(label), false, sortOrder);
}
