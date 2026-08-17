using System.Collections.Frozen;
using System.Reflection;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace PanoramicData.Maps;

/// <summary>
/// Provides country boundary geometry (lon/lat) for named-region shading (issue #6), from the
/// embedded Natural Earth admin-0 1:110m dataset (public domain — no attribution obligation, unlike
/// the ODbL basemap).
/// <para>
/// Features are keyed on <c>adm0_a3</c> and never on <c>ISO_A3</c>/<c>ISO_A3_EH</c>: in this dataset
/// those are <c>-99</c> for France, Norway and several others, so keying on them silently drops those
/// countries. Callers resolve an arbitrary region code to alpha-3 via <see cref="Countries.ResolveAlpha3"/>
/// first. A code that resolves but has no boundary here (e.g. a micro-state absent from the 1:110m
/// set) returns <see langword="false"/> so the caller can reject rather than render nothing.
/// </para>
/// </summary>
public static class RegionBoundaries
{
	private static readonly FrozenDictionary<string, Geometry> ByAlpha3 = Load();

	/// <summary>Alpha-3 codes that have boundary geometry available.</summary>
	public static IReadOnlyCollection<string> AvailableAlpha3 => ByAlpha3.Keys;

	/// <summary>
	/// Gets the boundary geometry (WGS84 lon/lat) for an ISO alpha-3 country code.
	/// </summary>
	/// <param name="alpha3">The ISO alpha-3 code (case-insensitive).</param>
	/// <param name="geometry">The boundary geometry on success.</param>
	/// <returns><see langword="true"/> if geometry was found.</returns>
	public static bool TryGet(string alpha3, out Geometry geometry)
	{
		if (!string.IsNullOrWhiteSpace(alpha3) && ByAlpha3.TryGetValue(alpha3.Trim().ToUpperInvariant(), out var g))
		{
			geometry = g;
			return true;
		}

		geometry = Polygon.Empty;
		return false;
	}

	private static FrozenDictionary<string, Geometry> Load()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var resource = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("ne_110m_admin_0_countries.min.geojson", StringComparison.Ordinal))
			?? throw new InvalidOperationException("Embedded Natural Earth admin-0 resource not found.");
		using var stream = assembly.GetManifestResourceStream(resource)!;
		using var reader = new StreamReader(stream);
		var json = reader.ReadToEnd();

		var collection = new GeoJsonReader().Read<FeatureCollection>(json);
		var map = new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase);
		foreach (var feature in collection)
		{
			if (feature.Geometry is null || feature.Attributes is null)
			{
				continue;
			}

			var code = feature.Attributes.GetOptionalValue("adm0_a3") as string;
			if (!string.IsNullOrWhiteSpace(code))
			{
				map[code.ToUpperInvariant()] = feature.Geometry;
			}
		}

		return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}
}
