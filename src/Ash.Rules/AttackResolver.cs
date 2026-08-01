namespace Ash.Rules;

public static class AttackResolver
{
    public static AttackResult Resolve(RulesData rules, AttackRequest request)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (request.RawD20 is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.RawD20,
                "Raw d20 must be between 1 and 20.");
        }

        if (request.AttackSize is { } size && !Enum.IsDefined(size))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                size,
                "Attack size must be a defined value.");
        }

        if (request.MaximumCriticalTier is { } maximumTier &&
            !Enum.IsDefined(maximumTier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                maximumTier,
                "Maximum critical tier must be a defined value.");
        }

        var table = rules.GetAttackTable(request.AttackCategory);
        if (!table.IsSourceValidated && !request.AllowUnvalidatedTable)
        {
            throw new RulesResolutionException(
                $"Attack category '{table.StableId}' has no supplied source chart, so its " +
                "damage curve, hit threshold, and critical thresholds are unverified authored " +
                "values. This table is deliberately deferred, not accepted -- see " +
                "vendor/ash-v1-rules/PROVENANCE.md. Set AttackRequest.AllowUnvalidatedTable " +
                "to resolve against it anyway.");
        }

        var target = table.ForArmor(request.Armor);
        int uncappedNetRoll;
        try
        {
            uncappedNetRoll = checked(
                request.RawD20 +
                request.AttackModifier -
                request.DefenseModifier);
        }
        catch (OverflowException exception)
        {
            throw new RulesResolutionException(
                $"Net roll overflowed for raw {request.RawD20}, attack modifier " +
                $"{request.AttackModifier}, defense modifier {request.DefenseModifier}: " +
                exception.Message);
        }

        var netRoll = request.AttackSize is { } attackSize
            ? Math.Min(
                uncappedNetRoll,
                AttackSizeRules.MaximumNetRoll(attackSize))
            : uncappedNetRoll;

        var margin = checked(netRoll - target.HitThreshold);
        var messages = new List<string>
        {
            FormattableString.Invariant($"Net roll = {request.RawD20} + {request.AttackModifier} - {request.DefenseModifier} = {uncappedNetRoll}."),
        };
        if (netRoll != uncappedNetRoll)
        {
            messages.Add(
                $"{request.AttackSize} attack caps the net roll at {netRoll}.");
        }

        var isSpell = request.IsSpell ||
            request.AttackCategory is AttackCategoryId.SpellBolts or AttackCategoryId.SpellBalls;
        if (isSpell && request.RawD20 == rules.Spellcasting.MishapRawD20)
        {
            messages.Add(
                $"{rules.Spellcasting.MishapEffect}: {rules.Spellcasting.MishapConsequence}");
            return new AttackResult
            {
                Hit = false,
                RawD20 = request.RawD20,
                NetRoll = netRoll,
                Margin = margin,
                ConcussionHits = 0,
                Mishap = true,
                Messages = Array.AsReadOnly(messages.ToArray()),
            };
        }

        var hit = netRoll >= target.HitThreshold;
        var damageDistance = Math.Max(0, netRoll - target.DamageOrigin);
        var concussionHits = hit
            ? checked((int)decimal.Floor(
                damageDistance * target.Multiplier +
                damageDistance * damageDistance * target.Quadratic))
            : 0;
        messages.Add(
            FormattableString.Invariant(
                $"Hits = max(0, floor({target.Multiplier} * z + {target.Quadratic} * z^2)), where z = max(0, {netRoll} - {target.DamageOrigin}) = {damageDistance}; hits = {concussionHits}."));

        CriticalTier? criticalTier = null;
        CriticalTableId? criticalTable = null;
        int? traumaIndex = null;
        CriticalOutcome? outcome = null;

        if (hit && netRoll >= target.BaseCriticalA)
        {
            var numericTier = checked((int)decimal.Floor(
                (netRoll - target.BaseCriticalA) / target.CriticalInterval));
            var resolvedTier = (CriticalTier)Math.Min(
                numericTier,
                (int)CriticalTier.E);
            var sizeMaximum = request.AttackSize is { } sized
                ? AttackSizeRules.MaximumCriticalTier(sized)
                : CriticalTier.E;
            var requestedMaximum = request.MaximumCriticalTier ?? CriticalTier.E;
            criticalTier = (CriticalTier)Math.Min(
                (int)resolvedTier,
                Math.Min((int)sizeMaximum, (int)requestedMaximum));
            criticalTable = request.CriticalTable ??
                table.DefaultCriticalTable;
            if (criticalTable is null)
            {
                throw new RulesResolutionException(
                    $"Attack category '{table.StableId}' has critical type " +
                    $"'{table.DefaultCriticalType}', which does not select one physical table. " +
                    "Supply AttackRequest.CriticalTable from the weapon, creature attack, or spell.");
            }

            traumaIndex = request.RawD20 % 10;
            if (traumaIndex == 0)
            {
                traumaIndex = 10;
            }

            outcome = rules.GetCriticalOutcome(
                criticalTable.Value,
                criticalTier.Value,
                traumaIndex.Value);
            messages.Add(
                FormattableString.Invariant($"Critical tier = floor(({netRoll} - {target.BaseCriticalA}) / {target.CriticalInterval}) = {numericTier}, capped at Tier {criticalTier}."));
            messages.Add(
                $"Trauma = {criticalTable} Tier {criticalTier}, index {traumaIndex}: {outcome.Text}");
        }
        else
        {
            messages.Add(
                FormattableString.Invariant($"No critical: net roll {netRoll} is below Base Crit A {target.BaseCriticalA}."));
        }

        return new AttackResult
        {
            Hit = hit,
            RawD20 = request.RawD20,
            NetRoll = netRoll,
            Margin = margin,
            ConcussionHits = concussionHits,
            CriticalTier = criticalTier,
            CriticalTable = criticalTable,
            TraumaIndex = traumaIndex,
            TraumaText = outcome?.Text,
            TraumaEffects = outcome?.Effects ?? Array.Empty<TraumaEffect>(),
            Mishap = false,
            Messages = Array.AsReadOnly(messages.ToArray()),
        };
    }
}
