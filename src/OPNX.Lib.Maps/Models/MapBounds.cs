using System.Text.Json.Serialization;

namespace OPNX.Lib.Maps.Models
{
    public sealed record MapBounds
    {
        #region Constructors
        [JsonConstructor]
        public MapBounds(MapPoint topLeft, MapPoint bottomRight)
        {
            ArgumentNullException.ThrowIfNull(topLeft);
            ArgumentNullException.ThrowIfNull(bottomRight);
            if (topLeft.CoordinateSystem != bottomRight.CoordinateSystem)
                throw new ArgumentException("Bounds must use the same coordinate system.", nameof(bottomRight));
            if (topLeft.CoordinateSystem == MapCoordinateSystem.Geographic ? topLeft.Y < bottomRight.Y :
                topLeft.Y > bottomRight.Y || topLeft.X > bottomRight.X)
                throw new ArgumentException("Invalid top-left and bottom-right ordering.", nameof(bottomRight));
            TopLeft = topLeft;
            BottomRight = bottomRight;
        }
        #endregion

        #region Properties
        public MapPoint TopLeft { get; }
        public MapPoint BottomRight { get; }
        public MapCoordinateSystem CoordinateSystem => TopLeft.CoordinateSystem;
        public bool CrossesAntimeridian => CoordinateSystem == MapCoordinateSystem.Geographic && TopLeft.X > BottomRight.X;
        #endregion

        #region Public Methods
        public bool Contains(MapPoint point)
        {
            ArgumentNullException.ThrowIfNull(point);
            if (point.CoordinateSystem != CoordinateSystem)
                return false;
            bool containsX = CrossesAntimeridian
                ? point.X >= TopLeft.X || point.X <= BottomRight.X
                : point.X >= TopLeft.X && point.X <= BottomRight.X;
            return containsX && point.Y >= Math.Min(TopLeft.Y, BottomRight.Y) && point.Y <= Math.Max(TopLeft.Y, BottomRight.Y);
        }
        #endregion
    }
}
