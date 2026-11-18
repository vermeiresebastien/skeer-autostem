using StemForge.Core.Live;
using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// What the takeover does with the save dialog Live raises, measured on a dialog this test makes
/// for itself: the same automation tree a real one exposes - a labelled file-name field and a
/// confirm button - so the naming rules are exercised for real rather than described.
///
/// Every case also asserts that no input was injected: naming a dialog is a ValuePattern write and
/// a posted key or pattern Invoke, never the pointer or the keyboard.
/// </summary>
public sealed class SaveDialogNamingTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    private static readonly string Target = PrepareTarget();

    /// <summary>The dialog writes into the folder the value names, so the folder has to be there.</summary>
    private static string PrepareTarget()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "StemForge.Tests", "naming", "Untitled-20261116-101500.als");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        return path;
    }

    private static readonly string[] ExpectedReasons =
    {
        "ok", "no-edit-control", "value-not-set", "no-save-button", "enter-ignored", "dialog-still-open",
    };

    /// <summary>Shows a dialog of the given shape on its own thread and hands back its window.</summary>
    private static FakeFormHost Open(Func<FakeSaveDialog> create) => new(create);

    /// <summary>The dialog the host created, for asserting what it was asked to do.</summary>
    private static FakeSaveDialog DialogOf(FakeFormHost host) => (FakeSaveDialog)host.Form;

    private static TopLevelWindow WindowOf(FakeFormHost host) =>
        new(host.Handle, host.Form.Text, "FakeWindow", true);

    [Fact]
    public void TheTypedPathIsWrittenAndConfirmed()
    {
        var before = InputSimulator.InjectionAttempts;
        using var host = Open(() => new FakeSaveDialog(FakeSaveDialog.Confirm.WritesTheFile));
        var dialog = DialogOf(host);

        var attempt = LiveSessionTakeover.TryNameFileDialog(WindowOf(host), Target, Grace);

        Assert.Equal(SaveDialogOutcome.Ok, attempt.Outcome);
        Assert.Equal("ok", attempt.Reason);
        Assert.Equal(Target, dialog.WrittenPath);
        Assert.True(File.Exists(Target), $"no file at \"{Target}\"");
        Assert.Contains("File name:", attempt.Evidence);
        Assert.Contains("Save", attempt.Evidence);
        Assert.False(dialog.CancelPressed);
        Assert.False(dialog.EscapePressed);
        Assert.Equal(before, InputSimulator.InjectionAttempts);

        try { File.Delete(Target); } catch { }
    }

    [Fact]
    public void ADialogWithoutAFileNameFieldIsReportedAndLeftOpen()
    {
        var before = InputSimulator.InjectionAttempts;
        using var host = Open(() => new FakeSaveDialog(FakeSaveDialog.Confirm.WritesTheFile, hasEdit: false));
        var dialog = DialogOf(host);

        var attempt = LiveSessionTakeover.TryNameFileDialog(WindowOf(host), Target, Grace);

        Assert.Equal(SaveDialogOutcome.NoEditControl, attempt.Outcome);
        Assert.Equal("no-edit-control", attempt.Reason);
        Assert.Contains("edit=<none>", attempt.Evidence);
        Assert.True(WindowControl.IsVisible(host.Handle), "the dialog must be left open, not dismissed");
        Assert.False(dialog.CancelPressed);
        Assert.False(dialog.EscapePressed);
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public void AReadOnlyFileNameFieldIsReportedRatherThanTypedInto()
    {
        var before = InputSimulator.InjectionAttempts;
        using var host = Open(() => new FakeSaveDialog(FakeSaveDialog.Confirm.WritesTheFile, readOnlyField: true));
        var dialog = DialogOf(host);

        var attempt = LiveSessionTakeover.TryNameFileDialog(WindowOf(host), Target, Grace);

        Assert.Equal(SaveDialogOutcome.ValueNotSet, attempt.Outcome);
        Assert.Equal("value-not-set", attempt.Reason);
        Assert.True(WindowControl.IsVisible(host.Handle), "a refused value must not dismiss the dialog");
        Assert.False(dialog.CancelPressed);
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public void AConfirmButtonThatWillNotTakeAPressIsReportedAsNoSaveButton()
    {
        using var host = Open(() => new FakeSaveDialog(FakeSaveDialog.Confirm.Disabled, enterConfirms: false));

        var attempt = LiveSessionTakeover.TryNameFileDialog(WindowOf(host), Target, Grace);

        Assert.Equal(SaveDialogOutcome.NoSaveButton, attempt.Outcome);
        Assert.Equal("no-save-button", attempt.Reason);
        Assert.True(WindowControl.IsVisible(host.Handle));
    }

    [Fact]
    public void ADialogWithNoRecognisedButtonThatIgnoresEnterIsReportedAsEnterIgnored()
    {
        using var host = Open(() => new FakeSaveDialog(FakeSaveDialog.Confirm.None, enterConfirms: false));

        var attempt = LiveSessionTakeover.TryNameFileDialog(WindowOf(host), Target, Grace);

        Assert.Equal(SaveDialogOutcome.EnterIgnored, attempt.Outcome);
        Assert.Equal("enter-ignored", attempt.Reason);
        Assert.True(WindowControl.IsVisible(host.Handle), "an ignored Enter must not dismiss the dialog");
    }

    [Fact]
    public void ASaveButtonThatDoesNothingIsReportedAsDialogStillOpen()
    {
        using var host = Open(() => new FakeSaveDialog(FakeSaveDialog.Confirm.Inert, enterConfirms: false));

        var attempt = LiveSessionTakeover.TryNameFileDialog(WindowOf(host), Target, Grace);

        Assert.Equal(SaveDialogOutcome.DialogStillOpen, attempt.Outcome);
        Assert.Equal("dialog-still-open", attempt.Reason);
        Assert.True(WindowControl.IsVisible(host.Handle));
    }

    [Fact]
    public void ADialogWithoutARecognisedButtonStillSavesWhenItAnswersThePostedEnter()
    {
        var before = InputSimulator.InjectionAttempts;
        using var host = Open(() => new FakeSaveDialog(FakeSaveDialog.Confirm.None, enterConfirms: true));
        var dialog = DialogOf(host);

        var attempt = LiveSessionTakeover.TryNameFileDialog(WindowOf(host), Target, Grace);

        Assert.Equal(SaveDialogOutcome.Ok, attempt.Outcome);
        Assert.Equal(Target, dialog.WrittenPath);
        Assert.True(File.Exists(Target));
        Assert.False(dialog.CancelPressed);
        Assert.Equal(before, InputSimulator.InjectionAttempts);

        try { File.Delete(Target); } catch { }
    }

    [Fact]
    public void EveryOutcomeHasItsOwnGreppableReasonCode()
    {
        var reasons = Enum.GetValues<SaveDialogOutcome>()
            .Select(outcome => new SaveNamingAttempt(outcome, string.Empty).Reason)
            .ToList();

        Assert.Equal(ExpectedReasons.Length, reasons.Count);
        Assert.Equal(ExpectedReasons.OrderBy(r => r), reasons.OrderBy(r => r));
        Assert.Equal(reasons.Count, reasons.Distinct().Count());
        Assert.All(reasons, reason => Assert.Matches("^[a-z]+(-[a-z]+)*$", reason));
    }
}
