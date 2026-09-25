namespace FourFoldAccountManager.Desktop.Views;

// Adding a new overlay add-on only needs a data record implementing this, a template keyed to its
// type, and one OverlayAddOnCatalog entry — no switch statement to extend.
public interface IOverlayCardData
{
    string Summary { get; }
}
