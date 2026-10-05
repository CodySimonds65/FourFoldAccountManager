// Checks rate.mjs outside FourFold. Run: node samples/silver-tracker/.check/rate.check.mjs
import assert from 'node:assert/strict';
import { applyRead, createSession, parseGoal, rateAt, toGoal } from '../rate.mjs';

const at = minutes => new Date(Date.UTC(2026, 9, 4, 12, 0) + minutes * 60000).toISOString();
const read = (silver, minutes, isStale = false) => ({ silver, updatedAt: at(minutes), isStale });
const feed = (...reads) => {
  const session = createSession();
  for (const next of reads) applyRead(session, next);
  return session;
};
const rate = session => rateAt(session, session.lastAt);
const net = session => session.lastBalance - session.startBalance;

// One read alone gives no rate. Two reads a minute apart: 600 silver in 60 seconds is 36,000 an hour.
assert.equal(rate(feed(read(1000, 0))), null);
let session = feed(read(1000, 0), read(1600, 1));
assert.equal(session.earned, 600);
assert.equal(Math.round(rate(session)), 36000);

// Spending adds nothing to earned and never makes the rate negative. Net does go negative.
session = feed(read(5000, 0), read(2000, 1), read(2500, 2));
assert.equal(session.earned, 500);
assert.equal(net(session), -2500);
assert.equal(Math.round(rate(session)), 15000);

// A failed read, then a good one half an hour later with far more silver: no interval spans the gap, so the rate
// doesn't spike. Net is still the true change.
session = feed(read(1000, 0), read(1100, 1), read(1100, 1, true), read(9000, 30), read(9100, 31));
assert.equal(session.intervals.length, 2);
assert.equal(session.earned, 200);
assert.equal(net(session), 8100);
assert.equal(Math.round(rate(session)), 6000);

// The same read twice, or one stamped earlier than the last, changes nothing.
session = feed(read(1000, 0), read(1600, 1), read(1600, 1), read(99999, 0));
assert.equal(session.earned, 600);
assert.equal(session.intervals.length, 1);

// A read with no silver or no time counts as missed, like a stale one.
session = feed(read(1000, 0), { silver: null, updatedAt: null, isStale: false }, read(5000, 1), read(5100, 2));
assert.equal(session.earned, 100);

// Only the last hour counts. An interval that ended before it is dropped; one that straddles it counts in part.
session = feed(read(0, 0), read(60000, 1), read(60000, 90), read(60000, 91));
assert.equal(rate(session), 0);
assert.equal(session.earned, 60000);
assert.equal(Math.round(rate(feed(read(0, 0), read(1200, 120)))), 600);

// A goal is a whole number above zero, with commas or spaces as separators. Anything else is no goal.
assert.equal(parseGoal('1,000,000'), 1000000);
assert.equal(parseGoal(' 250 000 '), 250000);
for (const bad of ['', '0', '-5', '1.5', '1e9', 'abc', '9'.repeat(16)]) assert.equal(parseGoal(bad), null);

// Time to a goal needs a rate above zero. A reached goal has nothing left.
assert.deepEqual(toGoal(1000, 400, 300), { remaining: 600, progress: 0.4, hours: 2 });
assert.equal(toGoal(1000, 400, null).hours, null);
assert.equal(toGoal(1000, 400, 0).hours, null);
assert.deepEqual(toGoal(1000, 2500, 300), { remaining: 0, progress: 1, hours: null });

console.log('rate.mjs: all checks passed');
