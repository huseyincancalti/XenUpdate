namespace XenUpdate.Infrastructure.Winget;

/// <summary>
/// Holds the captured result of running an external process.
/// </summary>
/// <param name="StandardOutput">All text written to stdout.</param>
/// <param name="StandardError">All text written to stderr.</param>
/// <param name="ExitCode">The process exit code. 0 typically means success.</param>
public sealed record ProcessExecutionResult(
    string StandardOutput,
    string StandardError,
    int ExitCode)
{
    /// <summary>True when the exit code is 0 (conventional success).</summary>
    public bool Succeeded => ExitCode == 0;
}
