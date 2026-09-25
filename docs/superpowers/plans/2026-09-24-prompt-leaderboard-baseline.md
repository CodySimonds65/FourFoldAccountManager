# Prompt leaderboard baseline implementation plan

## Goal and files

Give newly active leaderboard profiles a server-verified baseline at the next approved public-profile request slot. Preserve the existing heartbeat contract and all-class scoring. Work in `fix/xp-calculator-leaderboard-accuracy`.

Files: `src/FourFoldAccountManager.Leaderboard.Service/Data/ILeaderboardStore.cs`, `EfLeaderboardStore.cs`, `Collection/LeaderboardSamplingService.cs`, their leaderboard tests, and `README.md`.

## Task 1: Expose pending baseline profiles

1. Add `GetActiveProfilesNeedingBaselineAsync(DateTimeOffset activeAfterUtc, CancellationToken ct)` to `ILeaderboardStore`. Implement it in `EfLeaderboardStore` using the same active-lease and sharing filters as `GetActiveProfilesAsync`, selecting profiles whose sample state is absent or marked `NeedsBaseline`; deduplicate player IDs and retain the freshest active username.
2. Add a PostgreSQL store test: new active profile appears; after a baseline observation it disappears; a repeated active heartbeat leaves it absent; deactivation excludes it; reactivation marks it pending. Run the test (Docker required) and record whether the environment can execute it.
3. Update the in-memory test stores to implement the new query, then build. Commit the interface and store work.

## Task 2: Give baselines the next available sampler slot

1. Add sampler tests that fail under the current order: an active player without a baseline and a high ID is fetched before routine players; a new player activated during an earlier fetch is collected before the remaining routine players. Add a timing test that covers a priority request followed by a routine request at the approved interval.
2. In `RunOnceAsync`, query pending baselines before each routine profile. Fetch at most one previously unattempted pending baseline before that routine profile, using the existing `SampleAsync` validation and active-lease recheck. Skip its later routine turn if already sampled. Keep at least `MinimumSampleInterval` between actual fetches and preserve the completed-pass timing schedule used to distinguish normal queue delay from worker gaps.
3. Run the focused tests red then green, the non-Docker leaderboard suite, Core and Desktop suites, and the Release solution build. Commit the sampler change.

## Task 3: Document and deliver

1. Update the README to say the first server-verified observation is a zero-gain baseline, now prioritized at the next request slot after participation starts; prior XP is not backfilled, and client-reported XP is not accepted.
2. Inspect the branch diff, run `git diff --check`, and commit documentation. Record Docker-dependent tests separately if the daemon is unavailable. Provide the changed server behavior and an updated standalone Windows test EXE, while noting that the leaderboard behavior requires deployment of the service.
