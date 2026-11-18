namespace StemForge.Core.Live;

/// <summary>
/// An Ableton Live installation this tool can drive - and the single statement of what "can drive"
/// means.
///
/// The version floor and the edition that floor has to be found in live here and nowhere else, and
/// every sentence the product tells a user about what Live needs is built from
/// <see cref="RequirementPhrase"/>. That is the point of putting them here: the floor used to be
/// written four times over, in three languages of prose, and one of those copies compared a parsed
/// <see cref="Version"/> against a literal that was not this constant.
///
/// Both halves of the requirement fail, and they fail differently. A build older than
/// <see cref="MinimumStemSeparationVersion"/> has no "Separate Stems" command at all; a new enough
/// build in an edition without it has the command sitting greyed out in the Create menu. The second
/// case is the one this class used to wave through - and the only place the product could notice it
/// was inside Live, after the run had already taken the user's session over. A precondition is
/// asked before the thing it protects, so it is asked here.
/// </summary>
public sealed record LiveInstall(string ExePath, string Version, string Edition)
{
    /// <summary>
    /// The oldest Ableton Live this tool supports: <c>12.3</c>, the release that introduced
    /// "Separate Stems to New Audio Tracks".
    ///
    /// The 12.1 that stood here came from a doc comment restating another doc comment. Ableton's own
    /// release notes have no 12.1 separation entry, nothing on this machine has ever run a 12.1, and
    /// the research that settled the floor is in <c>docs/LICENSING-REQUIREMENTS.md</c> §1. Change
    /// this one line and every user-facing sentence moves with it - <c>LiveSupportGateTests</c> is
    /// what keeps a copy-pasted literal from coming back.
    /// </summary>
    public static readonly Version MinimumStemSeparationVersion = new(12, 3);

    /// <summary>The edition <see cref="MinimumStemSeparationVersion"/> has to be found in.</summary>
    public const string RequiredEdition = "Suite";

    /// <summary>
    /// Every edition name the product knows, in the order it tries them: a build whose name or path
    /// mentions more than one is read as the fuller product, which is how it has always been resolved.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownEditions = ["Suite", "Standard", "Intro", "Lite"];

    /// <summary>
    /// The editions this tool knows do not carry stem separation.
    ///
    /// An edition that is not on this list is not refused. <see cref="LiveLocator"/> falls back to
    /// <see cref="UnknownEdition"/> when neither the product name nor the path matched anything, and
    /// refusing on a string this side of Ableton would lock a real Suite install out of its own
    /// batch - which is not a hypothetical: on the installs this desk has, the edition is only ever
    /// in the path (<see cref="LiveLocator.ResolveEdition"/>). Unknown is therefore allowed, on
    /// purpose, and the trade is recorded here so the next reader finds a decision rather than an
    /// oversight: a Standard build whose detector was fooled runs and meets a greyed-out menu, which
    /// is the failure this gate exists to prevent - it is not made worse by a list that is short.
    ///
    /// The same asymmetry is why a name is matched as a whole word and not as a substring: "Lite"
    /// inside "Satellite Sessions" is a folder, and refusing somebody's batch on a folder word is a
    /// worse product than allowing one Standard install to meet the greyed-out menu.
    /// </summary>
    public static readonly IReadOnlyList<string> EditionsWithoutStemSeparation = ["Standard", "Intro", "Lite"];

    /// <summary>
    /// What <see cref="LiveLocator"/> reports for an edition it could not recognise.
    ///
    /// This is the normal answer, not the rare one. Ableton's own version info says ProductName
    /// <c>"Live"</c> on both installs on this machine - 12.3.2 under
    /// <c>C:\ProgramData\Ableton\Live 12 Suite\</c> and the 12.3.5 staging copy beside it, read
    /// 2026-11-16 - so nothing but the folder and file names ever carries an edition here. A label
    /// must never print this placeholder as though it were a product name: use
    /// <see cref="EditionLabelFor"/>.
    /// </summary>
    public const string UnknownEdition = "Live";

    /// <summary>
    /// What the simulated double reports as its edition (see <c>MainViewModel</c>'s simulated run).
    /// It is not an installation and there is no Live to check, so the gate exempts it: a run that
    /// never talks to Ableton must not be refused for lacking it.
    /// </summary>
    public const string SimulatedEdition = "Simulated";

    /// <summary>Reason codes - one per decision, greppable, never reused for a different decision.</summary>
    public const string SupportedReason = "live-supported";
    public const string VersionUnsupportedReason = "live-version-unsupported";
    public const string EditionUnsupportedReason = "live-edition-unsupported";

    public Version? ParsedVersion =>
        System.Version.TryParse(Version, out var parsed) ? parsed : null;

    /// <summary>
    /// "Live 12.3 Suite or newer" - the whole requirement as one phrase, for sentences that put it
    /// mid-line ("Needs Live 12.3 Suite or newer and the desktop to itself").
    /// </summary>
    public static string RequirementPhrase =>
        $"Live {MinimumStemSeparationVersion.Major}.{MinimumStemSeparationVersion.Minor} " +
        $"{RequiredEdition} or newer";

    /// <summary>The same requirement as the sentence a person reads at the end of a refusal.</summary>
    public static string RequirementSummary => $"{RequirementPhrase} is required.";

    /// <summary>
    /// The version half of the gate on its own - the "is it new enough" question, and nothing else.
    ///
    /// It is deliberately not the answer to "can this install be driven": a 12.3 Standard scores
    /// true here and cannot separate a stem, which is exactly the combination this whole change
    /// exists to stop. Callers deciding whether to start a run ask <see cref="Support"/>.
    /// </summary>
    public bool SupportsStemSeparation =>
        ParsedVersion is { } v && v >= MinimumStemSeparationVersion;

    /// <summary>
    /// The edition half. True for a recognised Suite install and for anything unrecognised - see
    /// <see cref="EditionsWithoutStemSeparation"/> for why that is the safe direction.
    /// </summary>
    public bool EditionIsSupported => !EditionIsKnownWithoutStemSeparation(Edition);

    /// <summary>The whole gate over this install.</summary>
    public SupportVerdict Support => CheckSupport(Version, Edition);

    public bool IsSupported => Support.IsSupported;

    /// <summary>
    /// Both High Speed and High Quality are exposed by the same Separate Stems dialog (a single
    /// "High Quality" toggle) for every build the product can drive at all. An edition without the
    /// dialog has neither mode, so this follows the full gate rather than the version alone.
    /// </summary>
    public bool SupportsBothProcessingModes => IsSupported;

    /// <summary>
    /// The edition as a label should print it: the name when one was found, and an honest
    /// "edition not detected" when the install did not name one. <see cref="UnknownEdition"/> is the
    /// placeholder <c>"Live"</c>, and a chip reading "Ableton Live 12.3.2 Live" both duplicates the
    /// word and implies the product knew something it did not.
    /// </summary>
    public static string EditionLabelFor(string? edition)
    {
        if (string.IsNullOrWhiteSpace(edition) || Is(edition, UnknownEdition))
            return "edition not detected";

        return edition.Trim();
    }

    public string DisplayName => $"Ableton Live {Version} {EditionLabelFor(Edition)}".Trim();

    /// <summary>Folder that contains the Program and Resources folders.</summary>
    public string InstallRoot
    {
        get
        {
            var programFolder = Path.GetDirectoryName(ExePath)!;   // ...\Program
            return Path.GetDirectoryName(programFolder) ?? programFolder;
        }
    }

    /// <summary>
    /// The gate, over the two strings a caller actually holds - the settings file keeps a version
    /// and an edition, not an install, and the Run button has to give the same answer the engine
    /// will.
    ///
    /// Version is asked first: a build from before the feature is the more fundamental gap, and its
    /// sentence is the one worth reading when both halves are wrong.
    /// </summary>
    public static SupportVerdict CheckSupport(string? version, string? edition)
    {
        if (Is(edition, SimulatedEdition)) return SupportVerdict.Supported;

        if (System.Version.TryParse(version, out var build) == false)
            return new SupportVerdict(false, VersionUnsupportedReason,
                string.IsNullOrWhiteSpace(version)
                    ? $"No Ableton Live version was detected. {RequirementSummary}"
                    : $"\"{version}\" is not an Ableton Live version this tool can read. {RequirementSummary}");

        if (build < MinimumStemSeparationVersion)
            return new SupportVerdict(false, VersionUnsupportedReason,
                $"Ableton Live {build} has no stem separation. {RequirementSummary}");

        if (EditionIsKnownWithoutStemSeparation(edition))
            return new SupportVerdict(false, EditionUnsupportedReason,
                $"Ableton Live {build} {edition} cannot separate stems. {RequirementSummary}");

        return SupportVerdict.Supported;
    }

    private static bool EditionIsKnownWithoutStemSeparation(string? edition) =>
        EditionsWithoutStemSeparation.Any(candidate => Is(edition, candidate));

    private static bool Is(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The verdict of <see cref="LiveInstall.CheckSupport"/>: which half failed, and the sentence that
/// says so. Like every other refusal in this product, the reason is a stable token a log line can be
/// grepped for, and the sentence names what was actually seen rather than reciting the requirement
/// alone.
/// </summary>
/// <param name="IsSupported">False when the run must not start.</param>
/// <param name="Reason">
/// <see cref="LiveInstall.SupportedReason"/>, or the kebab token of the half that failed.
/// </param>
/// <param name="Sentence">What the user is shown. Empty when supported.</param>
public sealed record SupportVerdict(bool IsSupported, string Reason, string Sentence)
{
    public static readonly SupportVerdict Supported =
        new(true, LiveInstall.SupportedReason, string.Empty);

    /// <summary>
    /// The refusal as a failure row reads it: the sentence, then the token, so the reason survives
    /// the log line being copied into a bug report.
    /// </summary>
    public string FailureText => IsSupported ? string.Empty : $"{Sentence} ({Reason})";
}
