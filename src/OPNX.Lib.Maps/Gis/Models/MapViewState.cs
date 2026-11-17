using System.Text.Json.Serialization;

namespace OPNX.Lib.Maps.Gis.Models
{
    public sealed record MapViewState
    {
        #region Constructors
        [JsonConstructor]
        public MapViewState(MapPoint center, double zoom)
        {
            ArgumentNullException.ThrowIfNull(center);
            MapValidation.Finite(zoom, nameof(zoom));
            Center = center;
            Zoom = zoom;
        }
        #endregion

        #region Properties
        public MapPoint Center { get; }

        /// <summary>
        /// Increasing zoom by one doubles the scale. Geographic zoom zero uses a
        /// 256-pixel-wide world; image zoom zero displays one image pixel per view pixel.
        /// Provider adapters translate this value to their own zoom levels and limits.
        /// </summary>
        public double Zoom { get; }
        #endregion
    }
}
