using System.Text.Json.Serialization;

namespace OPNX.Lib.Maps.Models
{
    public enum MapCoordinateSystem
    {
        Geographic,
        Image
    }

    /// <summary>
    /// Geographic: X is WGS84 longitude, Y is latitude, in degrees.
    /// Image: X increases rightward and Y downward, in original-image pixels.
    /// </summary>
    public sealed record MapPoint
    {
        #region Constructors
        [JsonConstructor]
        public MapPoint(MapCoordinateSystem coordinateSystem, double x, double y)
        {
            if (!Enum.IsDefined(coordinateSystem))
                throw new ArgumentOutOfRangeException(nameof(coordinateSystem));
            MapValidation.Finite(x, nameof(x));
            MapValidation.Finite(y, nameof(y));
            if (coordinateSystem == MapCoordinateSystem.Geographic)
            {
                if (x is < -180 or > 180)
                    throw new ArgumentOutOfRangeException(nameof(x));
                if (y is < -90 or > 90)
                    throw new ArgumentOutOfRangeException(nameof(y));
            }
            CoordinateSystem = coordinateSystem;
            X = x;
            Y = y;
        }
        #endregion

        #region Properties
        public MapCoordinateSystem CoordinateSystem { get; }
        public double X { get; }
        public double Y { get; }
        #endregion

        #region Public Static Methods
        public static MapPoint FromGeographic(double latitude, double longitude) =>
            new(MapCoordinateSystem.Geographic, longitude, latitude);

        public static MapPoint FromImage(double x, double y) => new(MapCoordinateSystem.Image, x, y);
        #endregion
    }
}
