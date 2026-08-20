using Microsoft.AspNetCore.Components;

namespace PanoramicData.Maps.Blazor;

/// <summary>
/// Renders a static map from a PanoramicData.Maps service as a plain <c>&lt;img&gt;</c>.
/// <para>
/// There is no JavaScript, and therefore none of the interop-lifetime and disposal hazards that come
/// with a scripted map. There is also, deliberately, <b>no API key parameter</b>: a key placed in an
/// image URL is visible to every browser that loads the page, including under Blazor Server where it
/// lands in the rendered HTML. Point <see cref="BaseUrl"/> at a same-origin endpoint in the host
/// application that adds the key server-side, or at a service that authenticates some other way. The
/// component cannot leak a key because it cannot send one.
/// </para>
/// </summary>
public partial class StaticMap : ComponentBase
{
	/// <summary>
	/// Base URL of the maps service, or of a same-origin proxy in front of it - for example
	/// <c>https://maps.panoramicdata.com</c> or <c>/api/maps</c>.
	/// </summary>
	[Parameter]
	[EditorRequired]
	public string? BaseUrl { get; set; }

	/// <summary>Explicit map centre. Takes precedence over <see cref="Location"/>.</summary>
	[Parameter]
	public GeoPoint? Center { get; set; }

	/// <summary>A place name to be geocoded by the service, used when <see cref="Center"/> is not set.</summary>
	[Parameter]
	public string? Location { get; set; }

	/// <summary>Zoom level. Omit to let the service fit the view to the markers.</summary>
	[Parameter]
	public double? Zoom { get; set; }

	/// <summary>Image width in CSS pixels.</summary>
	[Parameter]
	public int Width { get; set; } = 800;

	/// <summary>Image height in CSS pixels.</summary>
	[Parameter]
	public int Height { get; set; } = 600;

	/// <summary>Device scale factor: 2 requests an @2x image, which still occupies its CSS size.</summary>
	[Parameter]
	public int Scale { get; set; } = 1;

	/// <summary>Output format.</summary>
	[Parameter]
	public MapImageFormat Format { get; set; } = MapImageFormat.Png;

	/// <summary>Markers to draw.</summary>
	[Parameter]
	public IReadOnlyList<MarkerSpec> Markers { get; set; } = [];

	/// <summary>Polyline overlays to draw.</summary>
	[Parameter]
	public IReadOnlyList<PathSpec> Paths { get; set; } = [];

	/// <summary>Filled polygon overlays to draw.</summary>
	[Parameter]
	public IReadOnlyList<PolygonSpec> Polygons { get; set; } = [];

	/// <summary>Named regions (countries) to shade, resolved server-side from their codes.</summary>
	[Parameter]
	public IReadOnlyList<RegionSpec> Regions { get; set; } = [];

	/// <summary>
	/// Alternative text. Defaults to a description built from the location or centre, because a map
	/// carrying customer information should not be announced as "image".
	/// </summary>
	[Parameter]
	public string? Alt { get; set; }

	/// <summary>Whether to mark the image <c>loading="lazy"</c>. On by default.</summary>
	[Parameter]
	public bool Lazy { get; set; } = true;

	/// <summary>Any other attributes - <c>class</c>, <c>style</c>, <c>data-*</c> - are applied to the image.</summary>
	[Parameter(CaptureUnmatchedValues = true)]
	public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

	/// <summary>The image URL, or null when there is nothing to render.</summary>
	private string? Source { get; set; }

	/// <summary>The alternative text actually rendered.</summary>
	private string AltText => Alt ?? DefaultAlt();

	/// <inheritdoc />
	protected override void OnParametersSet()
	{
		Source = BuildSource();
	}

	private string? BuildSource()
	{
		if (string.IsNullOrWhiteSpace(BaseUrl))
		{
			// Rendering nothing beats throwing: an exception from a component's render tears down the
			// Blazor circuit and takes the whole page with it, which is a harsh punishment for a page
			// whose configuration has not arrived yet.
			return null;
		}

		var request = new MapRequest
		{
			Center = Center,
			Location = Location,
			Zoom = Zoom,
			Width = Width,
			Height = Height,
			Scale = Scale,
			Format = Format,
			Markers = Markers,
			Paths = Paths,
			Polygons = Polygons,
			Regions = Regions
		};

		try
		{
			return StaticMapUrlBuilder.Build(BaseUrl!, request);
		}
		catch (ArgumentException)
		{
			// Nothing to draw yet - no centre, no location, no overlays. Common while data loads, and a
			// URL built from it would only render a broken-image icon.
			return null;
		}
	}

	private string DefaultAlt()
	{
		if (!string.IsNullOrWhiteSpace(Location))
		{
			return $"Map of {Location}";
		}

		if (Center is { } center)
		{
			return $"Map centred on {center.Latitude:0.####}, {center.Longitude:0.####}";
		}

		return "Map";
	}
}
