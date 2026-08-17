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

	/// <summary>
	/// Default language ("en", "de", "fr", "it") passed to the geocoder when a caller does not
	/// supply one, so results come back with consistent naming (e.g. "Tokyo"/"Japan" rather than
	/// "東京都"/"日本"). Null leaves Photon's own default (local script). See issue #5.
	/// </summary>
	public string? DefaultLanguage { get; set; }

	/// <summary>
	/// Named map styles selectable via the <c>style</c>/<c>maptype</c> query parameter, mapping a
	/// style name (e.g. <c>terrain</c>) to a MapLibre style URL. Names are matched case-insensitively.
	/// Google <c>maptype</c> values with no open-data equivalent (<c>satellite</c>, <c>hybrid</c>) and
	/// any style name not present here alias to the default style; a completely unknown style name is
	/// rejected. See issue #7.
	/// </summary>
	public IDictionary<string, string> Styles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Maximum permitted image width in CSS pixels.</summary>
	public int MaxWidth { get; set; } = 2048;

	/// <summary>Maximum permitted image height in CSS pixels.</summary>
	public int MaxHeight { get; set; } = 2048;

	/// <summary>Maximum permitted device scale factor.</summary>
	public int MaxScale { get; set; } = 2;
}
