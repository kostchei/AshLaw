namespace Ash.Sim;

/// <summary>Authored concussion and wound pools for playable monsters.</summary>
public readonly record struct MonsterBodyProfile(
    int MaximumConcussion,
    int MaximumWounds);

/// <summary>
/// Combat bodies tuned against resolver-scale melee damage. These pools are
/// intentionally not damage caps: a large result spills through concussion
/// into wounds and the death clock under the normal vitality rules.
/// </summary>
public static class CombatBalance
{
    public static MonsterBodyProfile DemoMonsterBody(string typeId) => typeId switch
    {
        CombatProfileCatalog.CaveRatTypeId => new(6, 0),
        "monster.goblin-scout" => new(10, 0),
        "monster.goblin-guard" => new(16, 0),
        "monster.many-eyed-tyrant" => new(24, 0),
        _ => throw new ArgumentOutOfRangeException(
            nameof(typeId),
            typeId,
            "The demo monster has no authored combat body."),
    };

    public static MonsterBodyProfile GeneratedMonsterBody(
        string typeId,
        int rank)
    {
        if (rank < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rank));
        }
        return typeId switch
        {
            CombatProfileCatalog.CaveRatTypeId =>
                new(checked(4 + (2 * rank)), 0),
            "monster.goblin-guard" =>
                new(checked(8 + (2 * rank)), 0),
            "monster.many-eyed-tyrant" =>
                new(checked(10 + (3 * rank)), 0),
            _ => throw new ArgumentOutOfRangeException(
                nameof(typeId),
                typeId,
                "The generated monster has no authored combat body."),
        };
    }
}
