using System.Drawing;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Describes a prepared weather sprite and its local native bounds.</summary>
internal sealed record WeatherLayer(string Path, RectangleF Bounds);
