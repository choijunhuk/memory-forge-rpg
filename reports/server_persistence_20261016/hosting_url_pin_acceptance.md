# Hosting URL preference — narrow follow-up acceptance

Parent implementation: `7e38544bacafa84e4bc6eb4caa85bf2e4a0e88ea`. This supplements, rather than replaces, the first local-server acceptance and its limits.

Root's final independent startup probe found a distinct path not covered by the endpoint-reload regression: `MemoryForge:PracticePort=53122` with `--preferHostingUrls=true --urls=http://127.0.0.1:53123` actually bound 53123. `hosting_url_pin_red.json` preserves the observed failure. Only safe loopback addresses were used; no public/wildcard socket was opened. The framework's [hosting URL preference](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host?view=aspnetcore-10.0#prefer-hosting-urls) is a separate control from Kestrel's reloadable endpoint loader.

Official Opus High made the narrow correction in the same separate worktree/session, with unchanged subscription-only controls. Author execution remains NOT_RUN. Codex reviewed the exact delta: explicitly `PreferHostingUrls(false)` before the single Listen, and a post-start check that the one loopback address matches the configured nonzero port (zero retains server-selected ephemeral semantics). No Core, rule, data, price, SQL/OpenAPI, dependency, auth or storage behavior changed.

The existing BCL suite adds a required scenario with command-line and environment overrides. Both must serve an authenticated request at the configured port and leave the conflicting loopback port closed. Existing scenarios and privacy checks remain intact.

Fresh Root results:

- Server build: **0 warnings /0 errors**, `hosting_url_pin_build.txt`.
- Exact CI command: **PASS246 assertions**, `hosting_url_pin_checks.txt`. Counts vary with the legitimate stochastic gameplay path; this is not a fixed test-count target.
- Separate original HTTP/process boundary suite: **PASS24**, `hosting_url_pin_http24.json`.
- Independent actual startup/socket/authentication probes: **PASS2/2**, command line and environment, `hosting_url_pin_green.json`. Both authenticate at the configured port and find the conflicting port closed; actual server assembly hash is included.
- Imported four files match the author, `hosting_url_pin_import.json`; actual sole Opus/High metadata is in `hosting_url_pin_cli.json`. Root source hashes and preservation are recorded separately for this follow-up.

The first server unit's partial production/DB/account/Unity/manual-candidate/service-coverage/power-loss limits all remain. This is not production or physical-device acceptance. The exact corrected commit's remote CI result must be checked separately from the parent commit.
