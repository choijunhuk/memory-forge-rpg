# Root lock prerequisite, 2026-10-17 KST

Actual Unity6000.0.46f1 EditorMono and installed bundled Mono6.13 OS processes: FileShare.None alone allowed another Mono process to open the same inode; .NET10 also opened it. Native checked flock(LOCK_EX|LOCK_NB) on a permanent descriptor then blocked all three runtimes. Close and actual SIGKILL both allowed reacquisition, with the same lock inode. These are primitive probes, not component acceptance. The first native .NET probe had an unhandled expected FileStream-open contention exception; its test harness was corrected to report refusal, and the complete matrix passed. Product source was untouched.

Root interrupted original OpusMax task after these facts invalidated its FileShare.None-only premise, before any source/test/report edit (0 bytes). Original result remains FAILED/error_during_execution, process_exit0, and actual27 positive OpusMax records; it was NOT a subscription/model/billing limit and was not completed. Narrow same-session xhigh source continuation follows. No model/account/paid workaround.

Primary source review agrees with the measurement; public source is not an installed-binary revision assertion:
- Unity Mono process-local file_share_table: https://github.com/Unity-Technologies/mono/blob/unity-main/mono/metadata/w32file-unix.c#L213
- .NET10 FileShare locking uses best-effort flock: https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs#L311
- Apple native flock: https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/flock.2.html
- Managed FileStream.Lock is unsupported on macOS: https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.lock?view=net-10.0

Minimum tested native primitive: DllImport("libc",SetLastError=true) int flock(int fd,int operation); acquire operation6 (EX2|NB4), errno4 retries bounded; errno11/35 contention. Keep the FileStream and fd alive through the whole operation; close releases OS lock. Existing FileShare.None also protects same-process accesses. Hold per outbox critical operation, not across the component lifetime, and never unlink the permanent .lock file. Unsupported/native errors fail closed. No new package, engine, rule or record reset. Root will execute the final component independently.
