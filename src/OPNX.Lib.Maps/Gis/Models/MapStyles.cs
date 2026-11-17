using System.Text.Json.Serialization;

namespace OPNX.Lib.Maps.Gis.Models
{
    public enum MapMarkerDisplayMode
    {
        IconOnly,
        IconAndLabel
    }

    public sealed record MapMarkerStyle
    {
        #region Constructors
        [JsonConstructor]
        public MapMarkerStyle(Uri? iconUri = null, double width = 24, double height = 24,
            double rotation = 0, string? label = null)
        {
            MapValidation.Positive(width, nameof(width));
            MapValidation.Positive(height, nameof(height));
            MapValidation.Finite(rotation, nameof(rotation));
            IconUri = iconUri;
            Width = width;
            Height = height;
            Rotation = (rotation % 360 + 360) % 360;
            Label = label;
        }
        #endregion

        #region Properties
        /// <summary>Resolved by the view's asset source; relative URIs are allowed.</summary>
        public Uri? IconUri { get; }
        public double Width { get; }
        public double Height { get; }
        /// <summary>Clockwise degrees from the original icon orientation.</summary>
        public double Rotation { get; }
        /// <summary>Plain text, never executable HTML.</summary>
        public string? Label { get; }
        public MapMarkerDisplayMode DisplayMode { get; init; } = MapMarkerDisplayMode.IconAndLabel;
        public string AccentColor { get; init; } = "#20B4B9";
        public bool IsBlinking { get; init; }
        #endregion
    }

    public sealed record MapShapeStyle
    {
        #region Constructors
        [JsonConstructor]
        public MapShapeStyle(string strokeColor = "#4A6FE3", double strokeWidth = 2, double strokeOpacity = 1,
            string? fillColor = null, double fillOpacity = 0.2)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(strokeColor);
            if (fillColor != null)
                ArgumentException.ThrowIfNullOrWhiteSpace(fillColor);
            MapValidation.Finite(strokeWidth, nameof(strokeWidth));
            ArgumentOutOfRangeException.ThrowIfNegative(strokeWidth);
            MapValidation.Opacity(strokeOpacity, nameof(strokeOpacity));
            MapValidation.Opacity(fillOpacity, nameof(fillOpacity));
            StrokeColor = strokeColor;
            StrokeWidth = strokeWidth;
            StrokeOpacity = strokeOpacity;
            FillColor = fillColor;
            FillOpacity = fillOpacity;
        }
        #endregion

        #region Properties
        /// <summary>CSS color interpreted by the provider adapter.</summary>
        public string StrokeColor { get; }
        /// <summary>Width in view pixels; zero hides the outline.</summary>
        public double StrokeWidth { get; }
        public double StrokeOpacity { get; }
        /// <summary>Null disables filling.</summary>
        public string? FillColor { get; }
        public double FillOpacity { get; }
        #endregion
    }
}
