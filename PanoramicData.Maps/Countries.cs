using System.Collections.Frozen;
using System.Reflection;

namespace PanoramicData.Maps;

/// <summary>
/// ISO 3166-1 country reference plus a handful of colloquial aliases. Used to
/// <list type="bullet">
/// <item>rewrite an ambiguous whole-query geocoder input such as <c>USA</c> or <c>UK</c> to a
/// canonical country name before it reaches Photon (issue #4), and</item>
/// <item>resolve a region code (alpha-2, alpha-3, colloquial alias or full name) to its ISO alpha-3,
/// which is the stable key for boundary lookup (issue #6).</item>
/// </list>
/// The data is the ISO 3166-1 list (public), embedded as <c>data/iso3166.txt</c> in the format
/// <c>alpha2|alpha3|Name</c> per line.
/// </summary>
public static class Countries
{
	private sealed record Country(string Alpha2, string Alpha3, string Name);

	private static readonly IReadOnlyList<Country> All = Load();
	private static readonly FrozenDictionary<string, Country> ByAlpha2 =
		All.ToFrozenDictionary(c => c.Alpha2, StringComparer.OrdinalIgnoreCase);
	private static readonly FrozenDictionary<string, Country> ByAlpha3 =
		All.ToFrozenDictionary(c => c.Alpha3, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Colloquial forms that are not (or not only) an official ISO code, mapped to an ISO alpha-3.
	/// <c>GB</c>/<c>US</c> are covered by the ISO tables; <c>UK</c>/<c>USA</c>/<c>UAE</c> are the
	/// colloquial forms that appear in real inputs.
	/// </summary>
	private static readonly FrozenDictionary<string, string> Colloquial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["UK"] = "GBR",
		["USA"] = "USA",
		["UAE"] = "ARE",
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Short, commonly-understood names preferred over the verbose ISO 3166 short name for a handful of
	/// countries, because they geocode more reliably (e.g. "United Kingdom" rather than "United Kingdom
	/// of Great Britain and Northern Ireland"). Keyed by alpha-3.
	/// </summary>
	private static readonly FrozenDictionary<string, string> PreferredNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["GBR"] = "United Kingdom",
		["USA"] = "United States",
		["ARE"] = "United Arab Emirates",
		["RUS"] = "Russia",
		["KOR"] = "South Korea",
		["PRK"] = "North Korea",
		["IRN"] = "Iran",
		["SYR"] = "Syria",
		["LAO"] = "Laos",
		["VNM"] = "Vietnam",
		["TWN"] = "Taiwan",
		["BOL"] = "Bolivia",
		["VEN"] = "Venezuela",
		["TZA"] = "Tanzania",
		["MDA"] = "Moldova",
		["CZE"] = "Czechia",
		["BRN"] = "Brunei",
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>Maps every ISO short name and every preferred short name to its alpha-3 (case-insensitive).</summary>
	private static readonly FrozenDictionary<string, string> NameToAlpha3 = BuildNameIndex();

	/// <summary>
	/// If <paramref name="query"/> is, in its entirety, a country code or colloquial alias, returns a
	/// clean country name to search instead; otherwise <see langword="null"/> (search the input
	/// verbatim). Only whole-string matches are rewritten, so ordinary place searches are untouched.
	/// </summary>
	/// <param name="query">The raw geocoder query.</param>
	/// <returns>The canonical country name, or <see langword="null"/> if no rewrite applies.</returns>
	public static string? ResolveName(string? query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return null;
		}

		var alpha3 = CodeOrAliasToAlpha3(query.Trim());
		return alpha3 is null ? null : PreferredOrIsoName(alpha3);
	}

	/// <summary>
	/// Maps a whole-string colloquial alias ("UK", "USA") or an ISO alpha-2/alpha-3 code to its alpha-3,
	/// or <see langword="null"/> when the text is neither. Length is checked before the lookup so an
	/// ordinary place search never pays for it.
	/// </summary>
	private static string? CodeOrAliasToAlpha3(string key)
	{
		if (Colloquial.TryGetValue(key, out var colloquial))
		{
			return colloquial;
		}

		if (key.Length == 3 && ByAlpha3.TryGetValue(key, out var a3))
		{
			return a3.Alpha3;
		}

		return key.Length == 2 && ByAlpha2.TryGetValue(key, out var a2) ? a2.Alpha3 : null;
	}

	/// <summary>
	/// The name to search for a country: the preferred short name where one is configured (so "Russia"
	/// rather than "Russian Federation"), otherwise the ISO name.
	/// </summary>
	private static string? PreferredOrIsoName(string alpha3)
	{
		if (PreferredNames.TryGetValue(alpha3, out var preferred))
		{
			return preferred;
		}

		return ByAlpha3.TryGetValue(alpha3, out var country) ? country.Name : null;
	}

	/// <summary>
	/// Resolves a region code — alpha-2, alpha-3, colloquial alias, or a full country name — to its
	/// ISO alpha-3 (upper-case), or <see langword="null"/> if it matches no known country.
	/// </summary>
	/// <param name="code">The region code or name.</param>
	/// <returns>The ISO alpha-3 code, or <see langword="null"/>.</returns>
	public static string? ResolveAlpha3(string? code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return null;
		}

		var key = code.Trim();
		if (Colloquial.TryGetValue(key, out var alias))
		{
			return alias;
		}

		if (key.Length == 3 && ByAlpha3.TryGetValue(key, out var a3))
		{
			return a3.Alpha3;
		}

		if (key.Length == 2 && ByAlpha2.TryGetValue(key, out var a2))
		{
			return a2.Alpha3;
		}

		return NameToAlpha3.TryGetValue(key, out var byName) ? byName : null;
	}

	private static FrozenDictionary<string, string> BuildNameIndex()
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var country in All)
		{
			map[country.Name] = country.Alpha3;
		}

		foreach (var preferred in PreferredNames)
		{
			map[preferred.Value] = preferred.Key;
		}

		return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}

	private static IReadOnlyList<Country> Load()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("iso3166.txt", StringComparison.Ordinal))
			?? throw new InvalidOperationException("Embedded resource 'iso3166.txt' not found.");
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var reader = new StreamReader(stream);
		var text = reader.ReadToEnd();

		var list = new List<Country>();
		foreach (var line in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var parts = line.Split('|');
			if (parts.Length == 3 && parts[0].Length == 2 && parts[1].Length == 3)
			{
				list.Add(new Country(parts[0], parts[1], parts[2]));
			}
		}

		return list;
	}
}
