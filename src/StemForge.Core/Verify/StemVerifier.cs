using StemForge.Core.Audio;
using StemForge.Core.Models;
using StemForge.Core.Support;

namespace StemForge.Core.Verify;

/// <summary>Documented tolerances used when checking generated stems.</summary>
public sealed record VerificationOptions
{
    /// <summary>Fraction of the source duration a stem may differ by.</summary>
    public double DurationToleranceFraction { get; init; } = 0.005;

    /// <summary>Absolute floor for the tolerance, in seconds.</summary>
    public double DurationToleranceFloorSeconds { get; init; } = 0.05;

    /// <summary>Smallest plausible stem file, guarding against empty/truncated output.</summary>
    public long MinimumFileBytes { get; init; } = 1_000;

    public static VerificationOptions Default { get; } = new();

    /// <summary>
    /// Tolerance in seconds for a source of the given length. Stems produced by Live
    /// are frame-aligned to the source, so differences should be tiny; the tolerance
    /// exists to absorb container/padding rounding, and is documented in the test report.
    /// </summary>
    public double ToleranceSeconds(double sourceDurationSeconds) =>
        Math.Max(DurationToleranceFloorSeconds, sourceDurationSeconds * DurationToleranceFraction);
}

public sealed record StemFileVerification(
    Stems Stem,
    string FileName,
    long Bytes,
    string Sha256,
    WavInfo? Info,
    bool Ok,
    string? Problem)
{
    public double? DurationSeconds => Info?.DurationSeconds;
}

public sealed record StemVerificationReport(
    bool Ok,
    double SourceDurationSeconds,
    double ToleranceSeconds,
    IReadOnlyList<StemFileVerification> Files,
    IReadOnlyList<string> Problems)
{
    public string Summary => Ok
        ? $"{Files.Count} stem(s) verified (source {SourceDurationSeconds:F3}s, tolerance +/-{ToleranceSeconds:F3}s)"
        : string.Join("; ", Problems);
}

/// <summary>
/// Verifies that every requested stem is a readable WAV, is within the documented
/// duration tolerance of the source, and is aligned with its sibling stems.
/// The source file itself is only ever opened for reading.
/// </summary>
public static class StemVerifier
{
    public static StemVerificationReport Verify(
        Stems requested,
        IReadOnlyDictionary<Stems, string> generatedFiles,
        WavInfo sourceInfo,
        VerificationOptions options)
    {
        var tolerance = options.ToleranceSeconds(sourceInfo.DurationSeconds);
        var results = new List<StemFileVerification>();
        var problems = new List<string>();

        foreach (var stem in requested.Enumerate())
        {
            if (!generatedFiles.TryGetValue(stem, out var path) || !File.Exists(path))
            {
                problems.Add($"{stem.DisplayName()}: no file was produced");
                results.Add(new StemFileVerification(stem, stem.OutputFileName(), 0, string.Empty, null, false, "file missing"));
                continue;
            }

            var fileName = stem.OutputFileName();
            long bytes = 0;
            string sha = string.Empty;
            WavInfo? info = null;
            string? problem = null;

            try
            {
                bytes = new FileInfo(path).Length;
                if (bytes < options.MinimumFileBytes)
                {
                    problem = $"file is only {bytes} bytes";
                }
                else if (!WavReader.IsWav(path))
                {
                    problem = "not a RIFF/WAVE file";
                }
                else
                {
                    info = WavReader.ReadInfo(path);
                    var delta = Math.Abs(info.DurationSeconds - sourceInfo.DurationSeconds);
                    if (delta > tolerance)
                    {
                        problem = $"duration {info.DurationSeconds:F3}s differs from source {sourceInfo.DurationSeconds:F3}s by {delta:F3}s (tolerance {tolerance:F3}s)";
                    }
                    else if (info.SampleRate <= 0 || info.Channels <= 0)
                    {
                        problem = "invalid WAV format";
                    }
                }

                if (problem is null)
                {
                    sha = Hashing.Sha256File(path);
                }
            }
            catch (Exception ex)
            {
                problem = $"unreadable WAV: {ex.Message}";
            }

            if (problem is not null) problems.Add($"{stem.DisplayName()}: {problem}");

            results.Add(new StemFileVerification(stem, fileName, bytes, sha, info, problem is null, problem));
        }

        // Cross-stem alignment: all requested stems should share the source's length,
        // so a large spread between them means at least one was truncated or offset.
        var verified = results.Where(r => r.Info is not null).ToArray();
        if (verified.Length > 1)
        {
            var min = verified.Min(r => r.Info!.DurationSeconds);
            var max = verified.Max(r => r.Info!.DurationSeconds);
            if (max - min > tolerance)
            {
                problems.Add($"stems are not aligned with each other (spread {max - min:F3}s, tolerance {tolerance:F3}s)");
            }
        }

        return new StemVerificationReport(problems.Count == 0, sourceInfo.DurationSeconds, tolerance, results, problems);
    }
}
