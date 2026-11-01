# External Services Detailed Design

## 1. Purpose

Starting, watching, and stopping work outside Unity, in one place (system design chapter 4), so that features do not start processes on their own.

Voice Lab is the first user.

Out of scope: fetching and updating environments ([Voice Lab detailed design](voice-lab.md), system design 4.19).

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Running a process that finishes once | ExternalServices (`ExternalProcessRunner`) |
| Starting, checking, and stopping a long-running service | ExternalServices (`ExternalServiceManager`) |
| Knowing which processes it started | ExternalServices (process ownership) |
| Saying what to start | Each feature's adapter |
| Finding the environment | VoiceLab |

## 3. Module Boundaries

- `VirtualVessel.ExternalServices` knows nothing of Python, RVC, or VoxCPM2. It handles executables, arguments, environment variables, and working folders.
- The streaming path does not reference it, and neither does Voice (system design 4.3).
- PIDs, ports, and paths do not reach the UI (system design 4.27), only Developer Mode's diagnostics.

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IExternalProcessRunner` | Interface | Runs a process that finishes once |
| `ExternalProcessRunner` | Class | Implementation; manages the process tree with a Job Object |
| `ExternalProcessRequest` | Class | Executable, arguments, environment, working folder, log location, timeout |
| `ExternalProcessResult` | Class | Exit code, start and end times, whether canceled |
| `IExternalServiceManager` | Interface | Manages long-running services (second stage) |
| `ServiceDescriptor` | Class | The items of system design 4.6 |
| `ServiceIdentityChecker` | Class | Checks that what answers on a port is genuine (second stage) |

## 5. Public API

```csharp
public interface IExternalProcessRunner
{
    /// Gives output line by line. Canceling stops the process tree.
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        Action<string> onOutputLine,
        CancellationToken cancellationToken);
}

public interface IExternalServiceManager
{
    Task<ServiceHandle> AcquireAsync(string serviceId, CancellationToken cancellationToken);
    IReadOnlyObservable<ServiceState> StateOf(string serviceId);
}
```

## 6. Data Model

`ServiceDescriptor` lives in code. Voice Lab's makeup is fixed per application version, so it needs no file.

Progress flows on a process's standard output as JSON Lines. Each adapter sets its format, and ExternalServices does not look inside.

```json
{"type":"progress","stage":"training","epoch":10,"total":150}
```

## 7. State and Lifecycle

A process is `Running`, `Completed`, `Failed`, or `Cancelled`.

Long-running services use the states of system design 4.7. They start when needed and stop after a time unused (system design 4.15, 4.16).

## 8. Important Sequences

### 8.1 Running a process

```text
Check the request (is the executable inside an allowed location?)
  ↓
Create a Job Object (if the application dies, children stop too)
  ↓
Start, and put it in the job
  ↓
Read stdout and stderr, write the log, pass each line on
  ↓
Finish or cancellation
  ↓
Return the result
```

### 8.2 Canceling

First close standard input and wait for it to end. If it does not within a time, stop the whole job. Only processes it started are stopped (system design 4.13).

### 8.3 Long-running services (second stage)

As in system design 4.8. A one-time token is passed at start, and health checks confirm the token and version. An open port alone is not trusted (CLAUDE.md chapter 26).

## 9. Threading and Async

A process's output is read on a dedicated thread, and `onOutputLine` is called from it. Passing to the UI is the caller's job. Nothing waits on the main thread.

## 10. Configuration

No user settings. Timeouts are given per request.

## 11. Storage

Logs go where the request says. It saves no state of its own.

With Job Objects, it has no need to look for processes left after a crash (system design 4.33).

## 12. Error Handling and Recovery

| What happened | Result |
|---|---|
| The executable is missing | `Failed` before starting |
| Outside the allowed locations | Refused |
| Timeout | The job stops; `Failed` |
| Exit code other than 0 | `Failed`; no automatic retry |

Turning this into meaningful messages is the adapter's job.

## 13. Logging and Diagnostics

- Start, end, exit code, and duration are recorded.
- A process's output goes to the file the request names, not the main log.
- Secrets such as tokens in environment variables are not logged (CLAUDE.md chapter 20).
- The Diagnostics screen shows running processes and the last result.

## 14. Testing Strategy

| Kind | Content |
|---|---|
| EditMode | Checking requests, state changes |
| Integration | With a small executable: reading output, exit codes, canceling, children stopping too |
| Manual | Killing the application leaves no child process |

## 15. Performance Constraints

It does not run during a stream, and managing processes costs almost nothing.

## 16. Directory / Namespace Structure

```text
Assets/VirtualVessel/ExternalServices/Runtime/
├─ Process/    IExternalProcessRunner, ExternalProcessRunner, Job Object
├─ Services/   IExternalServiceManager, ServiceDescriptor
└─ Identity/   ServiceIdentityChecker
```

## 17. Stages and Open Issues

Stages:

1. Processes that finish once (needed by activation and RVC training)
2. Long-running services (needed by VoxCPM2)

Open issues:

- Whether VoxCPM2 is a long-running service or a one-time process. Its model takes long to load, which favors a service when making several candidates. System design 13.18 has a service.
- Using .NET's `Process` and Job Objects from Unity's Mono needs a P/Invoke check.
