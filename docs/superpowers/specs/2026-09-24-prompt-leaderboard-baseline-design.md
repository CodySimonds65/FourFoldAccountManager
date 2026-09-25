# Prompt server-verified leaderboard baseline

## Intent

When a user starts participating in the leaderboard, establish the first XP baseline as soon as the collector's approved request interval permits. This narrows the period in which the desktop session can gain XP before the leaderboard has any baseline. "Startup" means an opted-in profile becomes active, including client launch, account activation, and renewal after an expired activity lease. A routine heartbeat for an already active profile must not reset its baseline.

## Design

The desktop continues to send installation, linked profile, and active player IDs through the existing participation heartbeat. It does not send an XP value. The server selects profiles that are currently active and have no sample state or require a new baseline. During a sampling pass, the collector requests one of these profiles before the next scheduled routine profile, and checks again between routine requests. This includes profiles activated after a pass began. All requests use the existing public-site fetcher and the configured minimum interval between requests.

The first successful public-site response becomes a zero-gain baseline through the existing observation path. Later server observations calculate all-class XP deltas. Failed fetches, username mismatches, malformed profile data, opt-out, and expired activity leases retain their current no-score or rebaseline behavior. The server never treats client-provided XP as authoritative.

The baseline priority applies across worker scopes and service instances because pending status is read from persisted participation and sample state, not from an in-memory activation queue. It gives a new profile the next available collection slot, subject to other pending baselines and the approved request interval; it does not promise an instantaneous fetch or reconstruct XP gained before that response.

## Verification

Tests demonstrate that a new profile is fetched before routine queued profiles, that a profile activated mid-pass is collected before remaining routine work, that requests remain spaced, and that repeated heartbeats do not reset an established baseline. Existing failure, opt-out, and stale-gap tests remain green. The PostgreSQL query is covered by a store integration test when Docker is available.

## Revision: immediate launch baseline and per-player cadence

Confirmed by the user on 2026-09-24 after session XP continued to exceed leaderboard XP. Two gaps remained. First, the desktop session starts from its own website read at launch, while the server baseline waited for the next global request slot, which could be up to five minutes away. Second, `MinimumSampleInterval` spaced every request across all players, so with N active profiles each player was sampled only every 5N minutes, and a single failed fetch lost that whole span.

- A participation heartbeat with active profiles wakes the sampling worker immediately (`LeaderboardSamplingSchedule.RequestSample`). Profiles awaiting a baseline are read first, so the server baseline is taken within seconds of launch. The server still reads the public site itself and never accepts client-reported XP.
- `MinimumSampleInterval` is now the per-player cadence. The worker polls every 15 seconds for players whose last sample is at least one interval old, oldest first. `RequestSpacing` (default two seconds) separates consecutive site requests.
- A failed or rejected read requests a rebaseline and defers that player for one interval, so a site outage cannot turn into rapid retries.
- The pass-timing allowance (`continuousGap`) is removed. With per-player cadence, a normal gap stays near one interval, and the three-interval stale rule applies unchanged.
