using System.Security.Cryptography;
using System.Text;

namespace Ash.Sim;

/// <summary>
/// Fingerprints for the content a save names but does not carry.
/// </summary>
/// <remarks>
/// A save stores a projectile's definition id and a spell's id, not the numbers
/// behind them, so retuning an arrow's range or a bolt's critical cap silently
/// changes what a loaded world does. Folding that content into the world's
/// fingerprint makes the mismatch a refusal at load rather than a fight that
/// resolves differently than the one that was saved.
/// </remarks>
public static class ContentIdentity
{
    /// <summary>Every authored projectile and spell, as one stable hash.</summary>
    public static string ProjectilesAndSpells { get; } = Compute();

    private static string Compute()
    {
        var lines = new List<string>();
        foreach (var definition in ProjectileCatalog.Default.All)
        {
            lines.Add(
                $"{definition.Id}|{definition.Kind}|{definition.Attack.Id}|" +
                $"{definition.Attack.Category}|{definition.Attack.CriticalTable}|" +
                $"{definition.Attack.WeaponAttackModifier}|{definition.Attack.Size}|" +
                $"{definition.Attack.MaximumCriticalTier}|" +
                $"{definition.SpeedTilesPerBeat}|{definition.RangeTiles}|" +
                $"{definition.BlastRadiusTiles}|{definition.RecoveredTypeId}");
        }

        foreach (var spell in SpellCatalog.All)
        {
            lines.Add(
                $"{spell.Id}|{spell.Tradition}|{spell.Targeting}|{spell.Category}|" +
                $"{spell.CriticalTable}|{spell.WindUpMilliseconds}|{spell.RangeTiles}|" +
                $"{spell.RadiusTiles}|{spell.ReagentTypeId}|{spell.ReagentQuantity}|" +
                $"{spell.Size}|{spell.MaximumCriticalTier}");
        }

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        string.Join("\n", lines.Order(StringComparer.Ordinal)))))
            .ToLowerInvariant();
    }
}
