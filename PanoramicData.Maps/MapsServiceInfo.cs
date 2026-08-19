using System.Reflection;

namespace PanoramicData.Maps;

/// <summary>
/// The identity of the running build, for the service's health surface.
/// <para>
/// The service runs in two clusters and is consumed by report macros in every Magic Suite
/// environment, so "which build is this instance serving?" is an operational question that has to be
/// answerable without inspecting the cluster (issue #11).
/// </para>
/// </summary>
/// <param name="Version">The version, without the git commit suffix (for example <c>0.2.5</c>).</param>
/// <param name="Commit">The git commit the build came from, when the version carries one.</param>
public sealed record MapsServiceInfo(string Version, string? Commit)
{
	/// <summary>Used when no version information is stamped on the assembly at all.</summary>
	public const string UnknownVersion = "unknown";

	/// <summary>The identity of the entry assembly - the running service.</summary>
	public static MapsServiceInfo Current { get; } = For(Assembly.GetEntryAssembly() ?? typeof(MapsServiceInfo).Assembly);

	/// <summary>Reads the identity from an assembly's informational version.</summary>
	/// <param name="assembly">The assembly to describe.</param>
	/// <returns>The version and commit it was built from.</returns>
	public static MapsServiceInfo For(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);

		var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		return Parse(informational ?? assembly.GetName().Version?.ToString());
	}

	/// <summary>
	/// Splits a Nerdbank.GitVersioning informational version - <c>&lt;version&gt;+&lt;commit&gt;</c> -
	/// into its parts.
	/// </summary>
	/// <param name="informationalVersion">The informational version, which may be null or empty.</param>
	/// <returns>The version and commit, or <see cref="UnknownVersion"/> when there is nothing to read.</returns>
	public static MapsServiceInfo Parse(string? informationalVersion)
	{
		if (string.IsNullOrWhiteSpace(informationalVersion))
		{
			return new MapsServiceInfo(UnknownVersion, null);
		}

		var trimmed = informationalVersion.Trim();
		var plus = trimmed.IndexOf('+', StringComparison.Ordinal);
		return plus < 0
			? new MapsServiceInfo(trimmed, null)
			: new MapsServiceInfo(trimmed[..plus], trimmed[(plus + 1)..] is { Length: > 0 } commit ? commit : null);
	}
}
