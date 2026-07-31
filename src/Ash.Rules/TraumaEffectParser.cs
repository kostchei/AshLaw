using System.Text.RegularExpressions;

namespace Ash.Rules;

/// <summary>
/// Converts trauma prose from the <c>ct_*.csv</c> tables into structured effects,
/// once at import.
/// </summary>
/// <remarks>
/// <para>
/// Build plan M0 task 5 requires that a trauma line the parser cannot fully
/// account for is a build error rather than a silently-dropped effect. The parser
/// therefore records the span of every phrase it consumes, and then scans what is
/// left for <see cref="UnaccountedMechanicRegex"/> — a deliberately small set of
/// markers that can only denote a game mechanic (a signed modifier, a number
/// bound to a mechanical unit, or a state-change verb such as <c>broken</c> or
/// <c>stunned</c>).
/// </para>
/// <para>
/// Residual descriptive prose is expected and allowed: every line opens with an
/// injury description, and phrases like "Artery severed" or "Tendons torn"
/// narrate a mechanic that the accompanying bleed or activity penalty already
/// quantifies. The check targets the actual risk — a mechanic present in the text
/// that produces no <see cref="TraumaEffect"/> — rather than attempting to
/// classify every English phrase.
/// </para>
/// </remarks>
internal static partial class TraumaEffectParser
{
    public static IReadOnlyList<TraumaEffect> Parse(string text, string source)
    {
        var effects = new List<TraumaEffect>();
        var covered = new bool[text.Length];
        var unconditional = text.ToCharArray();

        foreach (Match match in ConditionalClauseRegex().Matches(text))
        {
            var condition = ParseCondition(match.Groups["condition"].Value);
            var clause = match.Groups["clause"];
            ParseClause(Isolate(text, clause.Index, clause.Length), condition, effects, covered);

            // The "If no <thing>:" lead-in is structural, and is accounted for by
            // the condition attached to every effect the clause produced.
            Mark(covered, match.Index, clause.Index - match.Index);
            Blank(unconditional, match.Index, match.Length);
        }

        ParseClause(new string(unconditional), TraumaEffectCondition.Always, effects, covered);
        RequireNoUnaccountedMechanics(text, covered, source);

        return Array.AsReadOnly(effects.ToArray());
    }

    private static void ParseClause(
        string clause,
        TraumaEffectCondition condition,
        ICollection<TraumaEffect> effects,
        bool[] covered)
    {
        foreach (var match in MatchAll(BleedingRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Bleeding,
                Magnitude: ParseInt(match, "amount"),
                AppliesWhen: condition));
        }

        // "N hits per round" must not also read as an additional-hits total, so the
        // bleed spans are blanked before the additional-hits pass. Blanking keeps
        // the string length intact so match indices stay aligned with the original.
        var withoutBleeding = clause.ToCharArray();
        foreach (Match match in BleedingRegex().Matches(clause))
        {
            Blank(withoutBleeding, match.Index, match.Length);
        }

        foreach (var match in MatchAll(AdditionalHitsRegex(), new string(withoutBleeding), covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.AdditionalHits,
                Magnitude: ParseInt(match, "amount"),
                AppliesWhen: condition));
        }

        foreach (var match in MatchAll(ActivityPenaltyRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.ActivityPenalty,
                Magnitude: ParseInt(match, "amount"),
                Duration: ParseOptionalInt(match, "duration"),
                DurationUnit: match.Groups["duration"].Success
                    ? TraumaDurationUnit.Rounds
                    : TraumaDurationUnit.None,
                AppliesWhen: condition));
        }

        foreach (var match in MatchAll(StunRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Stun,
                Duration: ParseInt(match, "duration"),
                DurationUnit: TraumaDurationUnit.Rounds,
                AppliesWhen: condition));
        }

        foreach (var match in MatchAll(UnconsciousRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Unconscious,
                Duration: ParseOptionalInt(match, "duration"),
                DurationUnit: match.Groups["duration"].Success
                    ? TraumaDurationUnit.Hours
                    : TraumaDurationUnit.None,
                AppliesWhen: condition));
        }

        foreach (var _ in MatchAll(ProneRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(TraumaEffectKind.Prone, AppliesWhen: condition));
        }

        foreach (var _ in MatchAll(DeathRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(TraumaEffectKind.Death, AppliesWhen: condition));
        }

        foreach (var match in MatchAll(ForcedMovementRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.ForcedMovement,
                Magnitude: ParseInt(match, "distance"),
                AppliesWhen: condition,
                Detail: Normalize(
                    match.Groups["trailing"].Success
                        ? match.Groups["trailing"].Value
                        : match.Groups["direction"].Value)));
        }

        foreach (var match in MatchAll(DropItemRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.DropHeldItem,
                AppliesWhen: condition,
                Detail: Normalize(match.Groups["item"].Value)));
        }

        foreach (var _ in MatchAll(BreakItemRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.BreakItem,
                AppliesWhen: condition,
                Detail: "shield"));
        }

        foreach (var match in MatchAll(BreakBoneRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.BreakBone,
                AppliesWhen: condition,
                DurationUnit: TraumaDurationUnit.Permanent,
                Detail: Normalize(match.Groups["part"].Value)));
        }

        foreach (var match in MatchAll(DisableLimbRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.DisableLimb,
                AppliesWhen: condition,
                DurationUnit: TraumaDurationUnit.Permanent,
                Detail: Normalize(match.Groups["limb"].Value)));
        }

        foreach (var _ in MatchAll(DestroyEyeRegex(), clause, covered))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.DestroyEye,
                AppliesWhen: condition,
                DurationUnit: TraumaDurationUnit.Permanent,
                Detail: "eye"));
        }
    }

    private static void RequireNoUnaccountedMechanics(string text, bool[] covered, string source)
    {
        var residue = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            residue[index] = covered[index] ? ' ' : text[index];
        }

        var unaccounted = UnaccountedMechanicRegex()
            .Matches(new string(residue))
            .Select(match => match.Value.Trim())
            .ToArray();
        if (unaccounted.Length == 0)
        {
            return;
        }

        throw new RulesDataException(
            $"{source}: trauma text contains mechanical phrase(s) that produce no effect: " +
            $"[{string.Join("; ", unaccounted)}] in \"{text}\". Extend TraumaEffectParser to " +
            "handle them rather than letting them drop.");
    }

    /// <summary>
    /// Runs <paramref name="regex"/> over <paramref name="input"/>, marking every
    /// matched span as accounted for. <paramref name="input"/> must be the same
    /// length as the original trauma text so indices stay aligned.
    /// </summary>
    private static List<Match> MatchAll(Regex regex, string input, bool[] covered)
    {
        var matches = new List<Match>();
        foreach (Match match in regex.Matches(input))
        {
            Mark(covered, match.Index, match.Length);
            matches.Add(match);
        }

        return matches;
    }

    /// <summary>
    /// Returns a string the same length as <paramref name="text"/> containing only
    /// the requested span, with everything else blanked to spaces.
    /// </summary>
    private static string Isolate(string text, int start, int length)
    {
        var isolated = new char[text.Length];
        Array.Fill(isolated, ' ');
        text.AsSpan(start, length).CopyTo(isolated.AsSpan(start));
        return new string(isolated);
    }

    private static void Blank(char[] buffer, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            buffer[index] = ' ';
        }
    }

    private static void Mark(bool[] covered, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            covered[index] = true;
        }
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

    private static TraumaEffectCondition ParseCondition(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "arm armor" => TraumaEffectCondition.NoArmArmor,
            "leg armor" => TraumaEffectCondition.NoLegArmor,
            "helm" => TraumaEffectCondition.NoHelm,
            "shield" => TraumaEffectCondition.NoShield,
            _ => throw new RulesDataException($"Unsupported trauma condition 'If no {value}'."),
        };

    private static int ParseInt(Match match, string group) =>
        int.Parse(match.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static int ParseOptionalInt(Match match, string group) =>
        match.Groups[group].Success ? ParseInt(match, group) : 0;

    [GeneratedRegex(
        @"If no (?<condition>arm armor|leg armor|helm|shield)\s*(?:,|:)\s*(?<clause>.*?)(?=(?:\s+If no )|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConditionalClauseRegex();

    [GeneratedRegex(
        @"(?<amount>\d+)\s+hits?(?:\s+per\s+round|/(?:rnd|rd))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BleedingRegex();

    [GeneratedRegex(
        @"\+(?<amount>\d+)\s+hits?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdditionalHitsRegex();

    [GeneratedRegex(
        @"-(?<amount>\d+)\s+(?:to\s+activity|act)\b(?:\s+for\s+(?<duration>\d+)\s+(?:rounds?|rnds?))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActivityPenaltyRegex();

    [GeneratedRegex(
        @"\b(?:stunned|stun)(?:\s+for)?\s+(?<duration>\d+)\s+(?:rounds?|rds?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StunRegex();

    [GeneratedRegex(
        @"\b(?:unconscious|knocked out)(?:\s+for\s+(?<duration>\d+)\s+hours?)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnconsciousRegex();

    [GeneratedRegex(
        @"\b(?:knocked down|prone position|falls?\s+prone|down and unconscious)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProneRegex();

    [GeneratedRegex(
        @"\b(?:dies instantly|instant death|(?:skull|chest|head|body|heart|throat)?\s*crushed)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeathRegex();

    [GeneratedRegex(
        @"(?:(?<direction>knocked(?:\s+back)?|sideways)\s+(?<distance>\d+)\s*(?:feet|foot|ft|')(?:\s+(?<trailing>sideways|backwards?|forwards?))?|(?:reduces?\s+(?:target's\s+)?speed\s+by|speed\s+is\s+reduced\s+by)\s+(?<distance>\d+)\s*(?:feet|foot|ft|'))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForcedMovementRegex();

    [GeneratedRegex(
        @"\b(?:drop|drops?)\s+(?<item>weapon|shield|held item|item|anything carried in hands|all items)\b|(?<item>shield)\s+torn away",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DropItemRegex();

    [GeneratedRegex(
        @"\b(?:breaks shield|shield broken|shield destroyed|armor broken|armor destroyed)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakItemRegex();

    /// <summary>
    /// A broken bone stated on its own. The negative lookahead defers "arm broken
    /// &amp; useless" to <see cref="DisableLimbRegex"/>, which models the stronger
    /// outcome.
    /// </summary>
    [GeneratedRegex(
        @"\bbreaks?\s+(?:bone\s+in\s+)?(?<part>(?:lower\s+|upper\s+)?(?:legs?|arms?)|hip|shoulder|ribs?|collarbone|jaw|elbow|knee|forearm)\b|\b(?<part>bone|hip|shoulder|ribs?|skull|collarbone|jaw|(?:lower\s+|upper\s+)?(?:legs?|arms?)|forearm|elbow|knee)\s+broken\b(?!\s*(?:&|and)\s*useless)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakBoneRegex();

    [GeneratedRegex(
        @"\b(?:(?<limb>arm|leg)\s+(?:broken(?:\s*[&]\s*|\s+and\s+))?useless|sever\s+(?<limb>hand)|shatter\s+(?<limb>elbow|knee))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisableLimbRegex();

    [GeneratedRegex(
        @"\b(?:destroys one eye|blinded|blinds)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DestroyEyeRegex();

    /// <summary>
    /// Markers that can only denote an unparsed game mechanic: a signed modifier,
    /// a number bound to a mechanical unit, a state-change verb, or an "If no ..."
    /// lead-in whose condition was not recognised. Injury nouns ("severed",
    /// "torn", "damage") are deliberately excluded — they narrate mechanics that
    /// the numeric effects on the same line already quantify.
    /// </summary>
    /// <remarks>
    /// The <c>if no</c> marker matters because an unrecognised condition would
    /// otherwise degrade silently: <see cref="ConditionalClauseRegex"/> would not
    /// match it, the clause would be parsed as unconditional, and its effects
    /// would apply unconditionally instead of being gated.
    /// </remarks>
    [GeneratedRegex(
        @"[+-]\s*\d+|\b\d+\s*(?:hits?|rounds?|rnds?|rds?|hours?|feet|foot|ft|')|\bif\s+no\b|\b(?:broken|useless|crushed|stunned|unconscious|prone|dies|death|killed|paralyz(?:ed|es)|blinded|deafened|bleeds?|bleeding|drops?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnaccountedMechanicRegex();
}
