using StemForge.Core.Audio;
using StemForge.Core.Models;
using StemForge.Core.Support;
using StemForge.Core.Verify;

namespace StemForge.Core.Publish;

public sealed record PublishResult(
    bool Ok,
    ErrorKind Kind,
    string? Error,
    string? ResultFolder,
    IReadOnlyList<string> Files,
    bool AlreadySatisfied);

/// <summary>
/// Moves generated stems into the user's destination safely:
/// stage on the destination volume, verify there, then publish the result folder and
/// write the completion manifest last. A crash at any point leaves either no result
/// folder or a manifest marked incomplete, so a retry repairs rather than duplicates.
/// </summary>
public static class OutputPublisher
{
    public const string StagingFolderName = ".autostem-staging";

    public static string StagingRoot(string destinationRoot) =>
        Path.Combine(destinationRoot, StagingFolderName);

    /// <summary>
    /// True when the job's recorded result folder already holds a complete, intact,
    /// profile-matching result. Used on resume so verified work is not repeated.
    /// </summary>
    public static bool IsAlreadySatisfied(Job job, out string? folder)
    {
        folder = null;
        if (string.IsNullOrEmpty(job.ResultFolder) || string.IsNullOrEmpty(job.DestinationRoot))
            return false;

        var full = Path.Combine(job.DestinationRoot, job.ResultFolder);
        if (!Directory.Exists(full)) return false;

        var manifest = ManifestStore.Read(full);
        if (manifest is null) return false;
        if (!manifest.Matches(job.Profile, job.Source)) return false;
        if (!manifest.IsIntact(full)) return false;

        // Every requested stem must be present in the published set.
        var names = manifest.Files.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!job.Stems.Enumerate().All(s => names.Contains(s.OutputFileName())))
            return false;

        folder = full;
        return true;
    }

    public static PublishResult Publish(
        Job job,
        IReadOnlyDictionary<Stems, string> generatedFiles,
        WavInfo sourceInfo,
        VerificationOptions options,
        string? liveVersion,
        Action<string>? log = null)
    {
        job.Touch();

        if (string.IsNullOrWhiteSpace(job.DestinationRoot))
            return Fail(ErrorKind.DestinationMissing, "No output folder is set.");

        // 1. Destination must exist and be writable.
        try
        {
            Directory.CreateDirectory(job.DestinationRoot);
            var probe = Path.Combine(job.DestinationRoot, ".stemforge-write-test.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            return Fail(ErrorKind.DestinationUnwritable,
                $"Output folder is not writable: {job.DestinationRoot} ({ex.Message})");
        }

        // 2. Enough free space for staging + the published copy.
        long required = 0;
        foreach (var path in generatedFiles.Values)
        {
            try { required += new FileInfo(path).Length; } catch { /* reported during verify */ }
        }

        var space = CheckFreeSpace(job.DestinationRoot, required * 2);
        if (space is not null)
            return Fail(ErrorKind.DiskFull, space);

        var staging = Path.Combine(StagingRoot(job.DestinationRoot), job.Id);

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
        }
        catch (Exception ex)
        {
            return Fail(ErrorKind.DestinationUnwritable, $"Could not create staging folder: {ex.Message}");
        }

        // 3. Stage every requested stem under its published name.
        var staged = new Dictionary<Stems, string>();
        foreach (var stem in job.Stems.Enumerate())
        {
            if (!generatedFiles.TryGetValue(stem, out var source) || !File.Exists(source))
                continue;

            var stagedPath = Path.Combine(staging, stem.OutputFileName());
            try
            {
                File.Copy(source, stagedPath, overwrite: true);
                staged[stem] = stagedPath;
            }
            catch (Exception ex)
            {
                Cleanup(staging);
                return Fail(ErrorKind.PublishFailed, $"Could not copy {stem.DisplayName()} stem: {ex.Message}");
            }
        }

        log?.Invoke($"staged {staged.Count} file(s) in {staging}");

        // 4. Verify the staged copy (never trust the source of the copy).
        var report = StemVerifier.Verify(job.Stems, staged, sourceInfo, options);
        if (!report.Ok)
        {
            Cleanup(staging);
            return Fail(ErrorKind.StemsUnverified, report.Summary);
        }

        // 5. Resolve the destination folder, honouring a folder from a previous attempt.
        string folderName;
        string resultFolder;
        try
        {
            resultFolder = Naming.ResultFolderResolver.Resolve(
                job.DestinationRoot, Path.GetFileNameWithoutExtension(job.SourcePath),
                job.Profile, job.Source, out folderName, job.ResultFolder);
        }
        catch (Exception ex)
        {
            Cleanup(staging);
            return Fail(ErrorKind.PublishFailed, $"Could not resolve an output folder: {ex.Message}");
        }

        var manifest = new JobManifest
        {
            JobId = job.Id,
            ProfileKey = job.Profile.Key,
            Stems = job.Stems,
            Mode = job.Mode,
            SourcePath = job.SourcePath,
            SourceSizeBytes = job.SourceSizeBytes,
            SourceLastWriteUtcTicks = job.SourceLastWriteUtcTicks,
            SourceQuickHash = job.SourceQuickHash,
            LiveVersion = liveVersion,
            Complete = false,
        };

        try
        {
            Directory.CreateDirectory(resultFolder);

            // Mark the folder as ours *before* any file lands in it, so a crash here
            // is recognisable and re-usable by the next attempt.
            ManifestStore.Write(resultFolder, manifest);

            foreach (var stem in job.Stems.Enumerate())
            {
                var stemFile = report.Files.FirstOrDefault(f => f.Stem == stem);
                if (stemFile is null || !stemFile.Ok) continue;

                var from = Path.Combine(staging, stemFile.FileName);
                var to = Path.Combine(resultFolder, stemFile.FileName);
                File.Move(from, to, overwrite: true);

                manifest.Files.Add(new ManifestFile
                {
                    Name = stemFile.FileName,
                    Stem = stem.ToString(),
                    Bytes = stemFile.Bytes,
                    Sha256 = stemFile.Sha256,
                    DurationSeconds = stemFile.DurationSeconds ?? 0,
                    SampleRate = stemFile.Info?.SampleRate ?? 0,
                    Channels = stemFile.Info?.Channels ?? 0,
                });
            }

            // 6. Flip completion last: this is the commit point.
            manifest.Complete = true;
            manifest.CompletedUtc = DateTime.UtcNow;
            ManifestStore.Write(resultFolder, manifest);
        }
        catch (Exception ex)
        {
            Cleanup(staging);
            return Fail(ex is IOException io && IsDiskFull(io) ? ErrorKind.DiskFull : ErrorKind.PublishFailed,
                $"Could not publish results: {ex.Message}");
        }

        Cleanup(staging);

        job.ResultFolder = folderName;
        job.OutputFiles = manifest.Files.Select(f => f.Name).ToList();
        return new PublishResult(true, ErrorKind.None, null, resultFolder,
            manifest.Files.Select(f => Path.Combine(resultFolder, f.Name)).ToList(), false);

        PublishResult Fail(ErrorKind kind, string message) => new(false, kind, message, null, Array.Empty<string>(), false);
    }

    /// <summary>
    /// Publishes the verified subset of stems salvaged from a failed attempt.
    ///
    /// Each stem is verified on its own; anything that does not verify is dropped, so the
    /// files kept here are only ever ones that actually passed. The folder is marked
    /// <see cref="JobManifest.Partial"/>, which means it never satisfies a job, is never
    /// reused by a later complete run, and is never reported as finished output.
    /// </summary>
    public static PublishResult PublishPartial(
        Job job,
        IReadOnlyDictionary<Stems, string> generatedFiles,
        WavInfo sourceInfo,
        VerificationOptions options,
        string? liveVersion,
        string failureReason,
        Action<string>? log = null)
    {
        var failed = new PublishResult(false, ErrorKind.None, null, null, Array.Empty<string>(), false);
        if (generatedFiles.Count == 0) return failed;
        if (string.IsNullOrWhiteSpace(job.DestinationRoot)) return failed;

        // Verify each available stem independently.
        var verified = new Dictionary<Stems, StemFileVerification>();
        foreach (var (stem, path) in generatedFiles)
        {
            if (!File.Exists(path)) continue;

            var single = StemVerifier.Verify(stem, new Dictionary<Stems, string> { [stem] = path }, sourceInfo, options);
            var file = single.Files.FirstOrDefault();
            if (file is { Ok: true }) verified[stem] = file;
        }

        if (verified.Count == 0) return failed;

        var staging = Path.Combine(StagingRoot(job.DestinationRoot), job.Id + "-partial");

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            foreach (var (stem, file) in verified)
            {
                File.Copy(generatedFiles[stem], Path.Combine(staging, file.FileName), overwrite: true);
            }

            var folderName = ResolvePartialFolderName(job, sourceInfo);
            var folder = Path.Combine(job.DestinationRoot, folderName);
            Directory.CreateDirectory(folder);

            var manifest = new JobManifest
            {
                JobId = job.Id,
                ProfileKey = job.Profile.Key,
                Stems = job.Stems,
                Mode = job.Mode,
                SourcePath = job.SourcePath,
                SourceSizeBytes = job.SourceSizeBytes,
                SourceLastWriteUtcTicks = job.SourceLastWriteUtcTicks,
                SourceQuickHash = job.SourceQuickHash,
                LiveVersion = liveVersion,
                Partial = true,
                Complete = false,
            };

            ManifestStore.Write(folder, manifest);

            foreach (var (stem, file) in verified)
            {
                File.Move(Path.Combine(staging, file.FileName), Path.Combine(folder, file.FileName), overwrite: true);

                manifest.Files.Add(new ManifestFile
                {
                    Name = file.FileName,
                    Stem = stem.ToString(),
                    Bytes = file.Bytes,
                    Sha256 = file.Sha256,
                    DurationSeconds = file.DurationSeconds ?? 0,
                    SampleRate = file.Info?.SampleRate ?? 0,
                    Channels = file.Info?.Channels ?? 0,
                });
            }

            ManifestStore.Write(folder, manifest);

            job.PartialFolder = folderName;
            job.PartialFiles = manifest.Files.Select(f => f.Name).ToList();

            log?.Invoke($"kept {manifest.Files.Count} verified stem(s) as partial output ({failureReason})");

            return new PublishResult(true, ErrorKind.None, null, folder,
                manifest.Files.Select(f => Path.Combine(folder, f.Name)).ToList(), false);
        }
        catch (Exception ex)
        {
            log?.Invoke($"could not keep partial output: {ex.Message}");
            return failed;
        }
        finally
        {
            Cleanup(staging);
        }
    }

    /// <summary>Reuses this job's own partial folder when re-running, otherwise picks a free name.</summary>
    private static string ResolvePartialFolderName(Job job, WavInfo sourceInfo)
    {
        var baseName = PathSanitizer.SanitizeFolderName(Path.GetFileNameWithoutExtension(job.SourcePath));

        if (!string.IsNullOrEmpty(job.PartialFolder))
        {
            var existing = Path.Combine(job.DestinationRoot, job.PartialFolder);
            var manifest = Directory.Exists(existing) ? ManifestStore.Read(existing) : null;
            if (manifest is null || (manifest.Partial && manifest.Matches(job.Profile, job.Source)))
                return job.PartialFolder!;
        }

        var candidate = $"{baseName} (partial)";
        var counter = 2;
        while (Directory.Exists(Path.Combine(job.DestinationRoot, candidate)))
        {
            candidate = $"{baseName} (partial {counter})";
            counter++;
        }

        _ = sourceInfo;
        return candidate;
    }

    private static void Cleanup(string staging)
    {
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        catch
        {
            // Leftover staging is harmless: it is reused/cleaned on the next attempt.
        }
    }

    private static string? CheckFreeSpace(string destinationRoot, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destinationRoot));
            if (string.IsNullOrEmpty(root)) return null;
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return null;
            if (drive.AvailableFreeSpace < requiredBytes)
            {
                return $"Not enough free space on {root}: need about {requiredBytes / (1024 * 1024)} MB, " +
                       $"{drive.AvailableFreeSpace / (1024 * 1024)} MB available.";
            }
        }
        catch
        {
            // If the drive cannot be queried we let the copy itself report the problem.
        }
        return null;
    }

    private static bool IsDiskFull(IOException ex) =>
        ex.HResult == unchecked((int)0x80070070) || ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase);
}
