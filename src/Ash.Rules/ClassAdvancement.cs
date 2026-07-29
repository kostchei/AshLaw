using System.Collections.ObjectModel;

namespace Ash.Rules;

public enum ClassLevelAdvanceKind
{
    PinnedAbility,
    TalentRoll,
}

public enum TalentOutcomeKind
{
    ClassTalent,
    AttributeBoost,
    Wildcard,
}

public sealed record ClassAbility(string Name, string Description);

public sealed record TalentOutcome(
    int MinimumRoll,
    int MaximumRoll,
    TalentOutcomeKind Kind,
    string Name,
    string Description)
{
    public bool Matches(int roll) => roll >= MinimumRoll && roll <= MaximumRoll;

    public int ProbabilityWays
    {
        get
        {
            var ways = 0;
            for (var roll = MinimumRoll; roll <= MaximumRoll; roll++)
            {
                ways += roll <= 7 ? roll - 1 : 13 - roll;
            }

            return ways;
        }
    }

    public decimal Probability => ProbabilityWays / 36m;
}

public sealed record ClassLevelAdvance(
    int Level,
    ClassLevelAdvanceKind Kind,
    IReadOnlyList<ClassAbility> Abilities);

/// <summary>
/// A level 1-20 class plan. Every level is either a pinned class-identity
/// milestone or an open 2d6 talent roll; the two never overlap.
/// </summary>
public sealed class ClassAdvancementPlan
{
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 20;

    private readonly IReadOnlyDictionary<int, IReadOnlyList<ClassAbility>> _pinnedAbilities;
    private readonly IReadOnlyList<TalentOutcome> _talentOutcomes;
    private readonly IReadOnlyList<int> _openTalentLevels;

    internal ClassAdvancementPlan(
        CharacterClass characterClass,
        string displayName,
        string designBasis,
        IEnumerable<(int Level, ClassAbility Ability)> pinnedAbilities,
        IEnumerable<TalentOutcome> talentOutcomes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(designBasis);

        Class = characterClass;
        DisplayName = displayName;
        DesignBasis = designBasis;

        var pinned = pinnedAbilities
            .GroupBy(entry => entry.Level)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ClassAbility>)Array.AsReadOnly(
                    group.Select(entry => entry.Ability).ToArray()));
        if (!pinned.ContainsKey(MinimumLevel))
        {
            throw new ArgumentException($"{displayName} must have a pinned level-1 identity.");
        }

        if (pinned.Keys.Any(level => level is < MinimumLevel or > MaximumLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pinnedAbilities),
                $"{displayName} has a pinned ability outside levels {MinimumLevel}-{MaximumLevel}.");
        }

        _pinnedAbilities =
            new ReadOnlyDictionary<int, IReadOnlyList<ClassAbility>>(pinned);
        _openTalentLevels = Array.AsReadOnly(
            Enumerable.Range(MinimumLevel, MaximumLevel)
                .Where(level => !pinned.ContainsKey(level))
                .ToArray());

        var outcomes = talentOutcomes.OrderBy(outcome => outcome.MinimumRoll).ToArray();
        for (var roll = 2; roll <= 12; roll++)
        {
            if (outcomes.Count(outcome => outcome.Matches(roll)) != 1)
            {
                throw new ArgumentException(
                    $"{displayName}'s talent table must cover 2d6 roll {roll} exactly once.");
            }
        }

        if (outcomes.Any(outcome =>
                outcome.MinimumRoll < 2 ||
                outcome.MaximumRoll > 12 ||
                outcome.MinimumRoll > outcome.MaximumRoll))
        {
            throw new ArgumentOutOfRangeException(
                nameof(talentOutcomes),
                $"{displayName}'s talent outcomes must use valid 2d6 ranges.");
        }

        _talentOutcomes = Array.AsReadOnly(outcomes);
    }

    public CharacterClass Class { get; }

    public string DisplayName { get; }

    public string DesignBasis { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<ClassAbility>> PinnedAbilities =>
        _pinnedAbilities;

    public IReadOnlyList<int> OpenTalentLevels => _openTalentLevels;

    public IReadOnlyList<TalentOutcome> TalentOutcomes => _talentOutcomes;

    public decimal ExpectedAttributeGains =>
        OpenTalentLevels.Count *
        TalentOutcomes
            .Where(outcome => outcome.Kind == TalentOutcomeKind.AttributeBoost)
            .Sum(outcome => outcome.Probability);

    public ClassLevelAdvance AtLevel(int level)
    {
        if (level is < MinimumLevel or > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                $"Level must be {MinimumLevel} through {MaximumLevel}.");
        }

        return _pinnedAbilities.TryGetValue(level, out var abilities)
            ? new ClassLevelAdvance(level, ClassLevelAdvanceKind.PinnedAbility, abilities)
            : new ClassLevelAdvance(
                level,
                ClassLevelAdvanceKind.TalentRoll,
                Array.Empty<ClassAbility>());
    }

    public TalentOutcome ResolveTalent(int roll)
    {
        if (roll is < 2 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(roll), roll, "A 2d6 roll must be 2 through 12.");
        }

        return _talentOutcomes.Single(outcome => outcome.Matches(roll));
    }
}

/// <summary>
/// The standard advancement plans for every playable class. The original four
/// reproduce class_progression_tables.md section 6; the additional eight retain
/// the same pinned-level/open-talent structure.
/// </summary>
public static class ClassAdvancementCatalog
{
    private static readonly IReadOnlyDictionary<CharacterClass, ClassAdvancementPlan> Plans =
        new ReadOnlyDictionary<CharacterClass, ClassAdvancementPlan>(
            CreatePlans().ToDictionary(plan => plan.Class));

    public static IReadOnlyCollection<ClassAdvancementPlan> All { get; } =
        Array.AsReadOnly(Plans.Values.ToArray());

    public static ClassAdvancementPlan For(CharacterClass characterClass) =>
        Plans.TryGetValue(characterClass, out var plan)
            ? plan
            : throw new RulesResolutionException(
                $"No advancement plan exists for character class '{characterClass}'.");

    private static IEnumerable<ClassAdvancementPlan> CreatePlans()
    {
        yield return Plan(
            CharacterClass.Fighter,
            "Fighter",
            "The existing martial master: weapon mastery, extra attacks, and critical lethality.",
            MartialTalents(
                "Mastery",
                "Weapon Specialization & Combat Style",
                "+1 Attack and Damage with one weapon type, or +1 AC while wearing armor.",
                "Martial Attribute Boost",
                "Strength, Dexterity, or Constitution",
                "Combat Vitality & Lethality",
                "+1 HP per level retroactively, or +1 melee/ranged critical severity."),
            P(1, "Martial Foundation", "Gain two weapon masteries and one fighting style."),
            P(3, "Improved Critical", "Improve the critical range of mastered weapons."),
            P(7, "Mastered Extra Attack", "Make a second attack when using a mastered weapon."),
            P(19, "Supreme Critical", "Choose an epic boon or supreme critical mastery."),
            P(20, "Defy Death", "Once at the brink of death, remain standing and fighting."));

        yield return Plan(
            CharacterClass.Cleric,
            "Priest",
            "The existing divine caster, governed by five deity virtues or vices.",
            MartialTalents(
                "Divine",
                "Divine Favor & Smite",
                "+1 to priest spellcasting, or +1 to turning and damage against undead.",
                "Holy Attribute Boost",
                "Wisdom, Strength, or Charisma",
                "Sacred Protection & Healing",
                "+1 AC in medium/heavy armor, or +1 HP restored by healing spells."),
            P(1, "Divine Office", "Turn undead and cast 1st-level priest spells."),
            P(3, "2nd-level Spells", "Unlock 2nd-level priest spells."),
            P(5, "3rd-level Spells", "Unlock 3rd-level priest spells."),
            P(7, "4th-level Spells", "Unlock 4th-level priest spells."),
            P(9, "5th-level Spells", "Unlock 5th-level priest spells."),
            P(11, "6th-level Spells", "Unlock 6th-level priest spells."),
            P(13, "7th-level Spells", "Unlock 7th-level priest spells."),
            P(15, "Divine Intervention", "Petition the deity for direct aid."),
            P(19, "Corona of Light", "Choose an epic boon or manifest a holy corona."),
            P(20, "Greater Divine Intervention", "Petition for a greater miracle."));

        yield return Plan(
            CharacterClass.Rogue,
            "Thief",
            "The existing skill expert: thievery, backstab, evasion, and magic-device use.",
            MartialTalents(
                "Shadow",
                "Agile Lethality & Thievery",
                "+1 Attack/Damage when backstabbing, or advantage with a chosen thief skill.",
                "Furtive Attribute Boost",
                "Dexterity, Intelligence, or Charisma",
                "Evasion & Reflexes",
                "+1 AC in light/no armor, or +1 Initiative and Reflex saves."),
            P(1, "Thief's Craft", "Gain advantage with thievery skills, ×2 backstab, and +4 to backstab."),
            P(5, "Greater Backstab", "Backstab damage becomes ×3."),
            P(9, "Greater Backstab", "Backstab damage becomes ×4."),
            P(10, "Second-storey Work", "Scale, balance, and infiltrate at expert level."),
            P(13, "Greater Backstab", "Backstab damage becomes ×5."),
            P(14, "Use Magic Device", "Employ magical devices without meeting their class requirements."),
            P(19, "Thief's Reflexes", "Choose an epic boon or an additional reactive turn."),
            P(20, "Elusive", "Ordinary attempts to pin down or corner the thief fail."));

        yield return Plan(
            CharacterClass.Wizard,
            "Wizard",
            "The existing prepared arcane caster, item-maker, and circle magician.",
            CasterTalents(
                "Arcane",
                "Arcane Memory & Mastery",
                "+1 spellcasting, or learn one additional spell of a known spell level.",
                "Mental Attribute Boost",
                "Intelligence, Wisdom, or Charisma",
                "Wards & Metamagic",
                "+1 saves against magic, or +10 ft range on non-touch spells."),
            P(1, "1st-level Spells", "Unlock 1st-level wizard spells."),
            P(3, "2nd-level Spells", "Unlock 2nd-level wizard spells."),
            P(5, "3rd-level Spells", "Unlock 3rd-level wizard spells."),
            P(7, "4th-level Spells", "Unlock 4th-level wizard spells."),
            P(9, "5th-level Spells", "Unlock 5th-level wizard spells."),
            P(10, "Consumable Artifice", "Create scrolls and potions."),
            P(11, "6th-level Spells", "Unlock 6th-level wizard spells."),
            P(13, "7th-level Spells", "Unlock 7th-level wizard spells."),
            P(14, "Permanent Artifice", "Create permanent magical items."),
            P(15, "8th-level Spells", "Unlock 8th-level wizard spells."),
            P(17, "9th-level Spells", "Unlock 9th-level wizard spells."),
            P(19, "Circle Magic", "Choose an epic boon or lead circle magic."));

        yield return Plan(
            CharacterClass.Bard,
            "Bard",
            "AD&D 1e bard: a fighter-thief-druid synthesis with music, lore, and druidic magic.",
            CasterTalents(
                "Ollamh",
                "Poetics & Jack-of-all-trades",
                "+1 to bardic music, or advantage with a chosen martial, thief, or lore skill.",
                "Heroic Attribute Boost",
                "Charisma, Dexterity, or Wisdom",
                "Legend Lore & Druidcraft",
                "+1 legend-lore checks, or learn one additional druidic spell."),
            P(1, "Bardic Synthesis", "Retain martial and thievery training; inspire courage, charm by music, recall legend lore, and cast 1st-level druidic spells."),
            P(3, "College of Fochlucan", "Unlock 2nd-level druidic spells and one additional language."),
            P(5, "College of Mac-Fuirmidh", "Music can counter fear and hostile enchantment."),
            P(7, "College of Doss", "Unlock 3rd-level druidic spells and improve charm by music."),
            P(9, "College of Canaith", "Legend lore can identify storied magical items."),
            P(11, "College of Cli", "Unlock 4th-level druidic spells and inspire ferocity."),
            P(13, "College of Anstruth", "Music can plant a suggestion in a charmed audience."),
            P(15, "College of Ollamh", "Unlock 5th-level druidic spells and master an additional language."),
            P(17, "High Druidic Office", "Gain the non-spell wilderness authority of a high druid."),
            P(19, "Song of Heroes", "Choose an epic boon or let allies ignore fear and stun for one scene."),
            P(20, "The Unending Lay", "Sustain one bardic performance without concentration or fatigue."));

        yield return Plan(
            CharacterClass.Barbarian,
            "Barbarian",
            "AD&D 1e Unearthed Arcana barbarian: exceptional toughness, wilderness craft, anti-magic instinct, and distrust of sorcery.",
            MartialTalents(
                "Primal",
                "Weapon Ferocity & Wilderness Craft",
                "+1 Attack/Damage with a tribal weapon, or advantage with a wilderness skill.",
                "Primal Attribute Boost",
                "Strength, Dexterity, or Constitution",
                "Hardiness & Anti-magic",
                "+1 HP per level retroactively, or +1 saves against spells and magical devices."),
            P(1, "Primal Survivor", "Gain exceptional hit points, fast movement, climbing, hiding, first aid, back protection, and wilderness survival."),
            P(3, "Instinct for Sorcery", "Detect illusions and the presence of magic through focused examination."),
            P(5, "Uncanny Reactions", "Reduce the chance of being surprised and cannot be flanked by ordinary foes."),
            P(7, "Weapon Ferocity", "Make a second attack with a specialized tribal weapon."),
            P(9, "Magic Aversion Mastery", "Use necessary magic without losing class abilities, but retain distrust of arcane coercion."),
            P(11, "Horde Leader", "Attract and command a war-band through reputation and victory."),
            P(13, "Indomitable", "Ignore the first stun or forced-movement effect in a scene."),
            P(15, "Spellbreaker", "A weapon blow can disrupt an active spell or magical barrier."),
            P(17, "Unconquered", "Remain conscious while below zero hit points until the battle ends."),
            P(19, "Legend of the Wild", "Choose an epic boon or become immune to mundane fear and exposure."),
            P(20, "World-breaker", "Critical melee hits can shatter shields, doors, and magical restraints."));

        yield return Plan(
            CharacterClass.Sorcerer,
            "Sorcerer",
            "Charisma-based defiler from docs/sorcerer.md: destructive spell lists, life-drawing spellfire, metamagic, counter-magic, illusion, and rule-bending.",
            CasterTalents(
                "Defiler",
                "Spellfire & Counter-magic",
                "+1 spellcasting/countering, or +1 spellfire charge capacity.",
                "Defiler Attribute Boost",
                "Charisma, Constitution, or Intelligence",
                "Metamagic & Defiling Control",
                "Learn one metamagic mode, or reduce life-drain radius while preserving power."),
            P(1, "Defiler Spellfire", "Cast through Charisma; draw life in a radius to fuel spellfire and learn Flesh and Solid Destruction."),
            P(3, "Spell Absorption", "Draw power from a directly incoming spell; unlock 2nd-level spells."),
            P(5, "Fluid Ruin", "Learn Gas/Fluid Destruction and unlock 3rd-level spells."),
            P(7, "Metamagic", "Shape range, area, duration, or force; unlock 4th-level spells."),
            P(9, "Counter-sorcery", "Gain Counter Magic, Spell Blast, and Disjunction; unlock 5th-level spells."),
            P(11, "Phantasmal Dominion", "Create resistance-tested phantoms and confusion; unlock 6th-level spells."),
            P(13, "Soul Destruction", "Learn magic that severs soul from body; unlock 7th-level spells."),
            P(14, "Magic Immunity", "Ward a creature so hostile magical attacks can be ignored."),
            P(15, "Bypass Physical Law", "Gain flight, wind walking, and invisibility effects; unlock 8th-level spells."),
            P(17, "Node Mastery", "Meld with sorcery nodes and unlock 9th-level spells."),
            P(19, "Spellfire Crucible", "Choose an epic boon or hold multiple absorbed spells as spellfire."),
            P(20, "Defiler Apotheosis", "Choose preservation or devastation when drawing life; soul destruction can annihilate utterly."));

        yield return Plan(
            CharacterClass.Paladin,
            "Paladin",
            "AD&D 1e paladin whose supernatural office is sustained by Pendragon's chivalric virtues.",
            MartialTalents(
                "Chivalric",
                "Holy Sword & Horsemanship",
                "+1 Attack/Damage with a chosen knightly weapon, or advantage with riding and command.",
                "Chivalric Attribute Boost",
                "Strength, Wisdom, or Charisma",
                "Grace & Mercy",
                "+1 saves through divine grace, or +1 healing per class level when laying on hands."),
            P(1, "Chivalric Office", "Detect malign supernatural forces, radiate protection, and lay on hands while the chivalric code is upheld."),
            P(3, "Divine Health", "Become immune to mundane disease and cure disease in another."),
            P(4, "Faithful Warhorse", "Call a bonded intelligent warhorse."),
            P(5, "Aura of Courage", "Nearby allies resist fear; turn undead as a lower-level priest."),
            P(7, "Holy Sword", "A consecrated sword gains power against supernatural evil."),
            P(9, "Clerical Spells", "Begin limited priest spellcasting, as in AD&D 1e."),
            P(11, "Chivalric Aura", "Allies who witness a fulfilled virtue gain morale and protection."),
            P(13, "Greater Lay on Hands", "Lay on hands can remove poison, disease, or curse instead of healing."),
            P(15, "Dispel Evil", "A holy sword can break hostile enchantment and banish summoned evil."),
            P(17, "Paragon of Chivalry", "A successful virtue test can restore an expended paladin feature."),
            P(19, "Grail Knight", "Choose an epic boon or make the protective aura ward against spells."),
            P(20, "The Once and Future Oath", "Falling in defense of the code grants allies a decisive final rally."));

        yield return Plan(
            CharacterClass.CelestialWarlock,
            "Celestial Warlock",
            "A pact caster bound to an empyrean, solar, couatl, unicorn, or other celestial patron.",
            CasterTalents(
                "Celestial Pact",
                "Eldritch Light & Invocation",
                "+1 pact spellcasting, or learn one celestial invocation.",
                "Pact Attribute Boost",
                "Charisma, Wisdom, or Constitution",
                "Healing Light & Radiant Ward",
                "+1 healing-light die step, or +1 saves against necrotic and radiant magic."),
            P(1, "Celestial Pact", "Gain pact magic, radiant spellfire, healing light, and the service demanded by a celestial patron."),
            P(3, "Radiant Invocation", "Learn a persistent invocation and unlock 2nd-level pact spells."),
            P(5, "Searing Mercy", "Radiant damage can spare a foe; healing light can end bleeding."),
            P(7, "Winged Messenger", "Unlock 4th-level pact spells and briefly manifest celestial flight."),
            P(9, "Radiant Soul", "Resist radiant damage and add Charisma to one radiant or fire effect."),
            P(11, "Sanctuary Between Stars", "Open a brief refuge in the patron's realm and unlock 6th-level pact magic."),
            P(13, "Celestial Resilience", "Grant the party temporary hit points after rest."),
            P(15, "Hurl Through Heaven", "Banish a struck foe through the patron's realm, returning it transformed or chastened."),
            P(17, "True Name of Light", "Call a 9th-level pact arcanum without consuming ordinary spellfire."),
            P(19, "Patron's Right Hand", "Choose an epic boon or invoke the patron without petition."),
            P(20, "Searing Vengeance", "Rise once in radiant fire, healing allies and blinding hostile creatures."));

        yield return Plan(
            CharacterClass.Monk,
            "Monk",
            "AD&D 1e monk: scaling open-hand combat, speed, defenses, bodily mastery, and lethal ki techniques.",
            MartialTalents(
                "Ki",
                "Open-hand Mastery & Thiefcraft",
                "+1 Attack/Damage unarmed, or advantage with a monk thief skill.",
                "Disciplined Attribute Boost",
                "Dexterity, Wisdom, or Constitution",
                "Evasion & Inner Strength",
                "+1 unarmored AC, or +1 saves against mind and body effects."),
            P(1, "Disciple of the Open Hand", "Gain unarmed damage, extra open-hand attacks, unarmored defense, speed, and selected thief skills."),
            P(3, "Missile Deflection", "Deflect or catch ordinary missiles while aware of the attack."),
            P(5, "Purity of Body", "Become immune to mundane disease and resist slow or haste."),
            P(7, "Wholeness of Body", "Heal personal wounds through meditation."),
            P(9, "Enchanted Hands", "Open-hand attacks count as magical; become immune to ordinary charm."),
            P(11, "Diamond Body", "Become immune to ordinary poison."),
            P(13, "Quivering Palm", "Plant lethal vibrations with a declared open-hand strike."),
            P(14, "Diamond Soul", "Gain level-scaled resistance to magic."),
            P(15, "Timeless Body", "No longer suffer frailty from age, hunger, or exposure."),
            P(17, "Tongue of Sun and Moon", "Communicate with any living creature and project the spirit."),
            P(19, "Empty Body", "Choose an epic boon or become ethereal briefly."),
            P(20, "Grand Master of Flowers", "Open-hand attacks overcome mundane immunity and bodily mastery is complete."));

        yield return Plan(
            CharacterClass.Ranger,
            "Ranger",
            "AD&D 1e ranger: fighter-grade combat, tracking, surprise, giant-slayer training, followers, and late druid/magic-user spells.",
            MartialTalents(
                "Warden",
                "Favored-enemy Hunter & Woodcraft",
                "+1 Attack/Damage against a chosen enemy family, or advantage with a wilderness skill.",
                "Warden Attribute Boost",
                "Strength, Dexterity, or Wisdom",
                "Skirmishing & Animal Bond",
                "+1 AC while moving in light armor, or improve a companion's attacks and saves."),
            P(1, "Wilderness Guardian", "Track, forage, avoid surprise, and deal bonus damage to giant-class or chosen marauding foes."),
            P(3, "Woodland Ambush", "Improve party surprise and concealment in natural terrain."),
            P(5, "Two-weapon Skirmisher", "Make an off-hand attack without the ordinary light-armor penalty."),
            P(7, "Ranger's Company", "Guide a party through hostile wilderness without becoming lost."),
            P(9, "Druidic & Magic-user Spells", "Begin the limited dual spell progression of the AD&D 1e ranger."),
            P(11, "Animal Companion", "Bond a loyal animal or fantastic wilderness companion."),
            P(13, "Giant-killer", "Critical severity rises against giant-class and chosen marauding foes."),
            P(15, "Unseen Warden", "The ranger's party leaves no mundane trail and can hide while travelling."),
            P(17, "High-level Spell Limit", "Reach the 1e ranger's maximum druidic and magic-user spell capability."),
            P(19, "Lord of the Borderlands", "Choose an epic boon or attract exceptional followers."),
            P(20, "Guardian of the Wild", "Name a region; within it the ranger cannot be surprised, lost, or deprived of sustenance."));

        yield return Plan(
            CharacterClass.Assassin,
            "Assassin",
            "AD&D 1e assassin: a thief subclass focused on studied assassination, disguise, poison, languages, and guild mastery.",
            MartialTalents(
                "Executioner",
                "Backstab & Poisoncraft",
                "+1 Attack/Damage from surprise, or improve one prepared poison.",
                "Assassin Attribute Boost",
                "Dexterity, Intelligence, or Charisma",
                "Disguise & Escape",
                "+1 disguise checks and detection DC, or +1 AC in light/no armor."),
            P(1, "Assassin's Trade", "Use any weapon, backstab, handle poison safely, disguise identity, and study a target for assassination."),
            P(3, "Thief Skills", "Gain thief abilities at the delayed rate of an AD&D 1e assassin."),
            P(5, "Professional Poisoner", "Prepare, identify, and apply contact or injected poisons."),
            P(7, "Deep Disguise", "Maintain a disguise under extended scrutiny and imitate a social role."),
            P(9, "Alignment Tongue", "Learn an additional secret or alignment language at this and later pinned milestones."),
            P(11, "Greater Assassination", "Reduce a studied target's resistance to the assassination attempt."),
            P(13, "Prime Assassin", "Command an assassins' guild after defeating or displacing its master."),
            P(15, "Grandfather's Art", "Assassination can bypass one layer of magical or bodily death protection."),
            P(17, "Poison Immunity", "Become immune to personally prepared poisons and highly resistant to others."),
            P(19, "Perfect Impostor", "Choose an epic boon or become undetectable by mundane scrutiny while in role."),
            P(20, "Death Without a Trace", "A successful assassination can be made to resemble accident, illness, or another culprit."));
    }

    private static ClassAdvancementPlan Plan(
        CharacterClass characterClass,
        string displayName,
        string designBasis,
        IReadOnlyList<TalentOutcome> talents,
        params (int Level, ClassAbility Ability)[] pinned) =>
        new(characterClass, displayName, designBasis, pinned, talents);

    private static (int Level, ClassAbility Ability) P(
        int level,
        string name,
        string description) =>
        (level, new ClassAbility(name, description));

    private static IReadOnlyList<TalentOutcome> MartialTalents(
        string wildcardPrefix,
        string lowName,
        string lowDescription,
        string attributeName,
        string attributes,
        string highName,
        string highDescription) =>
    [
        new(2, 2, TalentOutcomeKind.Wildcard, $"{wildcardPrefix} Wildcard", WildcardDescription),
        new(3, 6, TalentOutcomeKind.ClassTalent, lowName, lowDescription),
        new(
            7,
            9,
            TalentOutcomeKind.AttributeBoost,
            attributeName,
            $"+2 to {attributes}, or +1 to two of those attributes."),
        new(10, 11, TalentOutcomeKind.ClassTalent, highName, highDescription),
        new(12, 12, TalentOutcomeKind.Wildcard, $"{wildcardPrefix} Wildcard", WildcardDescription),
    ];

    private static IReadOnlyList<TalentOutcome> CasterTalents(
        string wildcardPrefix,
        string lowName,
        string lowDescription,
        string attributeName,
        string attributes,
        string highName,
        string highDescription) =>
    [
        new(2, 2, TalentOutcomeKind.Wildcard, $"{wildcardPrefix} Wildcard", WildcardDescription),
        new(3, 5, TalentOutcomeKind.ClassTalent, lowName, lowDescription),
        new(
            6,
            9,
            TalentOutcomeKind.AttributeBoost,
            attributeName,
            $"+2 to {attributes}, or +1 to two of those attributes."),
        new(10, 11, TalentOutcomeKind.ClassTalent, highName, highDescription),
        new(12, 12, TalentOutcomeKind.Wildcard, $"{wildcardPrefix} Wildcard", WildcardDescription),
    ];

    private const string WildcardDescription =
        "+2 to any attribute, or choose any other option on this class talent table.";
}

public enum ChivalricVirtue
{
    Energetic,
    Generous,
    Just,
    Merciful,
    Modest,
    Valorous,
}

/// <summary>
/// Pendragon's six-score chivalry test, expressed on the same 1-20 virtue scale
/// already used by priest alignment. A paladin's supernatural class abilities
/// require all six named scores and a combined total of at least 80.
/// </summary>
public static class PaladinChivalricCode
{
    public const int MinimumTotal = 80;

    public static SpellcastingCheck Check(
        IReadOnlyDictionary<ChivalricVirtue, int> virtueScores)
    {
        ArgumentNullException.ThrowIfNull(virtueScores);

        var required = Enum.GetValues<ChivalricVirtue>();
        if (virtueScores.Count != required.Length ||
            required.Any(virtue => !virtueScores.ContainsKey(virtue)))
        {
            throw new ArgumentException(
                $"A paladin must track exactly these chivalric virtues: {string.Join(", ", required)}.",
                nameof(virtueScores));
        }

        foreach (var (virtue, score) in virtueScores)
        {
            if (score is < 1 or > 20)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(virtueScores),
                    score,
                    $"{virtue} must be rated from 1 through 20.");
            }
        }

        var total = virtueScores.Values.Sum();
        return total >= MinimumTotal
            ? SpellcastingCheck.Allowed(
                $"Chivalric code maintained ({total}/{MinimumTotal}).")
            : SpellcastingCheck.Blocked(
                $"Chivalric code broken: the six virtues total {total}; " +
                $"{MinimumTotal} is required. Supernatural paladin abilities are suppressed until atonement.");
    }
}
