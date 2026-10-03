using FourFoldAccountManager.Core.Calculation;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record ClassComparisonRow(
    string StatLabel,
    string ProfileText,
    string AverageText,
    string DifferenceText,
    string PercentageText,
    ComparisonDirection Direction);
