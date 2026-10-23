Run from any directory with .NET 10 and a C compiler:

```sh
bash tools/tests/AndroidOutboxProbe/run.sh
```

`DOTNET` can name an existing dotnet executable. `NewtonsoftPath` is an optional MSBuild override; the default uses the SDK's already installed Newtonsoft assembly. No NuGet dependency or Unity Editor is required. Build/native outputs and every tested outbox live in test-owned temporary directories and are removed after the run.

The project compiles the actual client sources with `UNITY_ANDROID` and without `UNITY_EDITOR`. Its test-only libc bridge uses real POSIX files, independent opens, same-process and child-process flock contention, fchmod, partial writes, fsync and close. It rejects handles that did not originate in native open. This catches the original managed-handle backend: Unity IL2CPP returns an opaque FileHandle pointer, which cannot be supplied as a native fd. No opaque pointers are inspected. On macOS the bridge translates Linux/Android open flags and EWOULDBLOCK to host values. Fault injection verifies fail-closed behavior and exact existing-file preservation.

On Linux the script also runs two direct checks against system libc, with the production Android flags and no bridge: exact-body/private-mode publish/reload/confirm, and same-/separate-process native lock contention. This is Linux-host evidence. Neither host mode establishes Android bionic/IL2CPP or physical-device acceptance; a normal development APK must still be checked on the actual device.

A lock close failure can follow a successful publish. Stage then returns no sendable request and reports OUTBOX_LOCK_FAILED; Load resolves the exact pending state. Linux close is never retried, including EINTR, because the descriptor may already have been released and reused.
