/**
 * Bit-exact JS implementation of Ash.Rules.Dice (xorshift64* PRNG).
 * Supports deterministic seeding and pool rolling with detailed die breakdown.
 */
class Dice {
  constructor(seed = Date.now()) {
    const BigSeed = BigInt(seed);
    // Zero is the one state a xorshift generator cannot leave.
    this.state = BigSeed === 0n ? 0x9E3779B97F4A7C15n : BigInt.asUintN(64, BigSeed);
  }

  static fromState(state) {
    const d = new Dice(0);
    d.state = BigInt.asUintN(64, BigInt(state));
    return d;
  }

  nextUInt64() {
    let s = this.state;
    s ^= (s >> 12n) & 0xFFFFFFFFFFFFFFFFn;
    s ^= (s << 25n) & 0xFFFFFFFFFFFFFFFFn;
    s ^= (s >> 27n) & 0xFFFFFFFFFFFFFFFFn;
    this.state = BigInt.asUintN(64, s);
    return BigInt.asUintN(64, this.state * 0x2545F4914F6CDD1Dn);
  }

  roll(sides) {
    if (sides < 1) {
      throw new Error(`Sides must be >= 1, got ${sides}`);
    }
    const val = this.nextUInt64() % BigInt(sides);
    return Number(val) + 1;
  }

  d20() {
    return this.roll(20);
  }

  d6() {
    return this.roll(6);
  }

  pool(count, sides) {
    if (count < 0) {
      throw new Error(`Count must be >= 0, got ${count}`);
    }
    const rolls = [];
    for (let i = 0; i < count; i++) {
      rolls.push(this.roll(sides));
    }
    return rolls;
  }

  poolKeepHighest(count, sides, keep) {
    const details = this.poolDetails(count, sides, keep);
    return details.sum;
  }

  /**
   * Returns pool roll details including total sum, all rolled dice, and kept dice indices.
   */
  poolDetails(count, sides, keep) {
    if (keep < 0 || keep > count) {
      throw new Error(`Invalid keep count ${keep} for pool size ${count}`);
    }
    const rawRolls = this.pool(count, sides);
    const indexed = rawRolls.map((val, idx) => ({ val, idx }));
    indexed.sort((a, b) => b.val - a.val);

    const keptIndexed = indexed.slice(0, keep);
    const keptIndices = new Set(keptIndexed.map(item => item.idx));
    const sum = keptIndexed.reduce((acc, item) => acc + item.val, 0);

    return {
      sum,
      rawRolls,
      keptIndices,
      keptValues: keptIndexed.map(item => item.val)
    };
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { Dice };
}
