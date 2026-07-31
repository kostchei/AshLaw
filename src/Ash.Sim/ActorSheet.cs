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
    Ability GoverningAbility,
    ObjectId Weapon,
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

    public ActorSheets(
        ObjectStore objects,
        ClassProgressionTable progression,
        AbilityBonusTable bonuses)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _progression = progression ??
            throw new ArgumentNullException(nameof(progression));
        _bonuses = bonuses ?? throw new ArgumentNullException(nameof(bonuses));
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
        var worn = Worn(actorId);
        var weapon = Weapon(worn);
        var shield = Shield(worn, weapon);
        var finesse = !weapon.Id.IsNone && weapon.HasFlag(ObjectFlags.Finesse);

        // An unlevelled creature has no class progression: what it brings is
        // its body and its gear.
        var classAttack = actor.Level > 0
            ? _progression.GetAttackModifier(actor.Class, actor.Level)
            : 0;
        var shieldModifier = shield.Id.IsNone ? 0 : shield.DefenseBonus;
        var baseDefense = worn
            .Where(item => item.Id != shield.Id)
            .Sum(item => item.DefenseBonus);

        return new ActorSheet(
            actorId,
            abilities,
            actor.Class,
            actor.Level,
            classAttack,
            AttackDerivation.AttackModifier(
                classAttack,
                AttackDerivation.WeaponAbilityBonus(
                    _bonuses,
                    abilities,
                    finesse)),
            AttackDerivation.GoverningAbility(_bonuses, abilities, finesse),
            weapon.Id,
            finesse,
            ArmorWorn(worn),
            DefenseDerivation.DefenseModifier(
                ArmorWorn(worn),
                baseDefense,
                _bonuses.BonusOf(abilities, Ability.Dexterity),
                _bonuses.BonusOf(abilities, Ability.Strength),
                shieldModifier),
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
    private static WorldObject Weapon(IReadOnlyList<WorldObject> worn)
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
                    item.DefenseBonus == 0)
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
            if (item.Location.Slot == (byte)EquipmentSlot.Body)
            {
                return item.ArmorType;
            }
        }

        return ArmorType.None;
    }
}
