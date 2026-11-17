using OPNX.Lib.Maps.Gis.Models;

namespace OPNX.Lib.Maps.Gis.Events
{
    public enum MapInteractionKind
    {
        Click,
        DoubleClick,
        ContextMenu,
        ItemMoved
    }

    public sealed class MapInteractionEventArgs : EventArgs
    {
        #region Constructors
        public MapInteractionEventArgs(MapInteractionKind kind, MapPoint location, string? itemId = null)
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ArgumentNullException.ThrowIfNull(location);
            if (itemId != null)
                ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
            if (kind == MapInteractionKind.ItemMoved && itemId == null)
                throw new ArgumentException("An item move must identify the item.", nameof(itemId));
            Kind = kind;
            Location = location;
            ItemId = itemId;
        }
        #endregion

        #region Properties
        public MapInteractionKind Kind { get; }
        public MapPoint Location { get; }
        /// <summary>Null when the background map was targeted. ItemMoved reports the final position after dragging.</summary>
        public string? ItemId { get; }
        #endregion
    }
}
