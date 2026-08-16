using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PanoramicData.Maps;

/// <summary>
/// Dependency-injection helpers for the maps core services.
/// </summary>
public static class MapsServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="MapsOptions"/> and the Photon-backed <see cref="IGeocoder"/>.
	/// The <see cref="IMapRenderer"/> is registered separately by the hosting application.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">Configuration containing the <c>Maps</c> section.</param>
	/// <returns>The service collection, for chaining.</returns>
	public static IServiceCollection AddPanoramicDataMaps(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		services
			.AddOptions<MapsOptions>()
			.Bind(configuration.GetSection(MapsOptions.SectionName));

		services.AddHttpClient<IGeocoder, PhotonGeocoder>((sp, client) =>
		{
			var options = sp.GetRequiredService<IOptions<MapsOptions>>().Value;
			var baseUrl = options.PhotonBaseUrl.TrimEnd('/') + "/";
			client.BaseAddress = new Uri(baseUrl);
		});

		return services;
	}
}
