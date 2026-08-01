/**
 * UI Controller for AshLaw Character Sheet Web App.
 * Handles Ironman (highest-stat class recommendation: DEX -> Thief, WIS -> Priest, STR -> Fighter, INT -> Wizard)
 * and Unearthed Arcana generation methods.
 */
document.addEventListener("DOMContentLoaded", () => {
  const rulesEngine = new RulesEngine();
  const generator = new CharacterGenerator(rulesEngine);

  let currentMethod = "ironman";
  let lastGeneratedResult = null;
  let lastUsedSeed = 0;

  // DOM Elements
  const tabIronman = document.getElementById("tab-ironman");
  const tabUnearthed = document.getElementById("tab-unearthed");
  const selectClass = document.getElementById("select-class");
  const inputSeed = document.getElementById("input-seed");
  const btnGenerate = document.getElementById("btn-generate");
  const btnCopyCharacter = document.getElementById("btn-copy-character");

  // Sheet DOM Elements
  const valName = document.getElementById("val-name");
  const valAncestry = document.getElementById("val-ancestry");
  const valClass = document.getElementById("val-class");
  const valHp = document.getElementById("val-hp");
  const valAc = document.getElementById("val-ac");
  const valAttacks = document.getElementById("val-attacks");
  const valTalents = document.getElementById("val-talents");
  const valGearList = document.getElementById("val-gear-list");
  const valMethodSeed = document.getElementById("val-method-seed");
  const classRecommendNotice = document.getElementById("class-recommend-notice");
  const recommendText = document.getElementById("recommend-text");

  // Stat Elements
  const statMap = {
    strength: { score: document.getElementById("score-str"), mod: document.getElementById("mod-str") },
    intelligence: { score: document.getElementById("score-int"), mod: document.getElementById("mod-int") },
    dexterity: { score: document.getElementById("score-dex"), mod: document.getElementById("mod-dex") },
    wisdom: { score: document.getElementById("score-wis"), mod: document.getElementById("mod-wis") },
    constitution: { score: document.getElementById("score-con"), mod: document.getElementById("mod-con") },
    charisma: { score: document.getElementById("score-cha"), mod: document.getElementById("mod-cha") }
  };

  // Preset Class Data for AshLaw
  const CLASS_DATA = {
    fighter: {
      name: "Fighter",
      hpBase: 8,
      acBase: 14, // Chainmail + Shield
      attacks: "• Longsword (1d8 + STR mod)<br>• Heavy Crossbow (1d8)",
      talents: "• <strong>Class Feature:</strong> Weapon Master (+1 hit/dmg with chosen weapon)<br>• <strong>Grit:</strong> Advantage on CON checks<br>• <strong>Human Ancestry:</strong> 2 rolls on Class Talent Table",
      gear: ["Chainmail", "Shield", "Longsword", "Crossbow", "Bolts (20)", "Rations (3)", "Torch (3)", "Backpack"]
    },
    rogue: {
      name: "Thief",
      hpBase: 6,
      acBase: 12, // Leather armor
      attacks: "• Shortsword (1d6 + DEX mod)<br>• Dagger (1d4 + DEX mod)",
      talents: "• <strong>Class Feature:</strong> Backstab (+1d6 dmg vs surprised target)<br>• <strong>Thievery:</strong> Pick Pockets & Disable Traps<br>• <strong>Human Ancestry:</strong> 2 rolls on Class Talent Table",
      gear: ["Leather Armor", "Shortsword", "Daggers (3)", "Thieves' Tools", "Rope (50ft)", "Rations (3)", "Torch (3)", "Backpack"]
    },
    cleric: {
      name: "Priest",
      hpBase: 6,
      acBase: 14, // Chainmail + Shield
      attacks: "• Mace (1d6 + STR mod)<br>• Holy Water Flask",
      talents: "• <strong>Spellcasting:</strong> Turn Undead, Cure Wounds<br>• <strong>Deity Favor:</strong> Advantage on Wisdom spellcasting<br>• <strong>Human Ancestry:</strong> 2 rolls on Class Talent Table",
      gear: ["Chainmail", "Shield", "Mace", "Holy Symbol", "Holy Water", "Rations (3)", "Torch (3)", "Backpack"]
    },
    wizard: {
      name: "Wizard",
      hpBase: 4,
      acBase: 10, // No armor
      attacks: "• Dagger (1d4 + DEX/STR mod)<br>• Light Staff (1d4)",
      talents: "• <strong>Spellcasting:</strong> Magic Missile, Light, Sleep<br>• <strong>Arcane Mastery:</strong> Advantage on INT spell checks<br>• <strong>Human Ancestry:</strong> 2 rolls on Class Talent Table",
      gear: ["Spellbook", "Dagger", "Staff", "Ink & Quill", "Rations (3)", "Torch (3)", "Backpack", "Pouch"]
    }
  };

  const NAMES = ["Valerius", "Kaelen", "Thorne", "Rowan", "Gideon", "Morgath", "Elric", "Caelum"];

  // Tab switching
  tabIronman.addEventListener("click", () => {
    currentMethod = "ironman";
    tabIronman.classList.add("active");
    tabUnearthed.classList.remove("active");
    generateCharacter();
  });

  tabUnearthed.addEventListener("click", () => {
    currentMethod = "unearthed_arcana";
    tabUnearthed.classList.add("active");
    tabIronman.classList.remove("active");
    generateCharacter();
  });

  btnGenerate.addEventListener("click", generateCharacter);

  function generateCharacter() {
    let seedVal;
    if (inputSeed.value.trim() !== "") {
      seedVal = parseInt(inputSeed.value.trim(), 10);
      if (isNaN(seedVal)) seedVal = Date.now();
    } else {
      seedVal = Math.floor(Math.random() * 100000000);
    }
    lastUsedSeed = seedVal;

    const dice = new Dice(seedVal);
    const selectedClass = selectClass.value;

    let result;
    if (currentMethod === "ironman") {
      const rolled = generator.rollIronman(dice, "human");
      result = generator.chooseIronmanClass(rolled, selectedClass);
    } else {
      const cls = (selectedClass === 'auto') ? 'fighter' : selectedClass;
      result = generator.rollUnearthedArcana(dice, cls, "human");
    }

    lastGeneratedResult = result;
    renderSheet(result, seedVal);
  }

  function renderSheet(char, seed) {
    const clsKey = char.characterClass;
    const info = CLASS_DATA[clsKey] || CLASS_DATA.fighter;

    // Random Name based on seed
    const nameIndex = Math.abs(seed) % NAMES.length;
    valName.textContent = NAMES[nameIndex];
    valAncestry.textContent = char.ancestry;
    valClass.textContent = info.name;

    // Stat Values & Modifiers
    for (const stat of ABILITIES) {
      const val = char.scores[stat];
      const mod = char.bonuses[stat];
      const modStr = mod >= 0 ? `+${mod}` : `${mod}`;
      
      if (statMap[stat]) {
        statMap[stat].score.textContent = val;
        statMap[stat].mod.textContent = modStr;
      }
    }

    // HP & AC calculation
    const conMod = char.bonuses.constitution;
    const dexMod = char.bonuses.dexterity;
    const finalHp = Math.max(1, info.hpBase + conMod);
    const finalAc = clsKey === 'wizard' || clsKey === 'rogue' ? info.acBase + dexMod : info.acBase;

    valHp.textContent = finalHp;
    valAc.textContent = finalAc;

    valAttacks.innerHTML = info.attacks;
    valTalents.innerHTML = info.talents;

    // Gear List
    valGearList.innerHTML = info.gear.map((item, idx) => `
      <div class="gear-slot">${idx + 1}. ${item}</div>
    `).join("");

    // Method & Seed
    valMethodSeed.textContent = `${char.method === 'ironman' ? 'Ironman' : 'Unearthed Arcana'} (Attempts: ${char.attempts}, Seed: ${seed})`;

    // Recommend notice for Ironman
    if (char.method === 'ironman') {
      classRecommendNotice.style.display = "flex";
      const recClassLabel = CLASS_DATA[char.suggestedClass] ? CLASS_DATA[char.suggestedClass].name : char.suggestedClass;
      recommendText.textContent = `Ironman stat analysis: Highest prime stat yields ${recClassLabel}. Assigned: ${info.name}.`;
    } else {
      classRecommendNotice.style.display = "none";
    }
  }

  // Copy Sheet summary
  btnCopyCharacter.addEventListener("click", () => {
    if (!lastGeneratedResult) return;
    const c = lastGeneratedResult;
    const clsName = CLASS_DATA[c.characterClass] ? CLASS_DATA[c.characterClass].name : c.characterClass;
    
    const text = [
      `=========================================`,
      `ASHLAW CHARACTER SHEET - ${valName.textContent.toUpperCase()}`,
      `Class: ${clsName} | Ancestry: ${c.ancestry}`,
      `Method: ${c.methodLabel} | Seed: ${lastUsedSeed}`,
      `-----------------------------------------`,
      `STR: ${c.scores.strength} (${c.bonuses.strength >= 0 ? '+' : ''}${c.bonuses.strength})   INT: ${c.scores.intelligence} (${c.bonuses.intelligence >= 0 ? '+' : ''}${c.bonuses.intelligence})`,
      `DEX: ${c.scores.dexterity} (${c.bonuses.dexterity >= 0 ? '+' : ''}${c.bonuses.dexterity})   WIS: ${c.scores.wisdom} (${c.bonuses.wisdom >= 0 ? '+' : ''}${c.bonuses.wisdom})`,
      `CON: ${c.scores.constitution} (${c.bonuses.constitution >= 0 ? '+' : ''}${c.bonuses.constitution})   CHA: ${c.scores.charisma} (${c.bonuses.charisma >= 0 ? '+' : ''}${c.bonuses.charisma})`,
      `-----------------------------------------`,
      `HP: ${valHp.textContent} | AC: ${valAc.textContent}`,
      `=========================================`
    ].join("\n");

    navigator.clipboard.writeText(text).then(() => {
      const orig = btnCopyCharacter.textContent;
      btnCopyCharacter.textContent = "✅ Copied!";
      setTimeout(() => { btnCopyCharacter.textContent = orig; }, 2000);
    });
  });

  // Generate initial character on load
  generateCharacter();
});
