namespace FourFoldAccountManager.Core.Models;

public enum PanelLayout
{
    OneByTwo,
    TwoByOne,
    TwoByTwo,
    TwoByThree,
    OneByTwoVertical,
    OneByOne,
    OneByThree,
    // One client per tab, taken from PanelSettings.Tabs instead of the grid slots.
    Tabs
}

public readonly record struct PanelSlotPlacement(
    int Row,
    int Column,
    int RowSpan = 1,
    int ColumnSpan = 1);
