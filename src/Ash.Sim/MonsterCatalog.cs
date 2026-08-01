using System.Security.Cryptography;
using System.Text;
using Ash.Rules;

namespace Ash.Sim;

public enum MonsterOrigin
{
    Adapted,
    SystemGenerated,
}

public enum MonsterLocomotion
{
    Ground,
    Burrow,
    Climb,
    Fly,
    Swim,
}

public enum MonsterSpecialAbility
{
    None,
    PackHunter,
    Skirmisher,
    ScentTracker,
    Venomous,
    DarknessAura,
    EchoHunter,
}

public enum MonsterQuality
{
    Beastlike,
    Avian,
    Amphibious,
    Demonic,
    Arachnid,
    Ooze,
    Insectoid,
    Draconic,
    Plantlike,
    Elephantine,
    Undead,
    Crystalline,
    Humanoid,
    Angelic,
    Spectral,
    Stonecarved,
    Serpentine,
    Elemental,
    Piscine,
    Reptilian,
}

public enum MonsterStrength
{
    PlusOneAttack,
    AbsorbsMagic,
    Swarm,
    OneD10Damage,
    PoisonSting,
    ConfusingGaze,
    EatsMetal,
    RangedAttacks,
    HighlyIntelligent,
    CrushingGrasp,
    PsychicBlast,
    Stealthy,
    PetrifyingGaze,
    OneD12Damage,
    Impersonation,
    BlindingAura,
    TurnsInvisible,
    TwoD6Damage,
    SwallowsWhole,
    PlusTwoAttacks,
}

public enum MonsterWeakness
{
    Cold,
    Greed,
    Light,
    Salt,
    Vanity,
    Mirrors,
    Electricity,
    FragileBody,
    Sunlight,
    Silver,
    Fire,
    Food,
    Acid,
    Garlic,
    Iron,
    Water,
    TrueName,
    LoudSounds,
    HolyWater,
    Music,
}

/// <summary>The first mutation column, used for a one-mutation boss.</summary>
public enum MonsterMutation
{
    Shapechanger,
    FinsAndGills,
    InsulatingFur,
    IronlikeScales,
    ExtraLimbs,
    Tentacles,
    Boneless,
    Gigantic,
    FlingsSpikes,
    TwoHeads,
    Burrows,
    Wings,
}

/// <summary>The four independent d20 results behind a generated monster.</summary>
public sealed record MonsterGeneratorRoll(
    int CombatRoll,
    int CombatAdjustment,
    int QualityRoll,
    MonsterQuality Quality,
    int StrengthRoll,
    MonsterStrength Strength,
    int WeaknessRoll,
    MonsterWeakness Weakness);

/// <summary>
/// Immutable gameplay content for one monster type. A saved actor carries its
/// type id; this catalogue supplies everything true of that kind of creature.
/// </summary>
public sealed record MonsterProfile(
    string TypeId,
    string Name,
    MonsterOrigin Origin,
    string Source,
    int Level,
    int ArmorClass,
    int HitPoints,
    int MaximumConcussion,
    ArmorType Armor,
    int DefenseBonus,
    string AttackName,
    AttackProfile Attack,
    int AttackBonus,
    int AttackCount,
    string DamageDice,
    int MovementFeetPerRound,
    MonsterLocomotion Locomotion,
    AbilityScores Abilities,
    MonsterSpecialAbility SpecialAbility,
    string SpecialDescription,
    MonsterWeakness Weakness,
    string WeaknessDescription,
    CreatureVoice Voice,
    string LootTypeId,
    string LootName,
    MonsterGeneratorRoll? GeneratorRoll,
    MonsterMutation? Mutation,
    string MutationDescription)
{
    public int MovementStepsPerRound =>
        MovementFeetPerRound / MovementAllowance.FeetPerTile;

    /// <summary>Mutations only raise the level used to roll treasure.</summary>
    public int TreasureLevel => checked(Level + (Mutation is null ? 0 : 2));

    public string BaseTypeId => Mutation is null
        ? TypeId
        : TypeId[..TypeId.LastIndexOf(".mutation.", StringComparison.Ordinal)];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TypeId) ||
            !TypeId.StartsWith("monster.", StringComparison.Ordinal))
        {
            throw new ArgumentException("A monster needs a monster.* type id.");
        }
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Source) ||
            string.IsNullOrWhiteSpace(AttackName) ||
            string.IsNullOrWhiteSpace(SpecialDescription) ||
            string.IsNullOrWhiteSpace(WeaknessDescription) ||
            string.IsNullOrWhiteSpace(LootTypeId) ||
            string.IsNullOrWhiteSpace(LootName))
        {
            throw new ArgumentException("A monster profile has blank authored content.");
        }
        if (Level != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Level), Level, "This catalogue is the level-one pool.");
        }
        if (ArmorClass != 10 + DefenseBonus)
        {
            throw new ArgumentException("AC and intrinsic DEF disagree.");
        }
        if (HitPoints is < 1 or > 20 || MaximumConcussion != HitPoints * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(HitPoints));
        }
        if (DefenseBonus is < 0 or > 6 || AttackBonus is < -2 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(DefenseBonus));
        }
        if (AttackCount != 1 ||
            DamageDice is not ("1d4" or "1d6" or "1d6+1" or "1d8" or
                "1d10" or "1d12" or "2d6"))
        {
            throw new ArgumentOutOfRangeException(nameof(AttackCount));
        }
        if (MovementFeetPerRound is < 5 or > 60 ||
            MovementFeetPerRound % MovementAllowance.FeetPerTile != 0 ||
            CombatClock.BeatsIn(AttackSpeed.RoundMilliseconds) %
                MovementStepsPerRound != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MovementFeetPerRound));
        }
        if (!Enum.IsDefined(Origin) || !Enum.IsDefined(Armor) ||
            !Enum.IsDefined(Locomotion) || !Enum.IsDefined(SpecialAbility) ||
            !Enum.IsDefined(Weakness) || !Enum.IsDefined(Voice) ||
            Mutation is { } mutation && !Enum.IsDefined(mutation))
        {
            throw new ArgumentException("A monster profile contains an unknown enum value.");
        }
        if (Origin == MonsterOrigin.SystemGenerated && GeneratorRoll is null ||
            Origin == MonsterOrigin.Adapted && GeneratorRoll is not null)
        {
            throw new ArgumentException("Only system-generated monsters carry generator rolls.");
        }
        if (Mutation is null != string.IsNullOrEmpty(MutationDescription))
        {
            throw new ArgumentException("Mutation and mutation description disagree.");
        }
        if (Abilities.InOrder.Any(value => value is < 3 or > 20))
        {
            throw new ArgumentOutOfRangeException(nameof(Abilities));
        }
        Attack.Validate();
    }
}

/// <summary>Five adaptations and five stable level-one generator outputs.</summary>
public static class MonsterCatalog
{
    public const ulong GeneratedCatalogSeed = 20260801;
    private const string MutationMarker = ".mutation.";

    public static IReadOnlyList<MonsterProfile> Adapted { get; } =
    [
        AdaptedMonster(
            "monster.bandit", "Bandit", "Shadowdark Bandit", 13, 4,
            ArmorType.Leather, "club", "1d4", 1,
            AttackCategoryId.OneHandedConcussion, CriticalTableId.Crush,
            AttackSize.Medium, 1, 30, MonsterLocomotion.Ground,
            new AbilityScores(12, 10, 10, 8, 10, 8),
            MonsterSpecialAbility.Skirmisher,
            "Withdraws one step after a missed melee attack when space permits.",
            MonsterWeakness.Greed,
            "Visible treasure can distract, bribe, or lure it.",
            CreatureVoice.Hail, "item.bandit-purse", "Bandit's Purse"),
        AdaptedMonster(
            "monster.beastman", "Beastman", "Shadowdark Beastman", 12, 5,
            ArmorType.Leather, "spear", "1d6+1", 1,
            AttackCategoryId.OneHandedSlashing, CriticalTableId.Puncture,
            AttackSize.Medium, 2, 30, MonsterLocomotion.Ground,
            new AbilityScores(14, 12, 12, 6, 12, 8),
            MonsterSpecialAbility.ScentTracker,
            "Tracks a perceived creature beyond an ordinary monster's leash.",
            MonsterWeakness.Fire,
            "Open flame breaks its nerve and masks scent trails.",
            CreatureVoice.Snarl, "item.beastman-totem", "Bone Totem"),
        AdaptedMonster(
            "monster.giant-centipede", "Giant Centipede",
            "Shadowdark Giant Centipede", 11, 4,
            ArmorType.None, "venomous bite", "1d4", 1,
            AttackCategoryId.ToothAndClaw, CriticalTableId.Puncture,
            AttackSize.Small, 1, 30, MonsterLocomotion.Climb,
            new AbilityScores(4, 12, 10, 3, 4, 3),
            MonsterSpecialAbility.Venomous,
            "A damaging bite can deliver a weakening venom.",
            MonsterWeakness.Cold,
            "Cold sharply reduces its speed and aggression.",
            CreatureVoice.Eep, "item.venom-sac", "Venom Sac"),
        AdaptedMonster(
            "monster.darkmantle", "Darkmantle", "Shadowdark Darkmantle", 13, 4,
            ArmorType.None, "bite", "1d4", 1,
            AttackCategoryId.ToothAndClaw, CriticalTableId.Puncture,
            AttackSize.Medium, 3, 30, MonsterLocomotion.Fly,
            new AbilityScores(6, 16, 10, 4, 10, 4),
            MonsterSpecialAbility.DarknessAura,
            "Can smother nearby light before dropping onto prey.",
            MonsterWeakness.Light,
            "Bright light prevents its darkness ambush.",
            CreatureVoice.Keen, "item.darkmantle-membrane", "Shadow Membrane"),
        AdaptedMonster(
            "monster.giant-rat", "Giant Rat", "Shadowdark Giant Rat", 11, 5,
            ArmorType.None, "bite", "1d4", 1,
            AttackCategoryId.ToothAndClaw, CriticalTableId.Puncture,
            AttackSize.Small, 1, 30, MonsterLocomotion.Ground,
            new AbilityScores(6, 12, 12, 6, 12, 6),
            MonsterSpecialAbility.PackHunter,
            "Alerts nearby giant rats and sustains a long pack pursuit.",
            MonsterWeakness.Fire,
            "Fire scatters the pack and denies narrow approaches.",
            CreatureVoice.Eep, "item.giant-rat-tail", "Giant Rat Tail"),
    ];

    public static IReadOnlyList<MonsterProfile> SystemGenerated { get; } =
        MonsterGenerator.GenerateLevelOne(GeneratedCatalogSeed, 5);

    public static IReadOnlyList<MonsterProfile> LevelOne { get; } =
        Adapted.Concat(SystemGenerated)
            .OrderBy(value => value.TypeId, StringComparer.Ordinal)
            .ToArray();

    private static readonly IReadOnlyDictionary<string, MonsterProfile> ByTypeId =
        BuildIndex();

    public static string CanonicalFingerprint { get; } = ComputeFingerprint();

    public static bool TryGet(string typeId, out MonsterProfile profile)
    {
        if (ByTypeId.TryGetValue(typeId, out profile!))
        {
            return true;
        }

        var marker = typeId.LastIndexOf(MutationMarker, StringComparison.Ordinal);
        if (marker <= 0 ||
            !ByTypeId.TryGetValue(typeId[..marker], out var baseProfile) ||
            !MonsterMutations.TryParse(typeId[(marker + MutationMarker.Length)..],
                out var mutation))
        {
            profile = null!;
            return false;
        }

        profile = WithMutation(baseProfile, mutation);
        return true;
    }

    public static MonsterProfile Get(string typeId) =>
        TryGet(typeId, out var profile)
            ? profile
            : throw new InvalidOperationException(
                $"Monster type '{typeId}' has no authored profile.");

    public static MonsterProfile WithMutation(
        MonsterProfile profile,
        MonsterMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Mutation is not null)
        {
            throw new InvalidOperationException("A monster already has a mutation.");
        }

        var slug = MonsterMutations.Slug(mutation);
        var locomotion = mutation switch
        {
            MonsterMutation.FinsAndGills => MonsterLocomotion.Swim,
            MonsterMutation.Burrows => MonsterLocomotion.Burrow,
            MonsterMutation.Wings => MonsterLocomotion.Fly,
            _ => profile.Locomotion,
        };
        var attack = mutation switch
        {
            MonsterMutation.Tentacles => profile.Attack with
            {
                Id = $"{profile.Attack.Id}.tentacles",
                Category = AttackCategoryId.GrapplingUnbalancing,
                CriticalTable = CriticalTableId.Unbalancing,
            },
            MonsterMutation.FlingsSpikes => profile.Attack with
            {
                Id = $"{profile.Attack.Id}.spikes",
                Category = AttackCategoryId.MissileWeapons,
                CriticalTable = CriticalTableId.Puncture,
                MeleeRangeTiles = 8,
            },
            MonsterMutation.Gigantic => profile.Attack with
            {
                Id = $"{profile.Attack.Id}.gigantic",
                Size = AttackSize.Large,
                MaximumCriticalTier =
                    AttackSizeRules.MaximumCriticalTier(AttackSize.Large),
            },
            _ => profile.Attack with { Id = $"{profile.Attack.Id}.{slug}" },
        };

        var result = profile with
        {
            TypeId = $"{profile.TypeId}{MutationMarker}{slug}",
            Name = $"{profile.Name} ({MonsterMutations.DisplayName(mutation)})",
            Source = $"{profile.Source}; boss mutation d12 " +
                $"{MonsterMutations.RollOf(mutation)}",
            Attack = attack,
            Locomotion = locomotion,
            MovementFeetPerRound = mutation == MonsterMutation.Gigantic
                ? 50
                : profile.MovementFeetPerRound,
            Mutation = mutation,
            MutationDescription = MonsterMutations.Description(mutation),
        };
        result.Validate();
        return result;
    }

    private static MonsterProfile AdaptedMonster(
        string typeId,
        string name,
        string source,
        int armorClass,
        int hitPoints,
        ArmorType armor,
        string attackName,
        string damageDice,
        int attackCount,
        AttackCategoryId category,
        CriticalTableId critical,
        AttackSize size,
        int attackBonus,
        int movement,
        MonsterLocomotion locomotion,
        AbilityScores abilities,
        MonsterSpecialAbility special,
        string specialDescription,
        MonsterWeakness weakness,
        string weaknessDescription,
        CreatureVoice voice,
        string lootTypeId,
        string lootName) =>
        new(
            typeId, name, MonsterOrigin.Adapted, source, 1,
            armorClass, hitPoints, hitPoints * 2, armor, armorClass - 10,
            attackName,
            new AttackProfile(
                $"attack.{typeId["monster.".Length..]}.{Slug(attackName)}",
                category,
                critical,
                Size: size,
                MaximumCriticalTier: AttackSizeRules.MaximumCriticalTier(size),
                IsNatural: true),
            attackBonus, attackCount, damageDice, movement,
            locomotion, abilities, special, specialDescription, weakness,
            weaknessDescription, voice, lootTypeId, lootName, null, null, "");

    private static IReadOnlyDictionary<string, MonsterProfile> BuildIndex()
    {
        var result = new Dictionary<string, MonsterProfile>(StringComparer.Ordinal);
        foreach (var profile in LevelOne)
        {
            profile.Validate();
            if (!result.TryAdd(profile.TypeId, profile))
            {
                throw new InvalidOperationException(
                    $"Monster type '{profile.TypeId}' is authored twice.");
            }
        }
        return result;
    }

    private static string ComputeFingerprint()
    {
        var allProfiles = LevelOne.Concat(
            LevelOne.SelectMany(profile =>
                Enum.GetValues<MonsterMutation>()
                    .Select(mutation => WithMutation(profile, mutation))));
        var canonical = string.Join("\n", allProfiles.Select(value =>
            $"{value.TypeId}|{value.Name}|{value.Origin}|{value.Source}|" +
            $"{value.Level}|{value.ArmorClass}|{value.HitPoints}|" +
            $"{value.MaximumConcussion}|{value.Armor}|{value.DefenseBonus}|" +
            $"{value.AttackName}|{value.Attack.Id}|{value.Attack.Category}|" +
            $"{value.Attack.CriticalTable}|{value.Attack.Size}|" +
            $"{value.Attack.MeleeRangeTiles}|{value.AttackBonus}|" +
            $"{value.AttackCount}|{value.DamageDice}|" +
            $"{value.MovementFeetPerRound}|{value.Locomotion}|" +
            $"{string.Join(',', value.Abilities.InOrder)}|" +
            $"{value.SpecialAbility}|{value.SpecialDescription}|" +
            $"{value.Weakness}|{value.WeaknessDescription}|{value.Voice}|" +
            $"{value.LootTypeId}|{value.LootName}|{value.GeneratorRoll}|" +
            $"{value.Mutation}|{value.MutationDescription}|{value.TreasureLevel}"));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static string Slug(string value) =>
        string.Join(
            '-',
            value.ToLowerInvariant()
                .Split([' ', '-', '\''], StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The exact first-column d12 mutation table.</summary>
public static class MonsterMutations
{
    private static readonly MonsterMutation[] FirstColumn =
    [
        MonsterMutation.Shapechanger,
        MonsterMutation.FinsAndGills,
        MonsterMutation.InsulatingFur,
        MonsterMutation.IronlikeScales,
        MonsterMutation.ExtraLimbs,
        MonsterMutation.Tentacles,
        MonsterMutation.Boneless,
        MonsterMutation.Gigantic,
        MonsterMutation.FlingsSpikes,
        MonsterMutation.TwoHeads,
        MonsterMutation.Burrows,
        MonsterMutation.Wings,
    ];

    private static readonly IReadOnlyDictionary<string, MonsterMutation> BySlug =
        Enum.GetValues<MonsterMutation>().ToDictionary(
            Slug, value => value, StringComparer.Ordinal);

    public static MonsterMutation FirstMutation(int d12)
    {
        if (d12 is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(d12));
        }
        return FirstColumn[d12 - 1];
    }

    public static int RollOf(MonsterMutation mutation) =>
        Array.IndexOf(FirstColumn, mutation) + 1;

    public static string Slug(MonsterMutation mutation) =>
        MonsterCatalog.Slug(DisplayName(mutation));

    public static bool TryParse(string slug, out MonsterMutation mutation) =>
        BySlug.TryGetValue(slug, out mutation);

    public static string DisplayName(MonsterMutation mutation) => mutation switch
    {
        MonsterMutation.Shapechanger => "Shapechanger",
        MonsterMutation.FinsAndGills => "Fins and gills",
        MonsterMutation.InsulatingFur => "Insulating fur",
        MonsterMutation.IronlikeScales => "Ironlike scales",
        MonsterMutation.ExtraLimbs => "Extra limbs",
        MonsterMutation.Tentacles => "Tentacles",
        MonsterMutation.Boneless => "Boneless",
        MonsterMutation.Gigantic => "Gigantic",
        MonsterMutation.FlingsSpikes => "Flings spikes",
        MonsterMutation.TwoHeads => "Two heads",
        MonsterMutation.Burrows => "Burrows",
        MonsterMutation.Wings => "Wings",
        _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
    };

    public static string Description(MonsterMutation mutation) => mutation switch
    {
        MonsterMutation.Shapechanger => "Can change its outward form.",
        MonsterMutation.FinsAndGills => "Can breathe and move through water.",
        MonsterMutation.InsulatingFur => "Thick fur insulates its body.",
        MonsterMutation.IronlikeScales => "Its body is covered in ironlike scales.",
        MonsterMutation.ExtraLimbs => "Extra limbs let it manipulate several things at once.",
        MonsterMutation.Tentacles => "Its tentacles can restrain creatures.",
        MonsterMutation.Boneless => "Its boneless body can squeeze through narrow gaps.",
        MonsterMutation.Gigantic => "Its gigantic stride covers double-near ground.",
        MonsterMutation.FlingsSpikes => "It can fling spikes at distant targets.",
        MonsterMutation.TwoHeads => "Two heads watch and act independently.",
        MonsterMutation.Burrows => "It can burrow a near distance.",
        MonsterMutation.Wings => "It can fly a near distance.",
        _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
    };
}

/// <summary>
/// Implements the printed generator. Level-one roster generation constrains
/// only Combat to PL; the other three d20 columns remain independent rolls.
/// </summary>
public static class MonsterGenerator
{
    private sealed record QualityTemplate(
        string AttackName,
        AttackCategoryId Category,
        CriticalTableId Critical,
        AttackSize Size,
        ArmorType Armor,
        MonsterLocomotion Locomotion,
        AbilityScores Abilities,
        CreatureVoice Voice);

    private static readonly int[] CombatAdjustments =
        [-3, -3, -2, -2, -1, -1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4];
    private static readonly MonsterQuality[] Qualities =
        Enum.GetValues<MonsterQuality>();
    private static readonly MonsterStrength[] Strengths =
        Enum.GetValues<MonsterStrength>();
    private static readonly MonsterWeakness[] Weaknesses =
        Enum.GetValues<MonsterWeakness>();

    public static IReadOnlyList<MonsterProfile> GenerateLevelOne(
        ulong seed,
        int count)
    {
        if (seed == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seed));
        }
        if (count is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var dice = new Dice(seed);
        var result = new List<MonsterProfile>(count);
        for (var index = 0; index < count; index++)
        {
            var combatRoll = RollLevelOneCombat(dice);
            var qualityRoll = dice.Roll(20);
            var strengthRoll = dice.Roll(20);
            var weaknessRoll = dice.Roll(20);
            var roll = new MonsterGeneratorRoll(
                combatRoll,
                CombatAdjustmentFor(combatRoll),
                qualityRoll,
                QualityFor(qualityRoll),
                strengthRoll,
                StrengthFor(strengthRoll),
                weaknessRoll,
                WeaknessFor(weaknessRoll));
            result.Add(BuildLevelOne(seed, index, dice, roll));
        }

        foreach (var profile in result)
        {
            profile.Validate();
        }
        return result;
    }

    public static int CombatAdjustmentFor(int d20) =>
        At(CombatAdjustments, d20);

    public static MonsterQuality QualityFor(int d20) => At(Qualities, d20);

    public static MonsterStrength StrengthFor(int d20) => At(Strengths, d20);

    public static MonsterWeakness WeaknessFor(int d20) => At(Weaknesses, d20);

    public static string QualityName(MonsterQuality value) => value switch
    {
        MonsterQuality.Plantlike => "Plantlike",
        _ => value.ToString(),
    };

    public static string StrengthName(MonsterStrength value) => value switch
    {
        MonsterStrength.PlusOneAttack => "+1 attack",
        MonsterStrength.AbsorbsMagic => "Absorbs magic",
        MonsterStrength.Swarm => "Swarm",
        MonsterStrength.OneD10Damage => "1d10 damage",
        MonsterStrength.PoisonSting => "Poison sting",
        MonsterStrength.ConfusingGaze => "Confusing gaze",
        MonsterStrength.EatsMetal => "Eats metal",
        MonsterStrength.RangedAttacks => "Ranged attacks",
        MonsterStrength.HighlyIntelligent => "Highly intelligent",
        MonsterStrength.CrushingGrasp => "Crushing grasp",
        MonsterStrength.PsychicBlast => "Psychic blast",
        MonsterStrength.Stealthy => "Stealthy",
        MonsterStrength.PetrifyingGaze => "Petrifying gaze",
        MonsterStrength.OneD12Damage => "1d12 damage",
        MonsterStrength.Impersonation => "Impersonation",
        MonsterStrength.BlindingAura => "Blinding aura",
        MonsterStrength.TurnsInvisible => "Turns invisible",
        MonsterStrength.TwoD6Damage => "2d6 damage",
        MonsterStrength.SwallowsWhole => "Swallows whole",
        MonsterStrength.PlusTwoAttacks => "+2 attacks",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string WeaknessName(MonsterWeakness value) => value switch
    {
        MonsterWeakness.FragileBody => "Fragile body",
        MonsterWeakness.TrueName => "Its True Name",
        MonsterWeakness.LoudSounds => "Loud sounds",
        MonsterWeakness.HolyWater => "Holy water",
        _ => value.ToString(),
    };

    private static MonsterProfile BuildLevelOne(
        ulong seed,
        int index,
        Dice dice,
        MonsterGeneratorRoll roll)
    {
        var template = TemplateFor(roll.Quality);
        var name = $"{QualityName(roll.Quality)} {StrengthNoun(roll.Strength)}";
        var slug = MonsterCatalog.Slug(name);
        var hitPoints = Math.Max(
            1,
            dice.Roll(8) + ScoreModifier(template.Abilities.Constitution));
        var damageDice = roll.Strength switch
        {
            MonsterStrength.OneD10Damage => "1d10",
            MonsterStrength.OneD12Damage => "1d12",
            MonsterStrength.TwoD6Damage => "2d6",
            _ => "1d8",
        };
        var ranged = roll.Strength is MonsterStrength.RangedAttacks or
            MonsterStrength.PsychicBlast;
        var size = damageDice is "1d12" or "2d6"
            ? AttackSize.Large
            : template.Size;
        var special = roll.Strength switch
        {
            MonsterStrength.Swarm => MonsterSpecialAbility.PackHunter,
            MonsterStrength.PoisonSting => MonsterSpecialAbility.Venomous,
            MonsterStrength.Stealthy or MonsterStrength.TurnsInvisible =>
                MonsterSpecialAbility.Skirmisher,
            _ => MonsterSpecialAbility.None,
        };
        var attackName = ranged ? $"ranged {template.AttackName}" : template.AttackName;
        var typeId = $"monster.generated.{index + 1}-{slug}";

        return new MonsterProfile(
            typeId,
            name,
            MonsterOrigin.SystemGenerated,
            $"Shadowdark generator seed {seed}: Combat d20 {roll.CombatRoll}, " +
                $"Quality d20 {roll.QualityRoll}, Strength d20 {roll.StrengthRoll}, " +
                $"Weakness d20 {roll.WeaknessRoll}",
            1,
            11,
            hitPoints,
            hitPoints * 2,
            template.Armor,
            1,
            attackName,
            new AttackProfile(
                $"attack.generated.{index + 1}-{slug}",
                ranged ? AttackCategoryId.MissileWeapons : template.Category,
                ranged ? CriticalTableId.Puncture : template.Critical,
                Size: size,
                MaximumCriticalTier: AttackSizeRules.MaximumCriticalTier(size),
                MeleeRangeTiles: ranged ? 8 : 1,
                IsNatural: true),
            1,
            1,
            damageDice,
            30,
            template.Locomotion,
            template.Abilities,
            special,
            StrengthDescription(roll.Strength),
            roll.Weakness,
            WeaknessDescription(roll.Weakness),
            template.Voice,
            $"item.generated.{index + 1}-{slug}-organ",
            $"{name} Organ",
            roll,
            null,
            "");
    }

    private static int RollLevelOneCombat(Dice dice)
    {
        int roll;
        do
        {
            roll = dice.Roll(20);
        }
        while (CombatAdjustmentFor(roll) != 0);
        return roll;
    }

    private static int ScoreModifier(int score) =>
        (int)Math.Floor((score - 10) / 2d);

    private static T At<T>(IReadOnlyList<T> table, int d20)
    {
        if (d20 is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(d20));
        }
        return table[d20 - 1];
    }

    private static string StrengthNoun(MonsterStrength value) => value switch
    {
        MonsterStrength.PlusOneAttack => "Ravager",
        MonsterStrength.AbsorbsMagic => "Null",
        MonsterStrength.Swarm => "Brood",
        MonsterStrength.OneD10Damage => "Brute",
        MonsterStrength.PoisonSting => "Stinger",
        MonsterStrength.ConfusingGaze => "Seer",
        MonsterStrength.EatsMetal => "Gnawer",
        MonsterStrength.RangedAttacks => "Spitter",
        MonsterStrength.HighlyIntelligent => "Sage",
        MonsterStrength.CrushingGrasp => "Crusher",
        MonsterStrength.PsychicBlast => "Mind",
        MonsterStrength.Stealthy => "Stalker",
        MonsterStrength.PetrifyingGaze => "Basilisk",
        MonsterStrength.OneD12Damage => "Reaver",
        MonsterStrength.Impersonation => "Mimic",
        MonsterStrength.BlindingAura => "Glare",
        MonsterStrength.TurnsInvisible => "Veil",
        MonsterStrength.TwoD6Damage => "Storm",
        MonsterStrength.SwallowsWhole => "Maw",
        MonsterStrength.PlusTwoAttacks => "Hydra",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static QualityTemplate TemplateFor(MonsterQuality value) => value switch
    {
        MonsterQuality.Beastlike => Template("bite", 14, 12, 12, 4, 12, 6),
        MonsterQuality.Avian => Template("beak", 8, 14, 10, 4, 12, 8,
            locomotion: MonsterLocomotion.Fly, voice: CreatureVoice.Keen),
        MonsterQuality.Amphibious => Template("bite", 12, 12, 12, 6, 10, 6,
            locomotion: MonsterLocomotion.Swim),
        MonsterQuality.Demonic => Template("claw", 14, 12, 14, 10, 10, 12),
        MonsterQuality.Arachnid => Template("bite", 8, 14, 10, 4, 10, 4,
            locomotion: MonsterLocomotion.Climb, size: AttackSize.Small),
        MonsterQuality.Ooze => Template("engulf", 14, 6, 16, 3, 8, 3,
            category: AttackCategoryId.GrapplingUnbalancing,
            critical: CriticalTableId.Unbalancing),
        MonsterQuality.Insectoid => Template("mandibles", 12, 12, 12, 6, 8, 4,
            armor: ArmorType.Leather, locomotion: MonsterLocomotion.Climb),
        MonsterQuality.Draconic => Template("bite", 14, 12, 14, 10, 12, 12,
            armor: ArmorType.Leather),
        MonsterQuality.Plantlike => Template("tendril", 12, 8, 14, 6, 12, 6,
            category: AttackCategoryId.GrapplingUnbalancing,
            critical: CriticalTableId.Unbalancing),
        MonsterQuality.Elephantine => Template("crush", 16, 8, 16, 4, 12, 6,
            size: AttackSize.Large, critical: CriticalTableId.Crush),
        MonsterQuality.Undead => Template("claw", 12, 10, 14, 8, 10, 6,
            voice: CreatureVoice.Keen),
        MonsterQuality.Crystalline => Template("shard", 12, 10, 16, 8, 10, 6,
            armor: ArmorType.Chain, critical: CriticalTableId.Puncture,
            voice: CreatureVoice.Keen),
        MonsterQuality.Humanoid => Template("weapon", 12, 12, 10, 10, 10, 10,
            armor: ArmorType.Leather, category: AttackCategoryId.OneHandedSlashing,
            critical: CriticalTableId.Slash, voice: CreatureVoice.Hail),
        MonsterQuality.Angelic => Template("radiant blade", 12, 14, 12, 12, 14, 14,
            armor: ArmorType.Chain, locomotion: MonsterLocomotion.Fly,
            category: AttackCategoryId.OneHandedSlashing,
            critical: CriticalTableId.Slash, voice: CreatureVoice.Hail),
        MonsterQuality.Spectral => Template("chilling touch", 6, 14, 12, 10, 12, 12,
            locomotion: MonsterLocomotion.Fly, critical: CriticalTableId.Cold,
            voice: CreatureVoice.Keen),
        MonsterQuality.Stonecarved => Template("stone fist", 16, 8, 16, 6, 10, 6,
            armor: ArmorType.Chain, critical: CriticalTableId.Crush),
        MonsterQuality.Serpentine => Template("bite", 10, 14, 10, 6, 12, 6,
            size: AttackSize.Small),
        MonsterQuality.Elemental => Template("elemental slam", 14, 12, 14, 8, 10, 8,
            critical: CriticalTableId.Impact, voice: CreatureVoice.Keen),
        MonsterQuality.Piscine => Template("bite", 12, 12, 12, 4, 10, 4,
            locomotion: MonsterLocomotion.Swim),
        MonsterQuality.Reptilian => Template("claw", 14, 12, 14, 6, 10, 6,
            armor: ArmorType.Leather),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static QualityTemplate Template(
        string attackName,
        int strength,
        int dexterity,
        int constitution,
        int intelligence,
        int wisdom,
        int charisma,
        ArmorType armor = ArmorType.None,
        MonsterLocomotion locomotion = MonsterLocomotion.Ground,
        AttackCategoryId category = AttackCategoryId.ToothAndClaw,
        CriticalTableId critical = CriticalTableId.Slash,
        AttackSize size = AttackSize.Medium,
        CreatureVoice voice = CreatureVoice.Snarl) =>
        new(
            attackName, category, critical, size, armor, locomotion,
            new AbilityScores(
                strength, dexterity, constitution, intelligence, wisdom, charisma),
            voice);

    private static string StrengthDescription(MonsterStrength value) => value switch
    {
        MonsterStrength.PlusOneAttack =>
            "Retains an additional-attack trait for higher-level use; level-one creatures make one attack.",
        MonsterStrength.AbsorbsMagic => "Absorbs magic directed at it.",
        MonsterStrength.Swarm => "Acts as a coordinated swarm.",
        MonsterStrength.OneD10Damage => "Its attack deals 1d10 damage.",
        MonsterStrength.PoisonSting => "A damaging sting can deliver poison.",
        MonsterStrength.ConfusingGaze => "Its gaze can confuse a nearby target.",
        MonsterStrength.EatsMetal => "It consumes unattended and exposed metal.",
        MonsterStrength.RangedAttacks => "Its attacks can reach distant targets.",
        MonsterStrength.HighlyIntelligent => "It reasons and plans with exceptional intelligence.",
        MonsterStrength.CrushingGrasp => "Its grasp can restrain and crush a target.",
        MonsterStrength.PsychicBlast => "It projects a ranged psychic blast.",
        MonsterStrength.Stealthy => "It hides and approaches with exceptional stealth.",
        MonsterStrength.PetrifyingGaze => "Its gaze can petrify a nearby target.",
        MonsterStrength.OneD12Damage => "Its attack deals 1d12 damage.",
        MonsterStrength.Impersonation => "It can impersonate another creature.",
        MonsterStrength.BlindingAura => "A nearby aura can blind creatures.",
        MonsterStrength.TurnsInvisible => "It can turn invisible.",
        MonsterStrength.TwoD6Damage => "Its attack deals 2d6 damage.",
        MonsterStrength.SwallowsWhole => "It can swallow a restrained target whole.",
        MonsterStrength.PlusTwoAttacks =>
            "Retains a two-additional-attacks trait for higher-level use; level-one creatures make one attack.",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string WeaknessDescription(MonsterWeakness value) => value switch
    {
        MonsterWeakness.Cold => "Cold exploits its weakness.",
        MonsterWeakness.Greed => "Displayed valuables can distract or lure it.",
        MonsterWeakness.Light => "Bright light disrupts it.",
        MonsterWeakness.Salt => "Salt disrupts or repels it.",
        MonsterWeakness.Vanity => "Flattery and insults can manipulate its vanity.",
        MonsterWeakness.Mirrors => "Its own reflection unsettles it.",
        MonsterWeakness.Electricity => "Electricity bypasses its protection.",
        MonsterWeakness.FragileBody => "Its body is unusually fragile once struck.",
        MonsterWeakness.Sunlight => "Sunlight suppresses its unnatural abilities.",
        MonsterWeakness.Silver => "Silver bypasses its unnatural protection.",
        MonsterWeakness.Fire => "Fire causes it to retreat or lose its special ability.",
        MonsterWeakness.Food => "Food can distract, bait, or placate it.",
        MonsterWeakness.Acid => "Acid dissolves its protection.",
        MonsterWeakness.Garlic => "Garlic repels it at close range.",
        MonsterWeakness.Iron => "Iron bypasses its unnatural protection.",
        MonsterWeakness.Water => "Water disrupts its form or special ability.",
        MonsterWeakness.TrueName => "Speaking its True Name gives power over it.",
        MonsterWeakness.LoudSounds => "Loud sounds interrupt its special ability.",
        MonsterWeakness.HolyWater => "Holy water burns or repels it.",
        MonsterWeakness.Music => "Music distracts or pacifies it.",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
