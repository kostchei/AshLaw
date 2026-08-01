/**
 * Character Generation Logic matching C# Ash.Rules.CharacterCreation.
 * Updated to reflect ShadowDark RPG class recommendations for Ironman mode:
 * High STR -> Fighter, High DEX -> Thief, High INT -> Wizard, High WIS -> Priest.
 */

const ABILITIES = ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"];
const ABILITY_LABELS = {
  strength: "STR",
  dexterity: "DEX",
  constitution: "CON",
  intelligence: "INT",
  wisdom: "WIS",
  charisma: "CHA"
};

const ABILITY_FULL_NAMES = {
  strength: "Strength",
  dexterity: "Dexterity",
  constitution: "Constitution",
  intelligence: "Intelligence",
  wisdom: "Wisdom",
  charisma: "Charisma"
};

const CLASSES = ["fighter", "rogue", "cleric", "wizard"];
const CLASS_LABELS = {
  fighter: "Fighter",
  rogue: "Thief",
  cleric: "Priest",
  wizard: "Wizard"
};

/**
 * Suggests class for Ironman rolls based on highest prime stat:
 * High DEX -> Thief, High WIS -> Priest, High STR -> Fighter, High INT -> Wizard
 */
function suggestClassFromScores(scores) {
  const primeStats = [
    { key: "strength", classKey: "fighter" },
    { key: "dexterity", classKey: "rogue" },
    { key: "wisdom", classKey: "cleric" },
    { key: "intelligence", classKey: "wizard" }
  ];

  let maxVal = -1;
  let recommended = "fighter";

  for (const stat of primeStats) {
    if (scores[stat.key] > maxVal) {
      maxVal = scores[stat.key];
      recommended = stat.classKey;
    }
  }

  return recommended;
}

class CharacterGenerator {
  constructor(rulesEngine = new RulesEngine()) {
    this.rules = rulesEngine;
  }

  rollIronman(dice, ancestryId = "human") {
    const config = this.rules.data.ironman;
    const maxAttempts = config.maximum_attempts;
    const scoreOrder = config.score_order;

    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      const scores = {};
      const rollsDetail = {};
      const scoresArray = [];

      for (const ability of scoreOrder) {
        const detail = dice.poolDetails(config.dice_per_score, config.die_sides, config.keep_highest);
        scores[ability] = detail.sum;
        rollsDetail[ability] = detail;
        scoresArray.push(detail.sum);
      }

      if (!this.rules.isIronmanPlayable(scoresArray)) {
        continue;
      }

      const ancestryDef = this.rules.getAncestry(ancestryId);
      const suggestedClass = suggestClassFromScores(scores);

      return {
        method: "ironman",
        scores,
        rollsDetail,
        ancestry: ancestryDef,
        attempts: attempt,
        suggestedClass,
        isPlayable: true
      };
    }

    throw new Error(`No playable Ironman set found in ${maxAttempts} attempts.`);
  }

  chooseIronmanClass(rolledScores, selectedClass) {
    // If selectedClass is null/undefined or 'auto', use suggested class from highest stat
    const clsKey = (selectedClass && selectedClass !== 'auto') 
      ? selectedClass.toLowerCase() 
      : rolledScores.suggestedClass;

    const ancestryDef = rolledScores.ancestry;

    const bonuses = {};
    for (const ability of ABILITIES) {
      bonuses[ability] = this.rules.getBonus(rolledScores.scores[ability]);
    }

    return {
      method: "ironman",
      methodLabel: "Ironman (3d6 in order, rerolled)",
      characterClass: clsKey,
      className: CLASS_LABELS[clsKey] || selectedClass,
      ancestry: ancestryDef.name,
      talentRolls: ancestryDef.talent_rolls,
      attempts: rolledScores.attempts,
      scores: { ...rolledScores.scores },
      bonuses,
      rollsDetail: rolledScores.rollsDetail,
      suggestedClass: rolledScores.suggestedClass
    };
  }

  rollUnearthedArcana(dice, selectedClass, ancestryId = "human") {
    const clsKey = selectedClass.toLowerCase();
    const config = this.rules.data.unearthed_arcana;
    const priority = this.rules.getUnearthedArcanaPriority(clsKey);
    const pools = config.pools;

    const scores = {};
    const rollsDetail = {};
    const poolAllocation = {};

    for (let i = 0; i < priority.length; i++) {
      const ability = priority[i];
      const poolSize = pools[i];
      const detail = dice.poolDetails(poolSize, config.die_sides, config.keep_highest);
      scores[ability] = detail.sum;
      rollsDetail[ability] = detail;
      poolAllocation[ability] = poolSize;
    }

    const ancestryDef = this.rules.getAncestry(ancestryId);
    const bonuses = {};
    for (const ability of ABILITIES) {
      bonuses[ability] = this.rules.getBonus(scores[ability]);
    }

    return {
      method: "unearthed_arcana",
      methodLabel: "Unearthed Arcana (Class priority pools)",
      characterClass: clsKey,
      className: CLASS_LABELS[clsKey] || selectedClass,
      ancestry: ancestryDef.name,
      talentRolls: ancestryDef.talent_rolls,
      attempts: 1,
      scores,
      bonuses,
      rollsDetail,
      poolAllocation,
      priorityOrder: priority
    };
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = {
    ABILITIES,
    ABILITY_LABELS,
    ABILITY_FULL_NAMES,
    CLASSES,
    CLASS_LABELS,
    suggestClassFromScores,
    CharacterGenerator
  };
}
