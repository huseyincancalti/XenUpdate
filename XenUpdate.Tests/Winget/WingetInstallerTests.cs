using Xunit;
using XenUpdate.Core.Interfaces;
using XenUpdate.Core.Models;
using XenUpdate.Infrastructure.Winget;

namespace XenUpdate.Tests.Winget;

/// <summary>
/// Tests for <see cref="WingetInstaller"/>.
/// </summary>
public sealed class WingetInstallerTests
{
    [Fact]
    public void BuildUpgradeArguments_UsesExpectedFlags()
    {
        var arguments = WingetInstaller.BuildUpgradeArguments("Microsoft.VisualStudioCode");

        Assert.Equal(
            "upgrade --id \"Microsoft.VisualStudioCode\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            arguments);
    }

    [Fact]
    public async Task InstallUpdateAsync_ReturnsTrue_WhenWingetSucceeds()
    {
        var runner = new FakeProcessRunner(new ProcessExecutionResult("done", string.Empty, 0));
        var logger = new FakeLoggerService();
        var installer = new WingetInstaller(runner, logger);
        var item = new AppUpdateItem
        {
            DisplayName = "Visual Studio Code",
            WingetPackageId = "Microsoft.VisualStudioCode"
        };

        var success = await installer.InstallUpdateAsync(item, new Progress<int>(), CancellationToken.None);

        Assert.True(success);
        Assert.Equal("winget", runner.LastExecutable);
        Assert.Equal(
            "upgrade --id \"Microsoft.VisualStudioCode\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            runner.LastArguments);
    }

    [Fact]
    public async Task InstallUpdateAsync_ReturnsFalse_WhenWingetFails()
    {
        var runner = new FakeProcessRunner(new ProcessExecutionResult(string.Empty, "boom", 1));
        var logger = new FakeLoggerService();
        var installer = new WingetInstaller(runner, logger);
        var item = new AppUpdateItem
        {
            DisplayName = "Google Chrome",
            WingetPackageId = "Google.Chrome"
        };

        var success = await installer.InstallUpdateAsync(item, new Progress<int>(), CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task InstallUpdateAsync_ReturnsFalse_WhenWingetTimesOut()
    {
        var runner = new FakeProcessRunner(new OperationCanceledException());
        var logger = new FakeLoggerService();
        var installer = new WingetInstaller(runner, logger);
        var item = new AppUpdateItem
        {
            DisplayName = "PowerToys",
            WingetPackageId = "Microsoft.PowerToys"
        };

        var success = await installer.InstallUpdateAsync(item, new Progress<int>(), CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task InstallUpdateAsync_ThrowsWhenCancelled()
    {
        var runner = new FakeProcessRunner(new OperationCanceledException());
        var logger = new FakeLoggerService();
        var installer = new WingetInstaller(runner, logger);
        var item = new AppUpdateItem
        {
            DisplayName = "PowerToys",
            WingetPackageId = "Microsoft.PowerToys"
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            installer.InstallUpdateAsync(item, new Progress<int>(), CreateCancelledToken()));
    }

    [Fact]
    public async Task InstallUpdateAsync_RejectsUnsafePackageId_WithoutRunningWinget()
    {
        var runner = new FakeProcessRunner(new ProcessExecutionResult("ok", string.Empty, 0));
        var installer = new WingetInstaller(runner, new FakeLoggerService());
        var item = new AppUpdateItem
        {
            DisplayName = "Evil",
            WingetPackageId = "Foo --uninstall Bar"
        };

        var success = await installer.InstallUpdateAsync(item, new Progress<int>(), CancellationToken.None);

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
            CancellationToken cancellationToken)
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
