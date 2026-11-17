using System.Text.Json.Serialization;

namespace OPNX.Lib.Maps.Gis.Models
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(MapMarker), "marker")]
    [JsonDerivedType(typeof(MapPolyline), "polyline")]
    [JsonDerivedType(typeof(MapPolygon), "polygon")]
    [JsonDerivedType(typeof(MapCircle), "circle")]
    public abstract record MapItem
    {
        #region Constructors
        protected MapItem(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            Id = id;
        }
        #endregion

        #region Properties
        /// <summary>Unique within one map view. The application resolves device and linked-map identities.</summary>
        public string Id { get; }
        public bool IsVisible { get; init; } = true;
        public int ZIndex { get; init; }
        public abstract MapCoordinateSystem CoordinateSystem { get; }
        #endregion
    }

    public sealed record MapMarker : MapItem
    {
        #region Constructors
        [JsonConstructor]
        public MapMarker(string id, MapPoint position, MapMarkerStyle? style = null) : base(id)
        {
            ArgumentNullException.ThrowIfNull(position);
            Position = position;
            Style = style ?? new MapMarkerStyle();
        }
        #endregion

        #region Properties
        public MapPoint Position { get; }
        public MapMarkerStyle Style { get; }
        public bool IsDraggable { get; init; }
        public override MapCoordinateSystem CoordinateSystem => Position.CoordinateSystem;
        #endregion
    }

    public sealed record MapPolyline : MapItem
    {
        #region Constructors
        [JsonConstructor]
        public MapPolyline(string id, IReadOnlyList<MapPoint> points, MapShapeStyle? style = null) : base(id)
        {
            Points = MapValidation.Points(points, 2);
            Style = style ?? new MapShapeStyle();
        }
        #endregion

        #region Properties
        public IReadOnlyList<MapPoint> Points { get; }
        public MapShapeStyle Style { get; }
        public override MapCoordinateSystem CoordinateSystem => Points[0].CoordinateSystem;
        #endregion
    }

    public sealed record MapPolygon : MapItem
    {
        #region Constructors
        [JsonConstructor]
        public MapPolygon(string id, IReadOnlyList<MapPoint> points, MapShapeStyle? style = null) : base(id)
        {
            Points = MapValidation.Points(points, 3);
            if (Points.Distinct().Count() < 3)
                throw new ArgumentException("A polygon requires at least three distinct vertices.", nameof(points));
            Style = style ?? new MapShapeStyle();
        }
        #endregion

        #region Properties
        /// <summary>Outer ring. The adapter closes it; repeating the first vertex is optional.</summary>
        public IReadOnlyList<MapPoint> Points { get; }
        public MapShapeStyle Style { get; }
        public override MapCoordinateSystem CoordinateSystem => Points[0].CoordinateSystem;
        #endregion
    }

    public sealed record MapCircle : MapItem
    {
        #region Constructors
        [JsonConstructor]
        public MapCircle(string id, MapPoint center, double radius, MapShapeStyle? style = null) : base(id)
        {
            ArgumentNullException.ThrowIfNull(center);
            MapValidation.Positive(radius, nameof(radius));
            Center = center;
            Radius = radius;
            Style = style ?? new MapShapeStyle();
        }
        #endregion

        #region Properties
        public MapPoint Center { get; }
        /// <summary>Geographic: meters on the ground. Image: original-image pixels.</summary>
        public double Radius { get; }
        public MapShapeStyle Style { get; }
        public override MapCoordinateSystem CoordinateSystem => Center.CoordinateSystem;
        #endregion
    }
}
