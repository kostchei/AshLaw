using Ash.Rules;

namespace Ash.Sim;

/// <summary>
/// What an actor brings to a fight, derived from the authoritative object
/// graph: their scores and class, and whatever they are actually wearing and
/// holding at this instant.
/// </summary>
/// <remarks>
/// The rules service takes derived numbers and owns no inventory; this is the
/// derivation. Nothing here caches — equipment is world state, and a sheet is a
/// reading of it, not a copy that can go stale.
/// </remarks>
public readonly record struct ActorSheet(
    ObjectId ActorId,
    AbilityScores Abilities,
    CharacterClass Class,
    int Level,
    int ClassAttackModifier,
    int AttackModifier,
    int WeaponAttackModifier,
    int WeaponQualityModifier,
    Ability GoverningAbility,
    ObjectId Weapon,
    AttackProfile? AttackProfile,
    bool WeaponIsFinesse,
    ArmorType Armor,
    int DefenseModifier,
    int ShieldModifier)
{
    public bool IsUnarmed => Weapon.IsNone;
}

/// <summary>
/// Reads actor sheets from the object store.
/// </summary>
public sealed class ActorSheets
{
    private readonly ObjectStore _objects;
    private readonly ClassProgressionTable _progression;
    private readonly AbilityBonusTable _bonuses;
    private readonly CombatProfileCatalog _profiles;

    public ActorSheets(
        ObjectStore objects,
        ClassProgressionTable progression,
        AbilityBonusTable bonuses,
        CombatProfileCatalog? profiles = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _progression = progression ??
            throw new ArgumentNullException(nameof(progression));
        _bonuses = bonuses ?? throw new ArgumentNullException(nameof(bonuses));
        _profiles = profiles ?? CombatProfileCatalog.Default;
    }

    public ActorSheet For(ObjectId actorId)
    {
        var actor = _objects.Get(actorId);
        if (!actor.HasFlag(ObjectFlags.Actor))
        {
            throw new InvalidOperationException(
                $"{actor.Name} is not an actor and has no sheet.");
        }

        var abilities = actor.Abilities;
        var hasMonsterProfile = MonsterCatalog.TryGet(
            actor.TypeId,
            out var monsterProfile);
        var worn = Worn(actorId);
        var weapon = Weapon(worn);
        var shield = Shield(worn, weapon);
        var finesse = !weapon.Id.IsNone && weapon.HasFlag(ObjectFlags.Finesse);
        var attackProfile = weapon.Id.IsNone
            ? hasMonsterProfile
                ? monsterProfile.Attack
                : _profiles.NaturalFor(actor.TypeId)
            : _profiles.TryWeapon(weapon.TypeId, out var equippedProfile)
                ? equippedProfile
                : null;
        var weaponAttackModifier = attackProfile?.WeaponAttackModifier ?? 0;
        var weaponQualityModifier = weapon.Id.IsNone ? 0 : weapon.Quality;

        // An unlevelled creature has no class progression: what it brings is
        // its body and its gear.
        var classAttack = actor.Level > 0 && !hasMonsterProfile
            ? _progression.GetAttackModifier(actor.Class, actor.Level)
            : 0;
        var shieldModifier = shield.Id.IsNone ? 0 : shield.DefenseBonus;
        var baseDefense = worn
            .Where(item =>
                item.Id != shield.Id &&
                !item.HasFlag(ObjectFlags.Broken))
            .Sum(item => item.DefenseBonus);

        var armor = hasMonsterProfile
            ? monsterProfile.Armor
            : ArmorWorn(worn);
        var defense = hasMonsterProfile
            ? monsterProfile.DefenseBonus
            : DefenseDerivation.DefenseModifier(
                armor,
                baseDefense,
                _bonuses.BonusOf(abilities, Ability.Dexterity),
                _bonuses.BonusOf(abilities, Ability.Strength),
                shieldModifier);
        var attack = hasMonsterProfile
            ? monsterProfile.AttackBonus
            : AttackDerivation.AttackModifier(
                classAttack,
                AttackDerivation.WeaponAbilityBonus(
                    _bonuses,
                    abilities,
                    finesse),
                checked(weaponAttackModifier + weaponQualityModifier));

        return new ActorSheet(
            actorId,
            abilities,
            actor.Class,
            actor.Level,
            classAttack,
            attack,
            weaponAttackModifier,
            weaponQualityModifier,
            AttackDerivation.GoverningAbility(_bonuses, abilities, finesse),
            weapon.Id,
            attackProfile,
            finesse,
            armor,
            defense,
            shieldModifier);
    }

    private IReadOnlyList<WorldObject> Worn(ObjectId actorId) =>
        _objects.Enumerate()
            .Where(value =>
                value.Location.Kind == LocationKind.Equipped &&
                value.Location.Parent == actorId)
            .OrderBy(value => value.Location.Slot)
            .ToArray();

    /// <summary>
    /// What the actor is fighting with: the right hand first, then the left.
    /// </summary>
    private WorldObject Weapon(IReadOnlyList<WorldObject> worn)
    {
        foreach (var slot in new[]
                 {
                     EquipmentSlot.RightHand,
                     EquipmentSlot.LeftHand,
                 })
        {
            foreach (var item in worn)
            {
                if (item.Location.Slot == (byte)slot &&
                    item.HasFlag(ObjectFlags.Weapon) &&
                    !item.HasFlag(ObjectFlags.Broken) &&
                    _profiles.TryWeapon(item.TypeId, out _))
                {
                    return item;
                }
            }
        }

        return default;
    }

    /// <summary>
    /// A shield is what is in a hand for defence rather than offence: it has a
    /// defence bonus and is not the weapon.
    /// </summary>
    private static WorldObject Shield(
        IReadOnlyList<WorldObject> worn,
        WorldObject weapon)
    {
        foreach (var item in worn)
        {
            if (item.Id != weapon.Id &&
                item.DefenseBonus > 0 &&
                !item.HasFlag(ObjectFlags.Broken) &&
                item.Location.Slot is (byte)EquipmentSlot.LeftHand
                    or (byte)EquipmentSlot.RightHand)
            {
                return item;
            }
        }

        return default;
    }

    /// <summary>The suit on the body decides the armour category.</summary>
    private static ArmorType ArmorWorn(IReadOnlyList<WorldObject> worn)
    {
        foreach (var item in worn)
        {
            if (item.Location.Slot == (byte)EquipmentSlot.Body &&
                !item.HasFlag(ObjectFlags.Broken))
            {
                return item.ArmorType;
            }
        }

        return ArmorType.None;
    }
}
