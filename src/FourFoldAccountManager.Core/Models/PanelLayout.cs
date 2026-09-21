namespace FourFoldAccountManager.Core.Models;

public enum PanelLayout
{
    OneByTwo,
    TwoByOne,
    TwoByTwo,
    TwoByThree,
    OneByTwoVertical
}

public readonly record struct GridDimensions(int Rows, int Columns);

public readonly record struct PanelSlotPlacement(
    int Row,
    int Column,
    int RowSpan = 1,
    int ColumnSpan = 1);
