namespace FourFoldAccountManager.Core.Models;

public static class AccountProfileRules
{
    public const int MaximumLabelLength = 60;

    public static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var normalized = label.Trim();
        if (normalized.Length > MaximumLabelLength)
        {
            throw new ArgumentException(
                $"Labels may contain at most {MaximumLabelLength} characters.",
                nameof(label));
        }

        return normalized;
    }
}
