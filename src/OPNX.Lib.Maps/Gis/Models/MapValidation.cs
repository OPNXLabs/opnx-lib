namespace OPNX.Lib.Maps.Gis.Models
{
    internal static class MapValidation
    {
        public static void Finite(double value, string parameterName)
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(parameterName, "The value must be finite.");
        }

        public static void Positive(double value, string parameterName)
        {
            Finite(value, parameterName);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
        }

        public static void Opacity(double value, string parameterName)
        {
            Finite(value, parameterName);
            if (value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(parameterName, "Opacity must be between zero and one.");
        }

        public static IReadOnlyList<MapPoint> Points(IReadOnlyList<MapPoint> points, int minimumCount)
        {
            ArgumentNullException.ThrowIfNull(points);
            var snapshot = points.ToArray();
            if (snapshot.Length < minimumCount)
                throw new ArgumentException($"At least {minimumCount} points are required.", nameof(points));
            if (snapshot.Any(point => point == null))
                throw new ArgumentException("Points cannot contain null entries.", nameof(points));
            if (snapshot.Any(point => point.CoordinateSystem != snapshot[0].CoordinateSystem))
                throw new ArgumentException("Points must use the same coordinate system.", nameof(points));
            return Array.AsReadOnly(snapshot);
        }
    }
}
