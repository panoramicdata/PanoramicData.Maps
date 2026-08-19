using System.Reflection;
using AwesomeAssertions;
using PanoramicData.Maps;
using Xunit;

namespace PanoramicData.Maps.Test;

public class MapsServiceInfoTests
{
	[Fact]
	public void Current_ReportsAVersion()
	{
		// Issue #11: /health could not say which build was serving. This is where that answer comes from.
		var info = MapsServiceInfo.Current;

		info.Version.Should().NotBeNullOrWhiteSpace();
		info.Version.Should().MatchRegex(@"^\d+\.\d+", "the version comes from Nerdbank.GitVersioning");
	}

	[Fact]
	public void Parse_SplitsTheGitCommitOutOfAnInformationalVersion()
	{
		// Nerdbank.GitVersioning stamps '<version>+<commit>'.
		var info = MapsServiceInfo.Parse("1.2.3+abcdef1234567890");

		info.Version.Should().Be("1.2.3");
		info.Commit.Should().Be("abcdef1234567890");
	}

	[Fact]
	public void Parse_HandlesAVersionWithNoCommitSuffix()
	{
		var info = MapsServiceInfo.Parse("4.5.6");

		info.Version.Should().Be("4.5.6");
		info.Commit.Should().BeNull();
	}

	[Fact]
	public void Parse_FallsBackWhenTheVersionIsMissing()
	{
		MapsServiceInfo.Parse(null).Version.Should().Be("unknown");
		MapsServiceInfo.Parse("   ").Version.Should().Be("unknown");
	}

	[Fact]
	public void ForAssembly_ReadsTheInformationalVersionAttribute()
	{
		var assembly = typeof(MapsServiceInfo).Assembly;
		var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

		MapsServiceInfo.For(assembly).Version.Should().Be(expected.Split('+')[0]);
	}
}
