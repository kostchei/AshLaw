using Ash.Sim;
using Godot;

namespace Ash.Game;

/// <summary>
/// The noises NPCs make when they notice you.
/// </summary>
/// <remarks>
/// The cue is the player's warning: it arrives 200 ms before the NPC acts, and
/// it has to say what noticed them before they can see it. So every voice is
/// loaded up front and a missing file is a startup failure — a fight that
/// silently gives no warning is worse than one that refuses to start.
///
/// The cues are loaded with <see cref="AudioStreamOggVorbis.LoadFromFile"/>
/// rather than <c>GD.Load</c> so they need no <c>.import</c> sidecar and no
/// editor pass. See <c>assets/audio/creature-alerts/ATTRIBUTION.md</c>.
/// </remarks>
public sealed partial class AlertVoices : Node
{
    private const string AudioRoot = "res://assets/audio/creature-alerts";

    /// <summary>
    /// How many cues can sound at once. Four monsters can notice you on the
    /// same beat, and the fourth one's warning matters as much as the first's.
    /// </summary>
    private const int Voices = 4;

    private readonly Dictionary<CreatureVoice, AudioStream> _streams = [];
    private readonly List<AudioStreamPlayer> _players = [];
    private int _nextPlayer;

    public override void _Ready()
    {
        foreach (var voice in Enum.GetValues<CreatureVoice>())
        {
            var path = $"{AudioRoot}/{CreatureVoices.AssetName(voice)}.ogg";
            if (!Godot.FileAccess.FileExists(path))
            {
                throw new InvalidOperationException(
                    $"The alert cue for {voice} is missing from {path}. Every " +
                    "voice needs its sound: the cue is the only warning the " +
                    "player gets before the blow.");
            }

            _streams[voice] = AudioStreamOggVorbis.LoadFromFile(path);
        }

        for (var index = 0; index < Voices; index++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _players.Add(player);
        }
    }

    public void Play(CreatureVoice voice)
    {
        if (_players.Count == 0)
        {
            throw new InvalidOperationException(
                "The alert voices were asked to speak before _Ready built them.");
        }

        // Round-robin, so a burst of alerts overlaps instead of the last one
        // cutting off the ones before it.
        var player = _players[_nextPlayer];
        _nextPlayer = (_nextPlayer + 1) % _players.Count;
        player.Stream = _streams[voice];
        player.Play();
    }
}
