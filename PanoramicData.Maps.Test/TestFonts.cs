namespace PanoramicData.Maps.Test;

/// <summary>
/// The font the rendering tests draw text with. It ships with the tests (see TestData/Fonts/README.md)
/// so that text rendering does not depend on which fonts, if any, the machine running the tests has
/// installed: the CI runner image has none at all.
/// </summary>
internal static class TestFonts
{
	/// <summary>Full path to Liberation Sans Regular, copied to the test output directory.</summary>
	public static string SansRegular { get; } = Path.Combine(AppContext.BaseDirectory, "TestData", "Fonts", "LiberationSans-Regular.ttf");
}
