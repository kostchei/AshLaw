using System.Text.RegularExpressions;

namespace Ash.Rules;

internal static partial class TraumaEffectParser
{
    public static IReadOnlyList<TraumaEffect> Parse(string text)
    {
        var effects = new List<TraumaEffect>();
        var unconditionalText = ConditionalClauseRegex().Replace(
            text,
            match =>
            {
                var condition = ParseCondition(match.Groups["condition"].Value);
                ParseClause(match.Groups["clause"].Value, condition, effects);
                return string.Empty;
            });

        ParseClause(unconditionalText, TraumaEffectCondition.Always, effects);
        return Array.AsReadOnly(effects.ToArray());
    }

    private static void ParseClause(
        string clause,
        TraumaEffectCondition condition,
        ICollection<TraumaEffect> effects)
    {
        foreach (Match match in BleedingRegex().Matches(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Bleeding,
                Magnitude: ParseInt(match, "amount"),
                AppliesWhen: condition));
        }

        var withoutBleeding = BleedingRegex().Replace(clause, string.Empty);
        foreach (Match match in AdditionalHitsRegex().Matches(withoutBleeding))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.AdditionalHits,
                Magnitude: ParseInt(match, "amount"),
                AppliesWhen: condition));
        }

        foreach (Match match in ActivityPenaltyRegex().Matches(clause))
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

        foreach (Match match in StunRegex().Matches(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Stun,
                Duration: ParseInt(match, "duration"),
                DurationUnit: TraumaDurationUnit.Rounds,
                AppliesWhen: condition));
        }

        foreach (Match match in UnconsciousRegex().Matches(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Unconscious,
                Duration: ParseOptionalInt(match, "duration"),
                DurationUnit: match.Groups["duration"].Success
                    ? TraumaDurationUnit.Hours
                    : TraumaDurationUnit.None,
                AppliesWhen: condition));
        }

        if (ProneRegex().IsMatch(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Prone,
                AppliesWhen: condition));
        }

        if (DeathRegex().IsMatch(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.Death,
                AppliesWhen: condition));
        }

        foreach (Match match in ForcedMovementRegex().Matches(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.ForcedMovement,
                Magnitude: ParseInt(match, "distance"),
                AppliesWhen: condition,
                Detail: match.Groups["direction"].Value.Trim()));
        }

        foreach (Match match in DropItemRegex().Matches(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.DropHeldItem,
                AppliesWhen: condition,
                Detail: match.Groups["item"].Value.Trim().ToLowerInvariant()));
        }

        if (BreakItemRegex().IsMatch(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.BreakItem,
                AppliesWhen: condition,
                Detail: "shield"));
        }

        if (DisableLimbRegex().IsMatch(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.DisableLimb,
                AppliesWhen: condition,
                DurationUnit: TraumaDurationUnit.Permanent,
                Detail: DisableLimbRegex().Match(clause).Groups["limb"].Value.ToLowerInvariant()));
        }

        if (DestroyEyeRegex().IsMatch(clause))
        {
            effects.Add(new TraumaEffect(
                TraumaEffectKind.DestroyEye,
                AppliesWhen: condition,
                DurationUnit: TraumaDurationUnit.Permanent,
                Detail: "eye"));
        }
    }

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
        @"\b(?:knocked down|prone position|fall prone|down and unconscious)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProneRegex();

    [GeneratedRegex(
        @"\b(?:dies instantly|instant death|skull crushed)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeathRegex();

    [GeneratedRegex(
        @"(?<direction>knocked(?:\s+back)?|sideways)\s+(?<distance>\d+)\s*(?:feet|foot|ft|')",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForcedMovementRegex();

    [GeneratedRegex(
        @"\b(?:drop|drops?)\s+(?<item>weapon|shield|anything carried in hands|all items)\b|(?<item>shield)\s+torn away",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DropItemRegex();

    [GeneratedRegex(
        @"\bbreaks shield\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakItemRegex();

    [GeneratedRegex(
        @"\b(?:(?<limb>arm|leg)\s+(?:broken(?:\s*[&]\s*|\s+and\s+))?useless|sever\s+(?<limb>hand)|shatter\s+(?<limb>elbow|knee))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisableLimbRegex();

    [GeneratedRegex(
        @"\bdestroys one eye\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DestroyEyeRegex();
}

