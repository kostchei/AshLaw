/**
 * Rules data and configuration loaded from character-creation.json
 */
const CHARACTER_CREATION_DATA = {
  schema_version: 1,
  ability_bonuses: {
    minimum_score: 3,
    maximum_score: 20,
    rows: [
      { score: 3, bonus: -4 },
      { score: 4, bonus: -3 },
      { score: 5, bonus: -3 },
      { score: 6, bonus: -2 },
      { score: 7, bonus: -2 },
      { score: 8, bonus: -1 },
      { score: 9, bonus: -1 },
      { score: 10, bonus: 0 },
      { score: 11, bonus: 0 },
      { score: 12, bonus: 1 },
      { score: 13, bonus: 1 },
      { score: 14, bonus: 2 },
      { score: 15, bonus: 2 },
      { score: 16, bonus: 3 },
      { score: 17, bonus: 3 },
      { score: 18, bonus: 4 },
      { score: 19, bonus: 4 },
      { score: 20, bonus: 5 }
    ]
  },
  ancestries: [
    {
      id: "human",
      name: "Human",
      talent_rolls: 2,
      description: "Humans take two rolls on their class talent table where another ancestry takes one."
    }
  ],
  ironman: {
    dice_per_score: 3,
    die_sides: 6,
    keep_highest: 3,
    score_order: [
      "strength",
      "dexterity",
      "constitution",
      "intelligence",
      "wisdom",
      "charisma"
    ],
    playable: {
      high_score: 15,
      high_scores_required: 2,
      low_score: 6,
      low_scores_allowed: 1
    },
    maximum_attempts: 1000
  },
  unearthed_arcana: {
    die_sides: 6,
    keep_highest: 3,
    pools: [8, 7, 6, 5, 4, 3],
    class_priorities: {
      fighter: [
        "strength",
        "dexterity",
        "constitution",
        "charisma",
        "wisdom",
        "intelligence"
      ],
      rogue: [
        "dexterity",
        "intelligence",
        "charisma",
        "constitution",
        "strength",
        "wisdom"
      ],
      cleric: [
        "wisdom",
        "charisma",
        "strength",
        "intelligence",
        "constitution",
        "dexterity"
      ],
      wizard: [
        "intelligence",
        "wisdom",
        "dexterity",
        "constitution",
        "charisma",
        "strength"
      ]
    }
  }
};

class RulesEngine {
  constructor(data = CHARACTER_CREATION_DATA) {
    this.data = data;
    this.bonusMap = new Map();
    for (const row of data.ability_bonuses.rows) {
      this.bonusMap.set(row.score, row.bonus);
    }
  }

  getBonus(score) {
    if (this.bonusMap.has(score)) {
      return this.bonusMap.get(score);
    }
    return Math.floor((score - 10) / 2);
  }

  isIronmanPlayable(scoresArray) {
    const rules = this.data.ironman.playable;
    const highCount = scoresArray.filter(s => s >= rules.high_score).length;
    const lowCount = scoresArray.filter(s => s < rules.low_score).length;
    return highCount >= rules.high_scores_required && lowCount <= rules.low_scores_allowed;
  }

  getAncestry(ancestryId = "human") {
    const found = this.data.ancestries.find(a => a.id.toLowerCase() === ancestryId.toLowerCase());
    return found || this.data.ancestries[0];
  }

  getUnearthedArcanaPriority(characterClass) {
    const clsKey = characterClass.toLowerCase();
    const priorities = this.data.unearthed_arcana.class_priorities[clsKey];
    if (!priorities) {
      throw new Error(`No class priority found for class '${characterClass}'`);
    }
    return priorities;
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { CHARACTER_CREATION_DATA, RulesEngine };
}
