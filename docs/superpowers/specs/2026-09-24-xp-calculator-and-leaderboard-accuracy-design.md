# XP Calculator and Leaderboard Accuracy Design

**Status:** User confirmed the scope and scoring rule on 2026-09-24.

## Goal

Leave the XP Calculator's target-level input blank until the user enters a level, and make leaderboard XP gains reliable under normal multi-profile sampling and heartbeat renewal.

## XP Calculator behavior

- The **TARGET LEVEL** field in the **XP calc** tab starts blank when an account profile loads. The calculator does not choose the next level on the user's behalf.
- The active class and current level remain visible. XP remaining, target absolute XP, levels remaining, and the transition list have no calculated value until a valid target is entered.
- A manually entered target is preserved and recalculated when the same account's profile refreshes. Switching accounts clears the target. Clearing the field removes the previous projection immediately; invalid input does not leave an old projection visible.
- The XP tracker's automatic time-to-next-level estimate is outside this change.

## Leaderboard behavior

- Preserve the existing leaderboard rule: score the sum of valid gains across **every class** on one public player profile. Session XP still measures only the active class since that local session began.
- Preserve daily, ISO-weekly, and monthly UTC windows. A gain is attributed to the UTC time of the successful server observation. The first sample establishes a zero-gain baseline; invalid or uncertain intervals are not guessed or backfilled.
- When multiple profiles are continuously active and all fetches succeed, ordinary queue delay must not be treated as an uncertain sampling gap. The collector must continue respecting the configured minimum spacing between public-profile requests.
- A genuine fetch failure, expired active lease, deactivation, or resumed participation after a gap still requires a new baseline. A transient local failure while saving a desktop heartbeat must not permanently stop future renewal attempts.
- Existing totals remain visible after deactivation. A cached leaderboard page clearly indicates its last update, so a cached value is not mistaken for the latest score.

## Evidence and acceptance

`XpTrackingSession` compares the active class, while `LeaderboardSamplingService` compares all classes. The collector currently skips a gain if the prior sample is more than three configured intervals old, although its sequential queue can revisit a player after that threshold with three or more active profiles. `LeaderboardCoordinator.RunRenewalLoopAsync` currently stops if a renewal call throws an exception other than cancellation. `EfLeaderboardStore.ApplyHeartbeatAsync` intentionally marks a player for a new baseline after an active lease lapses or the last active installation deactivates.

Acceptance requires focused tests for the blank/manual target lifecycle, multi-profile queue timing, genuine stale gaps, heartbeat renewal recovery, and expected deactivation behavior. Include a same-window case where local session XP would exceed server XP under the current queue cutoff, and verify that the fix credits the valid server interval. A fresh leaderboard comparison must distinguish all-class gains from active-class session XP and account for the UTC period, first baseline, and page freshness.
