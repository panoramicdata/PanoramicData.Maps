using AwesomeAssertions;
using Bunit;
using PanoramicData.Maps.Blazor;
using Xunit;

namespace PanoramicData.Maps.Test;

/// <summary>
/// The component is a thin shell over <see cref="StaticMapUrlBuilder"/> (issue #17). These tests pin
/// the two things that make it safe and useful: it renders a plain image with no JavaScript, and it
/// has no way to put an API key into markup.
/// </summary>
public class StaticMapComponentTests : BunitContext
{
	private const string BaseUrl = "https://maps.example.com";

	[Fact]
	public void Renders_AnImageWhoseSourceComesFromTheSharedUrlBuilder()
	{
		var component = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Center, new GeoPoint(-0.1278, 51.5074))
			.Add(p => p.Zoom, 12));

		var img = component.Find("img");
		var expected = StaticMapUrlBuilder.Build(BaseUrl, new MapRequest { Center = new GeoPoint(-0.1278, 51.5074), Zoom = 12 });

		img.GetAttribute("src").Should().Be(expected, "the component must not build URLs of its own");
	}

	[Fact]
	public void Renders_NoScriptTagAtAll()
	{
		var component = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Location, "London")
			.Add(p => p.Zoom, 10));

		component.Markup.Should().NotContain("<script", "a static map needs no JavaScript, and no JS means no interop lifetime hazards");
	}

	[Fact]
	public void Passes_SizeThroughToBothTheUrlAndTheImageElement()
	{
		var component = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Center, new GeoPoint(0, 0))
			.Add(p => p.Zoom, 4)
			.Add(p => p.Width, 640)
			.Add(p => p.Height, 360));

		var img = component.Find("img");
		img.GetAttribute("src").Should().Contain("size=640x360");
		img.GetAttribute("width").Should().Be("640", "the intrinsic size avoids a layout shift while the image loads");
		img.GetAttribute("height").Should().Be("360");
	}

	[Fact]
	public void Scale_DoublesThePixelsWithoutChangingTheLayoutSize()
	{
		var component = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Center, new GeoPoint(0, 0))
			.Add(p => p.Zoom, 4)
			.Add(p => p.Width, 400)
			.Add(p => p.Height, 300)
			.Add(p => p.Scale, 2));

		var img = component.Find("img");
		img.GetAttribute("src").Should().Contain("scale=2");
		img.GetAttribute("width").Should().Be("400", "an @2x image still occupies its CSS size");
	}

	[Fact]
	public void Markers_AreWrittenIntoTheUrl()
	{
		var component = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Center, new GeoPoint(-0.1278, 51.5074))
			.Add(p => p.Zoom, 12)
			.Add(p => p.Markers,
			[
				new MarkerSpec { Location = new GeoPoint(-0.1278, 51.5074), Color = "red", Label = "A" }
			]));

		component.Find("img").GetAttribute("src").Should().Contain("markers=");
	}

	[Fact]
	public void Alt_DefaultsToSomethingUsefulAndCanBeOverridden()
	{
		var byDefault = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Location, "Maidenhead")
			.Add(p => p.Zoom, 13));

		byDefault.Find("img").GetAttribute("alt").Should().Contain("Maidenhead", "the fallback should describe the map, not say 'image'");

		var overridden = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Location, "Maidenhead")
			.Add(p => p.Zoom, 13)
			.Add(p => p.Alt, "Site location"));

		overridden.Find("img").GetAttribute("alt").Should().Be("Site location");
	}

	[Fact]
	public void Loading_IsLazyByDefaultAndCanBeMadeEager()
	{
		Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Center, new GeoPoint(0, 0))
			.Add(p => p.Zoom, 2))
			.Find("img").GetAttribute("loading").Should().Be("lazy");

		Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Center, new GeoPoint(0, 0))
			.Add(p => p.Zoom, 2)
			.Add(p => p.Lazy, false))
			.Find("img").GetAttribute("loading").Should().BeNull();
	}

	[Fact]
	public void AdditionalAttributes_AreSplattedOntoTheImage()
	{
		var component = Render<StaticMap>(parameters => parameters
			.Add(p => p.BaseUrl, BaseUrl)
			.Add(p => p.Center, new GeoPoint(0, 0))
			.Add(p => p.Zoom, 2)
			.AddUnmatched("class", "rounded shadow")
			.AddUnmatched("data-testid", "site-map"));

		var img = component.Find("img");
		img.GetAttribute("class").Should().Be("rounded shadow");
		img.GetAttribute("data-testid").Should().Be("site-map");
	}

	[Fact]
	public void NothingToDraw_RendersNothingRatherThanABrokenImage()
	{
		// A component that renders <img src="…/staticmap?"> would show a broken-image icon on every page
		// that has not yet chosen a location - a common state while data loads.
		var component = Render<StaticMap>(parameters => parameters.Add(p => p.BaseUrl, BaseUrl));

		component.Markup.Trim().Should().BeEmpty();
	}

	[Fact]
	public void MissingBaseUrl_RendersNothingRatherThanThrowingInsideACircuit()
	{
		// Throwing from a component's render tears down the Blazor circuit and takes the page with it.
		var component = Render<StaticMap>(parameters => parameters
			.Add(p => p.Center, new GeoPoint(0, 0))
			.Add(p => p.Zoom, 2));

		component.Markup.Trim().Should().BeEmpty();
	}

	[Fact]
	public void TheComponent_HasNoApiKeyParameter()
	{
		// By construction, not by convention: the component cannot leak a key because it cannot send one.
		// A key belongs in a header, added by a same-origin proxy in the host application.
		var parameterNames = typeof(StaticMap)
			.GetProperties()
			.Where(property => property.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.ParameterAttribute), true).Length > 0)
			.Select(property => property.Name)
			.ToList();

		parameterNames.Should().NotContain("ApiKey");
		parameterNames.Should().NotContain("Key");
	}
}
