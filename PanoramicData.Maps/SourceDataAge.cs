namespace PanoramicData.Maps;

/// <summary>How old one source dataset is, for staleness monitoring.</summary>
/// <param name="Source">Where the reading came from: a URL or a local path.</param>
/// <param name="LastUpdatedUtc">When the data was last produced, or null when unknown.</param>
/// <param name="AgeDays">Whole days since <paramref name="LastUpdatedUtc"/>, or null when unknown.</param>
/// <param name="Status"><c>ok</c> when the age was read, otherwise <c>unknown</c>.</param>
/// <param name="Note">Why the age is unknown; null when it is known.</param>
public sealed record DataAge(string Source, DateTimeOffset? LastUpdatedUtc, int? AgeDays, string Status, string? Note)
{
	/// <summary>A reading that could not be taken.</summary>
	/// <param name="source">Where the reading was attempted.</param>
	/// <param name="note">Why it failed.</param>
	/// <returns>An <c>unknown</c> reading.</returns>
	public static DataAge Unknown(string source, string note) => new(source, null, null, "unknown", note);

	/// <summary>A reading dated <paramref name="updated"/>, measured at <paramref name="now"/>.</summary>
	/// <param name="source">Where the reading came from.</param>
	/// <param name="updated">When the data was produced.</param>
	/// <param name="now">The current time.</param>
	/// <returns>An <c>ok</c> reading.</returns>
	public static DataAge Known(string source, DateTimeOffset updated, DateTimeOffset now)
		=> new(source, updated.ToUniversalTime(), Math.Max(0, (int)(now - updated).TotalDays), "ok", null);
}

/// <summary>The age of both source datasets, as reported on <c>/health</c>.</summary>
/// <param name="Tiles">The Protomaps PMTiles archive.</param>
/// <param name="Photon">The Photon geocoder index.</param>
public sealed record SourceDataAge(DataAge Tiles, DataAge Photon);
