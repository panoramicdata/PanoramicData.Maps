namespace PanoramicData.Maps;

/// <summary>
/// The pixel geometry of a marker pin, and the size names Google Static Maps accepts.
/// <para>
/// Reports built against the Google Static Maps API expect a *pin*: a teardrop about 22 CSS pixels
/// wide and 40 tall, pointing at its coordinate. Issue #9 was that this renderer drew an 18-pixel
/// circle with its tail hidden inside the head, so every marker read as a small dot with an
/// unreadably small label - and three of Google's four size names collapsed into two.
/// </para>
/// </summary>
/// <param name="Width">Overall pin width in device pixels.</param>
/// <param name="Height">Overall pin height in device pixels, from tip to the top of the head.</param>
public readonly record struct MarkerMetrics(float Width, float Height)
{
	/// <summary>Width of a 'normal' Google pin, in CSS pixels.</summary>
	private const float NormalWidth = 22f;

	/// <summary>Height of a 'normal' Google pin, in CSS pixels.</summary>
	private const float NormalHeight = 40f;

	/// <summary>
	/// How much larger or smaller this marker is than a Google 'normal' pin - marker scale multiplied by
	/// the image's device scale. Used to size sprite icons consistently with pins.
	/// </summary>
	public float ScaleFactor => Width / NormalWidth;

	/// <summary>Radius of the pin's circular head.</summary>
	public float HeadRadius => Width / 2f;

	/// <summary>
	/// Font size for a label drawn in the head. Sized to the head rather than to the whole pin, so a
	/// single character fills it without spilling over the outline.
	/// </summary>
	public float LabelFontSize => HeadRadius * 1.25f;

	/// <summary>The y coordinate of the pin's tip, which is the marker's anchor.</summary>
	/// <param name="anchorY">The y coordinate the marker points at.</param>
	/// <returns>The tip's y coordinate.</returns>
	public float TipY(float anchorY) => anchorY;

	/// <summary>The y coordinate of the top of the pin's head.</summary>
	/// <param name="anchorY">The y coordinate the marker points at.</param>
	/// <returns>The top edge's y coordinate.</returns>
	public float TopY(float anchorY) => anchorY - Height;

	/// <summary>The y coordinate of the centre of the pin's circular head.</summary>
	/// <param name="anchorY">The y coordinate the marker points at.</param>
	/// <returns>The head centre's y coordinate.</returns>
	public float HeadCenterY(float anchorY) => TopY(anchorY) + HeadRadius;

	/// <summary>
	/// The metrics for a marker at the given relative marker scale, rendered into an image at the
	/// given device scale factor.
	/// </summary>
	/// <param name="markerScale">Relative marker scale (1.0 = a Google 'normal' pin).</param>
	/// <param name="imageScale">The image's device scale factor (2 = @2x output).</param>
	/// <returns>The pin geometry in device pixels.</returns>
	public static MarkerMetrics For(double markerScale, int imageScale)
	{
		var factor = (float)Math.Max(markerScale, 0.05) * Math.Max(imageScale, 1);
		return new MarkerMetrics(NormalWidth * factor, NormalHeight * factor);
	}

	/// <summary>
	/// The relative scale for a Google <c>size</c> descriptor. The four documented sizes produce four
	/// visibly different pins; anything unrecognised is treated as <c>normal</c>, as Google does.
	/// </summary>
	/// <param name="size">The <c>size</c> descriptor value, for example <c>mid</c>.</param>
	/// <returns>The relative marker scale.</returns>
	public static double ScaleForSize(string? size) => size?.ToLowerInvariant() switch
	{
		"tiny" => 0.4,
		"small" => 0.55,
		"mid" => 0.8,
		_ => 1.0,
	};
}
