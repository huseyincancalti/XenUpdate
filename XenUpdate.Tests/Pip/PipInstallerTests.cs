using Xunit;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Pip;
using XenUpdate.Infrastructure.Winget;

namespace XenUpdate.Tests.Pip;

/// <summary>
/// Tests for <see cref="PipInstaller"/>.
/// </summary>
public sealed class PipInstallerTests
{
    [Fact]
    public void BuildUpgradeArguments_UsesExpectedFlags()
    {
        var arguments = PipInstaller.BuildUpgradeArguments("requests");

        Assert.Equal("install --upgrade \"requests\"", arguments);
    }

    [Fact]
    public async Task InstallUpdateAsync_ReturnsTrue_WhenPipSucceeds()
    {
        var runner = new FakeProcessRunner(new ProcessExecutionResult("Successfully installed requests-2.32.0", string.Empty, 0));
        var logger = new FakeLoggerService();
        var installer = new PipInstaller(runner, logger);
        var item = new PipPackageItem
        {
            DisplayName = "requests",
            PackageName = "requests"
        };

        var success = await installer.InstallUpdateAsync(item, new Progress<InstallProgress>(), CancellationToken.None);

        Assert.True(success);
        Assert.Equal("pip", runner.LastExecutable);
        Assert.Equal("install --upgrade \"requests\"", runner.LastArguments);
    }

    [Fact]
    public async Task InstallUpdateAsync_ReturnsFalse_WhenPipFails()
    {
        var runner = new FakeProcessRunner(new ProcessExecutionResult(string.Empty, "ERROR: No matching distribution found", 1));
        var logger = new FakeLoggerService();
        var installer = new PipInstaller(runner, logger);
        var item = new PipPackageItem
        {
            DisplayName = "numpy",
            PackageName = "numpy"
        };

        var success = await installer.InstallUpdateAsync(item, new Progress<InstallProgress>(), CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task InstallUpdateAsync_OnFailure_ReportsErrorLineFromStderr()
    {
        var runner = new FakeProcessRunner(new ProcessExecutionResult(
            string.Empty,
            "Some log noise\nERROR: No matching distribution found for numpy==2.0.0",
            1));
        var logger = new FakeLoggerService();
        var installer = new PipInstaller(runner, logger);
        var item = new PipPackageItem { DisplayName = "numpy", PackageName = "numpy" };

        string? reportedReason = null;
        var progress = new Progress<InstallProgress>(p =>
        {
            if (p.FailureReason is not null)
                reportedReason = p.FailureReason;
        });

        await installer.InstallUpdateAsync(item, progress, CancellationToken.None);

        Assert.Equal("ERROR: No matching distribution found for numpy==2.0.0", reportedReason);
    }

    [Fact]
    public async Task InstallUpdateAsync_ThrowsWhenCancelled()
    {
        var runner = new FakeProcessRunner(new OperationCanceledException());
        var logger = new FakeLoggerService();
        var installer = new PipInstaller(runner, logger);
        var item = new PipPackageItem { DisplayName = "requests", PackageName = "requests" };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            installer.InstallUpdateAsync(item, new Progress<InstallProgress>(), CreateCancelledToken()));
    }

    [Fact]
    public async Task InstallUpdateAsync_RejectsUnsafePackageName_WithoutRunningPip()
    {
        var runner = new FakeProcessRunner(new ProcessExecutionResult("ok", string.Empty, 0));
        var installer = new PipInstaller(runner, new FakeLoggerService());
        var item = new PipPackageItem
        {
            DisplayName = "Evil",
            PackageName = "foo; rm -rf /"
        };

        var success = await installer.InstallUpdateAsync(item, new Progress<InstallProgress>(), CancellationToken.None);

        Assert.False(success);
        Assert.Equal(string.Empty, runner.LastExecutable);
    }

    private static CancellationToken CreateCancelledToken()
    {
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        return cancellationTokenSource.Token;
    }

    private sealed class FakeProcessRunner : ProcessRunner
    {
        private readonly ProcessExecutionResult? _result;
        private readonly Exception? _exception;

        public FakeProcessRunner(ProcessExecutionResult result)
        {
            _result = result;
        }

        public FakeProcessRunner(Exception exception)
        {
            _exception = exception;
        }

        public string LastExecutable { get; private set; } = string.Empty;

        public string LastArguments { get; private set; } = string.Empty;

        public override Task<ProcessExecutionResult> RunAsync(
            string executable,
            string arguments,
            CancellationToken cancellationToken,
            IProgress<string>? outputProgress = null)
        {
            LastExecutable = executable;
            LastArguments = arguments;

            if (_exception is not null)
            {
                return Task.FromException<ProcessExecutionResult>(_exception);
            }

            return Task.FromResult(_result!);
        }
    }

    private sealed class FakeLoggerService : ILoggerService
    {
        public event Action<LogEntry>? LogEntryAdded
        {
            add { }
            remove { }
        }

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message, Exception? ex = null)
        {
        }
    }
}
