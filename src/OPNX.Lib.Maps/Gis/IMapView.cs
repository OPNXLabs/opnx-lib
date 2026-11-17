using OPNX.Lib.Maps.Gis.Events;
using OPNX.Lib.Maps.Gis.Models;

namespace OPNX.Lib.Maps.Gis
{
    /// <summary>
    /// One independent map view, implemented by a UI/provider adapter.
    /// Provider credentials, browser setup and proxy selection belong to that adapter's composition.
    /// </summary>
    public interface IMapView : IAsyncDisposable
    {
        #region Properties
        MapCoordinateSystem CoordinateSystem { get; }
        bool IsReady { get; }
        MapViewState? ViewState { get; }
        #endregion

        #region Events
        event EventHandler? ReadyChanged;
        event EventHandler<MapViewState>? ViewChanged;
        event EventHandler<MapInteractionEventArgs>? Interaction;
        event EventHandler<Exception>? Failed;
        event EventHandler<MapItemAnchor>? ItemAnchorChanged;
        #endregion

        #region Methods
        /// <summary>Completes after the provider is ready and the initial view has been applied.</summary>
        Task InitializeAsync(MapViewState initialView, CancellationToken cancellationToken = default);

        Task SetViewAsync(MapViewState view, CancellationToken cancellationToken = default);

        /// <summary>Fits the bounds with nonnegative padding measured in view pixels.</summary>
        Task FitBoundsAsync(MapBounds bounds, int padding = 24, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or replaces only the supplied item IDs; other items remain unchanged.
        /// Reject duplicate IDs and coordinate systems different from this view before applying the batch.
        /// A provider failure or cancellation can leave a partially applied batch; retrying the same IDs is safe.
        /// </summary>
        Task UpsertItemsAsync(IReadOnlyList<MapItem> items, CancellationToken cancellationToken = default);

        /// <summary>Removes the supplied IDs; missing IDs are ignored.</summary>
        Task RemoveItemsAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default);

        Task ClearItemsAsync(CancellationToken cancellationToken = default);

        /// <summary>Tracks one item's position in view pixels. Null stops tracking.</summary>
        Task TrackItemAsync(string? itemId, CancellationToken cancellationToken = default);

        /// <summary>Ends the current map pointer gesture before the host begins drag/drop.</summary>
        void CancelPointerInteraction();

        /// <summary>Enables or disables the attention animation of a non-draggable marker.</summary>
        Task SetItemBlinkingAsync(string itemId, bool isBlinking, CancellationToken cancellationToken = default);
        #endregion
    }
}
