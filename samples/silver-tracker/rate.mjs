// Session, rate and goal maths for the Silver tracker. Nothing here touches the page or window.fourfold, so
// .check/rate.check.mjs can run it with Node.

const HOUR_MS = 3600000;

// One account's session: what has been seen since tracking started. Times are milliseconds since the epoch.
export function createSession() {
  return { startBalance: null, lastBalance: null, lastAt: null, intervals: [], earned: 0, missed: false };
}

// Takes one answer from fourfold.profile.get.
export function applyRead(session, read) {
  const at = typeof read.updatedAt === 'string' ? Date.parse(read.updatedAt) : NaN;
  if (read.isStale || !Number.isFinite(read.silver) || Number.isNaN(at)) {
    session.missed = true;
    return;
  }
  // The same read again: xp.onUpdated also fires for changes that aren't a new read.
  if (session.lastAt !== null && at <= session.lastAt) return;
  if (session.lastAt === null || session.missed) {
    // Nothing trustworthy to measure from. FourFold's XP tracker starts over after a missed read too.
    session.startBalance ??= read.silver;
    session.missed = false;
  } else {
    // Silver earned only: a drop is spending, and adds nothing.
    const gain = Math.max(0, read.silver - session.lastBalance);
    session.intervals.push({ from: session.lastAt, to: at, gain });
    session.earned += gain;
  }
  session.lastBalance = read.silver;
  session.lastAt = at;
  session.intervals = session.intervals.filter(interval => interval.to > at - HOUR_MS);
}

// Silver per hour over the hour ending at `now`, or null when nothing in that hour was measured. The same sum as
// FourFold's XpRateWindow: each interval counts for the part of it inside the hour.
export function rateAt(session, now) {
  const start = now - HOUR_MS;
  let gain = 0;
  let covered = 0;
  for (const interval of session.intervals) {
    const overlap = Math.min(interval.to, now) - Math.max(interval.from, start);
    if (overlap <= 0) continue;
    gain += interval.gain * overlap / (interval.to - interval.from);
    covered += overlap;
  }
  return covered > 0 ? gain / covered * HOUR_MS : null;
}

// The account's status line. A missed read shows as stale whatever the reason, so old numbers never pass as live.
export function statusOf(session) {
  if (session.lastAt === null) return 'No data yet';
  if (session.missed) return 'Stale; the last read failed';
  return rateAt(session, session.lastAt) === null ? 'Collecting baseline' : 'Tracking';
}

// A goal as the user typed it: digits, with commas or spaces as thousands separators. Null when it isn't a whole
// number above zero. A dot is refused, not read as a separator: "1.5" must not become 15.
export function parseGoal(text) {
  const digits = String(text).replace(/[\s,]/g, '');
  if (!/^\d{1,15}$/.test(digits)) return null;
  const goal = Number(digits);
  return goal > 0 ? goal : null;
}

// What is left to a goal and how long it takes at this rate. hours is null without a rate above zero.
export function toGoal(goal, balance, rate) {
  const remaining = Math.max(0, goal - balance);
  return {
    remaining,
    progress: Math.min(1, Math.max(0, balance / goal)),
    hours: remaining > 0 && rate > 0 ? remaining / rate : null
  };
}
