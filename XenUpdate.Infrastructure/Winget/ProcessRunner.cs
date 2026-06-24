using System.Diagnostics;
using System.Text;

namespace XenUpdate.Infrastructure.Winget;

/// <summary>
/// Spawns an external process, captures its stdout and stderr, and returns
/// a <see cref="ProcessExecutionResult"/>. This is the only place in the
/// entire codebase that calls <see cref="Process.Start()"/>.
/// </summary>
public class ProcessRunner
{
    /// <summary>
    /// Runs the given executable with the specified arguments in a hidden window
    /// and asynchronously waits for it to exit.
    /// </summary>
    /// <param name="executable">Program to run (e.g., "winget").</param>
    /// <param name="arguments">Command-line arguments (e.g., "upgrade --accept-source-agreements").</param>
    /// <param name="cancellationToken">If cancelled, the process is killed immediately.</param>
    /// <param name="outputProgress">
    /// Optional. When supplied, each stdout line is reported as it arrives (winget redraws
    /// progress with carriage returns, which <see cref="StreamReader.ReadLineAsync(CancellationToken)"/>
    /// splits into separate lines), enabling live progress parsing. When <c>null</c>, stdout is
    /// buffered in one read — the original behavior, used by callers that only need the final output.
    /// </param>
    /// <returns>A <see cref="ProcessExecutionResult"/> with stdout, stderr, and exit code.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the process cannot be started.</exception>
    public virtual async Task<ProcessExecutionResult> RunAsync(
        string executable,
        string arguments,
        CancellationToken cancellationToken,
        IProgress<string>? outputProgress = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdOutTask = ReadStandardOutputAsync(process.StandardOutput, outputProgress, cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore errors during emergency kill.
            }

            throw;
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return new ProcessExecutionResult(
            StandardOutput: stdOut,
            StandardError: stdErr,
            ExitCode: process.ExitCode);
    }

    // Without a progress sink we keep the original single buffered read.
    // With one, we stream line-by-line, reporting each line while still
    // reassembling the full text for the returned result.
    private static async Task<string> ReadStandardOutputAsync(
        StreamReader reader,
        IProgress<string>? outputProgress,
        CancellationToken cancellationToken)
    {
        if (outputProgress is null)
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }

        var builder = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            builder.AppendLine(line);
            outputProgress.Report(line);
        }

        return builder.ToString();
    }
}
