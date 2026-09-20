namespace FourFoldAccountManager.Core.Models;

public enum PanelLayout
{
    OneByTwo,
    TwoByOne,
    TwoByTwo
}

public readonly record struct GridDimensions(int Rows, int Columns);
