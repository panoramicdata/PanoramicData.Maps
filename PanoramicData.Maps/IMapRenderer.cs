namespace PanoramicData.Maps;

/// <summary>
/// Renders a <see cref="MapRequest"/> into a <see cref="MapImage"/>.
/// </summary>
public interface IMapRenderer
{
	/// <summary>
	/// Renders the requested map to an encoded image.
	/// </summary>
	/// <param name="request">The map to render.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The rendered image.</returns>
	Task<MapImage> RenderAsync(MapRequest request, CancellationToken cancellationToken = default);
}
