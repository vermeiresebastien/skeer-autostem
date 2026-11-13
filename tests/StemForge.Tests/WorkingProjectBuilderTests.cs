using StemForge.Core.Audio;
using StemForge.Core.Live;
using StemForge.Core.Live.Projects;
using StemForge.Core.Models;
using System.Xml;
using Xunit;

namespace StemForge.Tests;

public sealed class WorkingProjectBuilderTests
{
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Ableton MajorVersion="5" MinorVersion="12.0_12300">
          <LiveSet>
            <Tracks>
              <AudioTrack Id="0">
                <IsContentSelectedInDocument Value="false" />
                <Name>
                  <EffectiveName Value="1-Audio" />
                  <UserName Value="" />
                </Name>
                <DeviceChain>
                  <MainSequencer>
                    <ClipSlotList>
                      <ClipSlot Id="0">
                        <LomId Value="0" />
                        <ClipSlot>
                          <Value />
                        </ClipSlot>
                        <HasStop Value="true" />
                      </ClipSlot>
                    </ClipSlotList>
                    <Sample>
                      <ArrangerAutomation>
                        <Events />
                      </ArrangerAutomation>
                    </Sample>
                  </MainSequencer>
                </DeviceChain>
              </AudioTrack>
            </Tracks>
            <Transport>
              <PhaseNudgeTempo Value="120" />
            </Transport>
          </LiveSet>
        </Ableton>
        """;

    private static XmlDocument LoadFixture()
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(Fixture);
        return document;
    }

    [Fact]
    public void TheInjectedClipCoversTheWholeFileWithNoLoopingOrWarping()
    {
        var document = LoadFixture();
        var spec = new AudioClipSpec(@"C:\Music\Artist - Track.wav", 12345, 88200, 44100, "Artist - Track", 120.0);

        WorkingProjectBuilder.InjectClip(document, spec);

        Assert.Equal("0", Attr(document, "//AudioClip/CurrentStart"));
        Assert.Equal(spec.LengthBeats.ToString("0.######"), Attr(document, "//AudioClip/CurrentEnd"));
        Assert.Equal("false", Attr(document, "//AudioClip/Loop/LoopOn"));
        Assert.Equal("0", Attr(document, "//AudioClip/Loop/LoopStart"));
        Assert.Equal(spec.LengthBeats.ToString("0.######"), Attr(document, "//AudioClip/Loop/LoopEnd"));
        Assert.Equal("false", Attr(document, "//AudioClip/IsWarped"));
        Assert.Equal("1", Attr(document, "//AudioClip/SampleVolume"));
        Assert.Equal("0", Attr(document, "//AudioClip/PitchCoarse"));
        Assert.Equal("false", Attr(document, "//AudioClip/Fade"));
    }

    [Fact]
    public void TheSourceIsReferencedInPlaceWithItsRealSizeAndLength()
    {
        var document = LoadFixture();
        var spec = new AudioClipSpec(@"C:\Music\Artist - Track.wav", 12345, 88200, 44100, "Artist - Track", 120.0);

        WorkingProjectBuilder.InjectClip(document, spec);

        Assert.Equal("C:/Music/Artist - Track.wav", Attr(document, "//AudioClip/SampleRef/FileRef/Path"));
        Assert.Equal("0", Attr(document, "//AudioClip/SampleRef/FileRef/RelativePathType"));
        Assert.Equal("12345", Attr(document, "//AudioClip/SampleRef/FileRef/OriginalFileSize"));
        Assert.Equal("88200", Attr(document, "//AudioClip/SampleRef/DefaultDuration"));
        Assert.Equal("44100", Attr(document, "//AudioClip/SampleRef/DefaultSampleRate"));
        Assert.Equal("Artist - Track", Attr(document, "//AudioTrack/Name/EffectiveName"));
        Assert.Equal("true", Attr(document, "//AudioTrack/IsContentSelectedInDocument"));
    }

    [Fact]
    public void PathsContainingXmlSpecialCharactersAreEscaped()
    {
        var document = LoadFixture();
        var spec = new AudioClipSpec(@"C:\Music\Tom & Jerry <mix>.wav", 10, 100, 44100, "Tom & Jerry <mix>", 120.0);

        WorkingProjectBuilder.InjectClip(document, spec);

        Assert.Contains("Tom & Jerry <mix>", Attr(document, "//AudioClip/SampleRef/FileRef/Path"));
        Assert.DoesNotContain("&amp;amp;", Attr(document, "//AudioClip/Name"));
    }

    [Fact]
    public void CreateProjectWritesAGzippedSlowSetThatCanBeReadBack()
    {
        using var workspace = new TempWorkspace();
        var baseTemplate = workspace.Path("base.xml");
        File.WriteAllText(baseTemplate, Fixture);

        // CreateProject expects a gzipped base; gzip the fixture into place.
        var gzPath = workspace.Path("base.als");
        WriteGzip(gzPath, Fixture);

        var source = workspace.CreateWav("Track.wav");
        var spec = new AudioClipSpec(source, new FileInfo(source).Length, 88200, 44100, "Track");

        var project = WorkingProjectBuilder.CreateProject(workspace.NewFolder("work"), gzPath, spec);

        Assert.True(File.Exists(project));

        var xml = ReadGzip(project);
        Assert.Contains("<AudioClip", xml);

        var document = new XmlDocument();
        document.LoadXml(xml);
        Assert.Equal("false", Attr(document, "//AudioClip/Loop/LoopOn"));
    }

    [Fact]
    public void TheInstalledLiveTemplateIsAvailableAndUsable()
    {
        var install = LiveLocator.Locate();
        if (install is null) return;   // no Live on this machine: nothing to assert

        var template = WorkingProjectBuilder.FindBaseTemplate(install);
        Assert.NotNull(template);

        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("Track.wav");
        var spec = new AudioClipSpec(source, new FileInfo(source).Length, 44100, 44100, "Track");

        var project = WorkingProjectBuilder.CreateProject(workspace.NewFolder("work"), template!, spec);
        var document = new XmlDocument();
        document.LoadXml(ReadGzip(project));

        Assert.Equal("0", Attr(document, "//AudioClip/CurrentStart"));
        Assert.Equal("false", Attr(document, "//AudioClip/IsWarped"));
        Assert.NotEmpty(document.SelectNodes("//AudioTrack")!);
    }

    private static string Attr(XmlDocument document, string xpath) =>
        (document.SelectSingleNode(xpath) as XmlElement)?.GetAttribute("Value") ?? "<missing>";

    private static void WriteGzip(string path, string contents)
    {
        using var file = File.Create(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
        using var writer = new StreamWriter(gzip, new System.Text.UTF8Encoding(false));
        writer.Write(contents);
    }

    private static string ReadGzip(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
}
