using System.Runtime.InteropServices;

namespace StemForge.Core.Interop;

/// <summary>
/// Requests that Windows stays awake for the duration of an active run and releases
/// the request when the run ends. Only the display/system sleep is suppressed; lock
/// and security settings are deliberately left under the user's control.
/// </summary>
public sealed class SystemAwake : IDisposable
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(ExecutionState esFlags);

    private readonly ExecutionState _previous;

    public bool Active { get; }
    public string? FailureReason { get; }

    public SystemAwake(bool keepDisplayOn = false)
    {
        try
        {
            var desired = ExecutionState.Continuous | ExecutionState.SystemRequired;
            if (keepDisplayOn) desired |= ExecutionState.DisplayRequired;

            _previous = (ExecutionState)SetThreadExecutionState(desired);
            Active = true;
        }
        catch (Exception ex)
        {
            Active = false;
            FailureReason = ex.Message;
        }
    }

    public void Dispose()
    {
        try
        {
            SetThreadExecutionState(_previous | ExecutionState.Continuous);
        }
        catch
        {
            // Best effort: the OS releases the request when the process exits anyway.
        }
    }
}
