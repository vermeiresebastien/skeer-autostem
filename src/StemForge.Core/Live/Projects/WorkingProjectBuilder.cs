using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Xml;

namespace StemForge.Core.Live.Projects;

/// <summary>Description of the clip to place in the generated Live Set.</summary>
public sealed record AudioClipSpec(
    string SourcePath,
    long SourceBytes,
    long SampleFrames,
    int SampleRate,
    string ClipName,
    double Tempo = 120.0)
{
    /// <summary>
    /// The clip's length in beats. The clip is written unwarped, so Live derives its
    /// musical length from the sample duration and the set tempo; using the same
    /// relationship here keeps the start/end markers exactly on the whole file.
    /// </summary>
    public double LengthBeats => SampleFrames / (double)SampleRate * Tempo / 60.0;
}

/// <summary>
/// Generates the dedicated working Live Set this tool owns.
///
/// The set is derived from Live's own empty "Audio Track" template, so the schema is
/// always valid, and exactly one audio clip is injected. The clip is configured so
/// that nothing can shorten or alter the source:
///   * CurrentStart/CurrentEnd cover the entire sample;
///   * looping is off;
///   * warping is off (no time-stretching);
///   * clip volume is unity and pitch/fade are neutral;
///   * the source file is referenced in place and never copied or modified.
/// </summary>
public static class WorkingProjectBuilder
{
    public const string WorkingProjectFileName = "autoSTEM Working Set.als";

    private const string AudioClipTemplate = """
        <AudioClip Id="0" Time="0"><LomId Value="0" /><LomIdView Value="0" /><CurrentStart Value="0" /><CurrentEnd Value="{LEN}" /><Loop><LoopStart Value="0" /><LoopEnd Value="{LEN}" /><StartRelative Value="0" /><LoopOn Value="false" /><OutMarker Value="{LEN}" /><HiddenLoopStart Value="0" /><HiddenLoopEnd Value="{LEN}" /></Loop><Name Value="{NAME}" /><Annotation Value="" /><Color Value="12" /><LaunchMode Value="0" /><LaunchQuantisation Value="0" /><TimeSignature><TimeSignatures><RemoteableTimeSignature Id="0"><Numerator Value="4" /><Denominator Value="4" /><Time Value="0" /></RemoteableTimeSignature></TimeSignatures></TimeSignature><Envelopes><Envelopes /></Envelopes><ScrollerTimePreserver><LeftTime Value="0" /><RightTime Value="{LEN}" /></ScrollerTimePreserver><TimeSelection><AnchorTime Value="0" /><OtherTime Value="0" /></TimeSelection><Legato Value="false" /><Ram Value="false" /><GrooveSettings><GrooveId Value="-1" /></GrooveSettings><Disabled Value="false" /><VelocityAmount Value="0" /><FollowAction><FollowTime Value="4" /><IsLinked Value="true" /><LoopIterations Value="1" /><FollowActionA Value="4" /><FollowActionB Value="0" /><FollowChanceA Value="100" /><FollowChanceB Value="0" /><JumpIndexA Value="1" /><JumpIndexB Value="1" /><FollowActionEnabled Value="false" /></FollowAction><Grid><FixedNumerator Value="1" /><FixedDenominator Value="16" /><GridIntervalPixel Value="20" /><Ntoles Value="2" /><SnapToGrid Value="true" /><Fixed Value="false" /></Grid><FreezeStart Value="0" /><FreezeEnd Value="0" /><IsWarped Value="false" /><TakeId Value="1" /><SampleRef><FileRef><RelativePathType Value="0" /><RelativePath Value="" /><Path Value="{PATH}" /><Type Value="1" /><LivePackName Value="" /><LivePackId Value="" /><OriginalFileSize Value="{SIZE}" /></FileRef><LastModDate Value="{MODDATE}" /><SampleUsageHint Value="0" /><DefaultDuration Value="{FRAMES}" /><DefaultSampleRate Value="{RATE}" /></SampleRef><Onsets><UserOnsets /><HasUserOnsets Value="false" /></Onsets><WarpMode Value="0" /><GranularityTones Value="30" /><GranularityTexture Value="65" /><FluctuationTexture Value="25" /><TransientResolution Value="6" /><TransientLoopMode Value="2" /><TransientEnvelope Value="100" /><ComplexProFormants Value="100" /><ComplexProEnvelope Value="128" /><Sync Value="true" /><HiQ Value="true" /><Fade Value="false" /><Fades><FadeInLength Value="0" /><FadeOutLength Value="0" /><ClipFadesAreInitialized Value="true" /><CrossfadeInState Value="0" /><FadeInCurveSkew Value="0" /><FadeInCurveSlope Value="0" /><FadeOutCurveSkew Value="0" /><FadeOutCurveSlope Value="0" /><IsDefaultFadeIn Value="true" /><IsDefaultFadeOut Value="true" /></Fades><PitchCoarse Value="0" /><PitchFine Value="0" /><SampleVolume Value="1" /><WarpMarkers><WarpMarker Id="0" SecTime="0" BeatTime="0" /></WarpMarkers><SavedWarpMarkersForStretched /><MarkersGenerated Value="false" /><IsSongTempoLeader Value="false" /></AudioClip>
        """;

    private static readonly string[] TemplateRelativePaths =
    {
        Path.Combine("Defaults", "Creating Tracks", "Audio Track", "Default Audio Track.als"),
        Path.Combine("Resources", "Core Library", "Defaults", "Creating Tracks", "Audio Track", "Default Audio Track.als"),
        Path.Combine("Resources", "Builtin", "Templates", "Default Audio Track.als"),
    };

    /// <summary>
    /// Finds Live's stock empty audio-track template, used as the structural base for
    /// the working project. Using Live's own file guarantees the XML schema is one the
    /// installed build accepts.
    /// </summary>
    public static string? FindBaseTemplate(LiveInstall install)
    {
        var roots = LiveLocator.CoreLibraryRoots(install)
            .Prepend(install.InstallRoot)
            .Distinct()
            .ToArray();

        foreach (var root in roots)
        {
            foreach (var relative in TemplateRelativePaths)
            {
                var candidate = Path.Combine(root, relative);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>Creates (or refreshes) the working project folder and Live Set for a job.</summary>
    public static string CreateProject(string workingFolder, string baseTemplatePath, AudioClipSpec spec)
    {
        Directory.CreateDirectory(workingFolder);

        var alsBytes = File.ReadAllBytes(baseTemplatePath);
        var xml = Decompress(alsBytes);

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);

        InjectClip(document, spec);

        var projectPath = Path.Combine(workingFolder, WorkingProjectFileName);
        AtomicWrite(projectPath, Compress(document.OuterXml));

        return projectPath;
    }

    /// <summary>Pure, testable transform: inject the clip into an already-parsed Live Set.</summary>
    public static void InjectClip(XmlDocument document, AudioClipSpec spec)
    {
        var clipSlot = document.SelectSingleNode("//AudioTrack/DeviceChain/MainSequencer/ClipSlotList/ClipSlot")
            ?? throw new InvalidOperationException("The Live template has no audio clip slot.");

        var valueNode = clipSlot.SelectSingleNode("ClipSlot/Value")
            ?? throw new InvalidOperationException("The Live template clip slot has no Value element.");

        var escapedPath = spec.SourcePath.Replace('\\', '/');
        var lengthBeats = spec.LengthBeats.ToString("0.######", CultureInfo.InvariantCulture);

        var clipXml = AudioClipTemplate
            .Replace("{LEN}", lengthBeats)
            .Replace("{NAME}", SecurityElement.Escape(spec.ClipName))
            .Replace("{PATH}", SecurityElement.Escape(escapedPath))
            .Replace("{SIZE}", spec.SourceBytes.ToString(CultureInfo.InvariantCulture))
            .Replace("{FRAMES}", spec.SampleFrames.ToString(CultureInfo.InvariantCulture))
            .Replace("{RATE}", spec.SampleRate.ToString(CultureInfo.InvariantCulture))
            .Replace("{MODDATE}", LastModDate(spec.SourcePath));

        // Replace the empty <Value /> with the clip.
        valueNode.InnerXml = string.Empty;
        var fragment = document.CreateDocumentFragment();
        fragment.InnerXml = clipXml;
        valueNode.AppendChild(fragment);

        // Name the track after the track, and mark the clip as the current selection so
        // the Create menu's "Separate Stems to New Audio Tracks" entry is available.
        if (document.SelectSingleNode("//AudioTrack/Name/EffectiveName") is XmlElement trackName)
            trackName.SetAttribute("Value", spec.ClipName);

        foreach (XmlElement flag in document.SelectNodes("//AudioTrack/IsContentSelectedInDocument")!)
        {
            flag.SetAttribute("Value", "true");
        }

        // Keep the set tempo explicit so the clip's beat length is deterministic.
        if (document.SelectSingleNode("//Transport/PhaseNudgeTempo") is XmlElement tempo)
            tempo.SetAttribute("Value", spec.Tempo.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private static string LastModDate(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path))
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "0";
        }
    }

    private static string Decompress(byte[] gz)
    {
        using var input = new MemoryStream(gz);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] Compress(string xml)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(xml);
        }
        return output.ToArray();
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }
}
