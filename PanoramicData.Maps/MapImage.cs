namespace PanoramicData.Maps;

/// <summary>
/// A rendered map image.
/// </summary>
/// <param name="Bytes">The encoded image bytes.</param>
/// <param name="ContentType">The MIME content type (e.g. <c>image/png</c>).</param>
public sealed record MapImage(byte[] Bytes, string ContentType);
