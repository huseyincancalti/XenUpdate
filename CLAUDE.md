# XenUpdate — standing engineering rules

## Process must actually exit on every close/exit path — no exceptions

XenUpdate is a tray-resident WPF app. A graceful `Application.Shutdown()` +
`IDisposable` cleanup is **not sufficient** to guarantee the process actually
terminates. Any library or service that subscribes to an OS-level notification
API can spin up a dedicated, **non-background** thread — and a single live
foreground thread anywhere is enough to keep the whole process alive in Task
Manager indefinitely, invisible, even after every window has closed and every
registered DI service has been disposed.

Two confirmed repeat offenders found in production testing of the actual
published self-contained EXE (not just `dotnet run`/Debug, which can mask
this):

1. **H.NotifyIcon's `TaskbarIcon`** — owns a native hidden window for tray
   messages. Must be explicitly `.Dispose()`d in `App.xaml.cs OnExit`.
2. **`System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged`**
   — backed by a dedicated OS-notification thread in the .NET networking
   stack. `ShellViewModel`'s `NetworkMonitorService` subscribes to this; it
   must be disposed (`ShellViewModel` implements `IDisposable` for exactly
   this reason, and is a DI singleton so the container calls it automatically).

**The rule going forward:** `App.xaml.cs OnExit` ends with an unconditional
`Environment.Exit(0)` as the very last line, after all graceful cleanup.
Do not remove it, do not gate it behind a condition, even if a specific known
leak looks fixed — the next library or service added to this app is not
guaranteed to be well-behaved about background threads, and this bug is easy
to miss because it only shows up as "quietly still running in Task Manager,"
not a crash or visible error.

**Always test the actual published self-contained EXE** (`dotnet publish` via
the `FolderProfile` publish profile) for exit-path bugs like this, not just a
Debug build — self-contained/single-file builds have been where this
specific class of bug actually surfaced in testing.

## A `Window.OnClosing` override that unconditionally cancels can block exit too

Third confirmed offender, found the same way as the first two (user reports
"still running, had to kill it from Task Manager"): a reusable popup window
(`UpdateQueueWindow`) overrode `OnClosing` to always set `e.Cancel = true` and
`Hide()`, so clicking its own close button hides it instead of tearing down
the WPF `Window` (cheap reuse across repeated `Show()` calls, avoiding a
rebuild of its visual tree every time).

That's a legitimate pattern **only if it distinguishes "the user dismissed
this one popup" from "the whole app is shutting down."** An unconditional
cancel does not — and `Application.Shutdown()` closes every window in
`Application.Windows` (including hidden ones still tracked there) as part of
its own sequence. If *any* window cancels that close, WPF aborts the whole
shutdown. `App.xaml.cs OnExit` — and the hard `Environment.Exit(0)` inside it
— never even runs, silently, with no exception and no log line, because the
shutdown never got that far.

**The rule going forward:** never override `OnClosing` on a window to
unconditionally cancel. If a window needs "close button hides instead of
destroying it" behavior, wire that directly into the close *button's* click
handler calling `Hide()` — bypassing `Closing` entirely for that one path —
and leave `OnClosing` itself alone so a real `Close()` (Alt+F4, or the app
shutting down) always succeeds. If the window is cached and reused
(`_window ??= new Window(...)`), subscribe to its `Closed` event to null out
the cached reference — otherwise a legitimate close (e.g. Alt+F4) leaves a
stale reference that throws on the next reuse attempt.
