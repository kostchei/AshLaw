namespace Ash.Sim;

/// <summary>
/// The noise a creature makes the moment it decides you are a problem.
/// </summary>
/// <remarks>
/// The alert cue is the player's warning, so it has to say what noticed them
/// before they can see it: vermin squeak, beasts snarl, people speak. It is a
/// sound first and a message second, which is why it names a voice rather than
/// an audio file — the presentation layer owns the file, the simulation owns
/// the fact that a rat has just squeaked at you.
/// </remarks>
public enum CreatureVoice : byte
{
    /// <summary>Vermin: a rat's "eep".</summary>
    Eep = 0,

    /// <summary>Beasts and brutes: a snarl.</summary>
    Snarl = 1,

    /// <summary>People, who greet you before they draw: "hi".</summary>
    Hail = 2,

    /// <summary>Things that are neither, and keen rather than speak.</summary>
    Keen = 3,
}

/// <summary>
/// Which voice a creature type has. An actor that can raise an alarm must have
/// a voice: a silent alert gives the player nothing to react to, so an unmapped
/// type is a content error rather than a quiet creature.
/// </summary>
public static class CreatureVoices
{
    private static readonly Dictionary<string, CreatureVoice> ByTypeId =
        new(StringComparer.Ordinal)
        {
            ["actor.avatar"] = CreatureVoice.Hail,
            ["monster.cave-rat"] = CreatureVoice.Eep,
            ["monster.goblin-scout"] = CreatureVoice.Snarl,
            ["monster.goblin-guard"] = CreatureVoice.Snarl,
            ["monster.many-eyed-tyrant"] = CreatureVoice.Keen,
        };

    /// <summary>Every voice a type id is registered for, in type id order.</summary>
    public static IReadOnlyDictionary<string, CreatureVoice> All => ByTypeId;

    public static CreatureVoice For(string typeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        if (!ByTypeId.TryGetValue(typeId, out var voice))
        {
            throw new InvalidOperationException(
                $"Actor type '{typeId}' has no alert voice. Every actor that " +
                "can raise an alarm needs one, because the cue is how the " +
                "player learns it was noticed.");
        }

        return voice;
    }

    /// <summary>
    /// The cue's asset name, without a directory or extension. The presentation
    /// layer resolves it against its own audio directory.
    /// </summary>
    public static string AssetName(CreatureVoice voice) =>
        voice switch
        {
            CreatureVoice.Eep => "alert-eep",
            CreatureVoice.Snarl => "alert-snarl",
            CreatureVoice.Hail => "alert-hail",
            CreatureVoice.Keen => "alert-keen",
            _ => throw new ArgumentOutOfRangeException(nameof(voice)),
        };

    /// <summary>What the status line says when the cue plays.</summary>
    public static string Utterance(CreatureVoice voice) =>
        voice switch
        {
            CreatureVoice.Eep => "eep!",
            CreatureVoice.Snarl => "a snarl",
            CreatureVoice.Hail => "\"hi\"",
            CreatureVoice.Keen => "a rising keen",
            _ => throw new ArgumentOutOfRangeException(nameof(voice)),
        };
}
