using System.ComponentModel.DataAnnotations;

namespace PanoramicData.Maps;

/// <summary>
/// Configuration for the maps stack. Bound from the <c>Maps</c> configuration section.
/// </summary>
public sealed class MapsOptions
{
	/// <summary>The configuration section name.</summary>
	public const string SectionName = "Maps";

	/// <summary>
	/// Base URL of the Photon geocoder (e.g. <c>https://photon.panoramicdata.com</c>).
	/// </summary>
	[Required]
	public string PhotonBaseUrl { get; set; } = "https://photon.panoramicdata.com";

	/// <summary>
	/// URL of the MapLibre style JSON served by the tile service
	/// (e.g. <c>https://tiles.panoramicdata.com/style.json</c>).
	/// </summary>
	[Required]
	public string TilesStyleUrl { get; set; } = "https://tiles.panoramicdata.com/style.json";

	/// <summary>
	/// When <see langword="true"/>, requests must present a valid API key (see <see cref="ApiKeys"/>).
	/// Defaults to <see langword="false"/> so the open-source image is usable out of the box; the
	/// canonical hosted service sets this to <see langword="true"/> to meter and monetise access.
	/// </summary>
	public bool RequireApiKey { get; set; }

	/// <summary>Accepted API keys when <see cref="RequireApiKey"/> is enabled.</summary>
	public IList<string> ApiKeys { get; set; } = [];

	/// <summary>Maximum permitted image width in CSS pixels.</summary>
	public int MaxWidth { get; set; } = 2048;

	/// <summary>Maximum permitted image height in CSS pixels.</summary>
	public int MaxHeight { get; set; } = 2048;

	/// <summary>Maximum permitted device scale factor.</summary>
	public int MaxScale { get; set; } = 2;
}
