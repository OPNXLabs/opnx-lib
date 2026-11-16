namespace OPNX.Lib.Maps.Models
{
    /// <summary>An item's screen position relative to its map view. The adapter reports view-pixel coordinates.</summary>
    public sealed record MapItemAnchor(string ItemId, double X, double Y, bool IsVisible);
}
