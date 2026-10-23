# R1a host-check reuse/refactor plan

Requirement: actual C# commands from fresh Create must match the JS full-state oracle and supported prefixes of the1000legacy traces; no restored expected state and no fake complete-run claims.

1. Preserve existing R0 strict-container behaviour with all115corruption probes. Split only its existing standard-library gzip/UTF8 byte decoding from JSON-document parsing, exposing a minimal internal byte reader and duplicate-key validator for host checks. No new package or generic validation framework.
2. Add an empty-gzip-prefix concatenation regression to the existing legacy trace reader tests first. Root must execute the test-only change on the old reader and retain actual RED. Reuse the strict shared byte reader for that old JSONL path, retain CRC/ISIZE/UTF8 checks, then execute GREEN. Do not weaken any current probe.
3. Add independent RunProgressionChecks against agreed Core APIs, full states/returns/queries/log/RNG for the five-case source oracle and every supported command prefix of1000legacy traces. Validate all oracle pins and records; count actual attempts separately from fully completed runs. State setup is only source input/M0 migration, never expected snapshots.
4. Wire Program once. Existing entire C# checks stay active. Root reviews the diff, compiles/runs, checks mismatch/ownership controls and actual Unity before integration. Core implementation in A and host verification in B own disjoint files.

Root measured note: .NET10 GZipStream with zero-length writes produces0bytes here, not a separate member. Do not use that as an empty-prefix test. Node standard gzipSync(Buffer.alloc(0),{level:9}) produces a real20-byte empty member, independently readable as empty through native GZipStream. Use an equivalent valid20-byte RFC1952 member and assert physical length/native empty decode before concatenation; otherwise the negative control is invalid.
