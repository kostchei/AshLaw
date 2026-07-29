using System.Globalization;
using System.Text.Json;

namespace Ash.Rules;

public static class RulesDataLoader
{
    private const string JsonFileName = "combat_system_data.json";
    private const string AttackSummaryFileName = "attack_tables_summary.csv";
    private const string ClassProgressionFileName = "class_progression.csv";

    /// <summary>
    /// Bracket rules from <c>class_progression_tables.md</c> §2. These derive the
    /// curve; <c>class_progression.csv</c> is the authority it must reproduce.
    /// </summary>
    private static readonly ClassProgressionDefinition[] ClassProgressionDefinitions =
    [
        new(
            CharacterClass.Fighter,
            HardCap: 20,
            ProgressionRate.OnePerLevel,
            ProgressionRate.TwoThirdsPerLevel,
            ProgressionRate.OneHalfPerLevel),
        new(
            CharacterClass.Cleric,
            HardCap: 15,
            ProgressionRate.TwoThirdsPerLevel,
            ProgressionRate.OneHalfPerLevel,
            ProgressionRate.OneThirdPerLevel),
        new(
            CharacterClass.Rogue,
            HardCap: 10,
            ProgressionRate.OneHalfPerLevel,
            ProgressionRate.OneThirdPerLevel,
            ProgressionRate.OneThirdPerLevel),
        new(
            CharacterClass.Wizard,
            HardCap: 6,
            ProgressionRate.OneThirdPerLevel,
            ProgressionRate.OneThirdPerLevel,
            ProgressionRate.Capped),
    ];

    private static readonly int[] BracketFirstLevels = [1, 11, 21];

    private static readonly CategoryDefinition[] CategoryDefinitions =
    [
        new(
            AttackCategoryId.OneHandedSlashing,
            "at_1_1_handed_slashing",
            "AT-1"),
        new(
            AttackCategoryId.OneHandedConcussion,
            "at_2_1_handed_concussion",
            "AT-2"),
        new(
            AttackCategoryId.TwoHandedWeapons,
            "at_3_2_handed_weapons",
            "AT-3"),
        new(
            AttackCategoryId.MissileWeapons,
            "at_4_missile_weapons",
            "AT-4"),
        new(
            AttackCategoryId.ToothAndClaw,
            "at_5_tooth_and_claw",
            "AT-5"),
        new(
            AttackCategoryId.GrapplingUnbalancing,
            "at_6_grappling_unbalancing",
            "AT-6"),
        new(
            AttackCategoryId.SpellBolts,
            "at_7_spell_bolts",
            "AT-7"),
        new(
            AttackCategoryId.SpellBalls,
            "at_8_spell_balls",
            "AT-8"),
        new(
            AttackCategoryId.EnvironmentalFallCrush,
            "environmental_fall_crush",
            "ENV"),
    ];

    private static readonly CriticalTableDefinition[] CriticalTableDefinitions =
    [
        new(CriticalTableId.Crush, "ct_1_crush_critical_table.csv"),
        new(CriticalTableId.Slash, "ct_2_slash_critical_table.csv"),
        new(CriticalTableId.Puncture, "ct_3_puncture_critical_table.csv"),
        new(CriticalTableId.Unbalancing, "ct_4_unbalancing_critical_table.csv"),
    ];

    public static RulesData LoadFromDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        try
        {
            var directory = Path.GetFullPath(dataDirectory);
            if (!Directory.Exists(directory))
            {
                throw new RulesDataException($"Rules data directory does not exist: {directory}");
            }

            var jsonPath = Path.Combine(directory, JsonFileName);
            var summaryPath = Path.Combine(directory, AttackSummaryFileName);
            var (attackTables, spellcasting) = LoadJson(jsonPath);

            ValidateAttackSummary(summaryPath, attackTables);
            var criticalOutcomes = LoadCriticalTables(directory);
            var classProgression = LoadClassProgression(
                Path.Combine(directory, ClassProgressionFileName));

            return new RulesData(
                attackTables,
                criticalOutcomes,
                spellcasting,
                classProgression);
        }
        catch (RulesDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or OverflowException)
        {
            throw new RulesDataException(
                $"Failed to load rules data from '{dataDirectory}': {exception.Message}",
                exception);
        }
    }

    private static (
        IDictionary<AttackCategoryId, AttackTable> AttackTables,
        SpellcastingRules Spellcasting)
        LoadJson(string path)
    {
        if (!File.Exists(path))
        {
            throw new RulesDataException($"Missing required rules file: {path}");
        }

        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });

        var root = document.RootElement;
        RequireObject(root, "$");
        RequireExactProperties(root, "$", "combat_system_data");

        var combatData = GetRequiredProperty(root, "combat_system_data", "$");
        RequireObject(combatData, "$.combat_system_data");
        RequireExactProperties(
            combatData,
            "$.combat_system_data",
            "description",
            "attack_tables",
            "spellcasting_rules");
        RequireNonEmptyString(combatData, "description", "$.combat_system_data");

        var attackTablesElement =
            GetRequiredProperty(combatData, "attack_tables", "$.combat_system_data");
        var attackTables = ParseAttackTables(attackTablesElement);
        var spellcasting = ParseSpellcastingRules(
            GetRequiredProperty(combatData, "spellcasting_rules", "$.combat_system_data"));

        return (attackTables, spellcasting);
    }

    private static IDictionary<AttackCategoryId, AttackTable> ParseAttackTables(JsonElement element)
    {
        const string path = "$.combat_system_data.attack_tables";
        RequireObject(element, path);
        RequireExactProperties(
            element,
            path,
            CategoryDefinitions.Select(definition => definition.JsonKey).ToArray());

        var tables = new Dictionary<AttackCategoryId, AttackTable>();
        foreach (var definition in CategoryDefinitions)
        {
            var tablePath = $"{path}.{definition.JsonKey}";
            var tableElement = GetRequiredProperty(element, definition.JsonKey, path);
            RequireObject(tableElement, tablePath);
            RequireExactProperties(
                tableElement,
                tablePath,
                "name",
                "examples",
                "default_crit_type",
                "can_use_shield",
                "description",
                "armor_targets");

            var name = RequireNonEmptyString(tableElement, "name", tablePath);
            var defaultCriticalType =
                RequireNonEmptyString(tableElement, "default_crit_type", tablePath);
            var defaultCriticalTable =
                ParseDefaultCriticalTable(defaultCriticalType, $"{tablePath}.default_crit_type");
            var canUseShield = RequireBoolean(tableElement, "can_use_shield", tablePath);
            RequireNonEmptyString(tableElement, "description", tablePath);
            ValidateExamples(GetRequiredProperty(tableElement, "examples", tablePath), $"{tablePath}.examples");

            var targets = ParseArmorTargets(
                GetRequiredProperty(tableElement, "armor_targets", tablePath),
                $"{tablePath}.armor_targets");

            tables.Add(
                definition.Id,
                new AttackTable(
                    definition.Id,
                    definition.JsonKey,
                    definition.CsvId,
                    name,
                    defaultCriticalType,
                    defaultCriticalTable,
                    canUseShield,
                    targets));
        }

        return tables;
    }

    private static IDictionary<ArmorType, ArmorTarget> ParseArmorTargets(
        JsonElement element,
        string path)
    {
        RequireObject(element, path);
        var armorNames = Enum.GetNames<ArmorType>();
        RequireExactProperties(element, path, armorNames);

        var targets = new Dictionary<ArmorType, ArmorTarget>();
        foreach (var armor in Enum.GetValues<ArmorType>())
        {
            var armorName = armor.ToString();
            var targetPath = $"{path}.{armorName}";
            var target = GetRequiredProperty(element, armorName, path);
            RequireObject(target, targetPath);
            RequireExactProperties(
                target,
                targetPath,
                "hit_threshold",
                "damage_origin",
                "multiplier",
                "quadratic",
                "base_crit_a",
                "crit_interval");

            var hitThreshold = RequireInteger(target, "hit_threshold", targetPath);
            var damageOrigin = RequireInteger(target, "damage_origin", targetPath);
            var multiplier = RequireDecimal(target, "multiplier", targetPath);
            var quadratic = RequireDecimal(target, "quadratic", targetPath);
            var baseCritical = RequireInteger(target, "base_crit_a", targetPath);
            var criticalInterval = RequireDecimal(target, "crit_interval", targetPath);

            if (multiplier < 0)
            {
                throw new RulesDataException($"{targetPath}.multiplier must not be negative.");
            }

            if (quadratic < 0)
            {
                throw new RulesDataException($"{targetPath}.quadratic must not be negative.");
            }

            if (multiplier == 0 && quadratic == 0)
            {
                throw new RulesDataException(
                    $"{targetPath} must have a positive multiplier or quadratic coefficient.");
            }

            if (criticalInterval <= 0)
            {
                throw new RulesDataException($"{targetPath}.crit_interval must be positive.");
            }

            targets.Add(
                armor,
                new ArmorTarget(
                    hitThreshold,
                    damageOrigin,
                    multiplier,
                    quadratic,
                    baseCritical,
                    criticalInterval));
        }

        return targets;
    }

    private static SpellcastingRules ParseSpellcastingRules(JsonElement element)
    {
        const string path = "$.combat_system_data.spellcasting_rules";
        RequireObject(element, path);
        RequireExactProperties(
            element,
            path,
            "attacks_of_opportunity",
            "armor_restrictions",
            "cleric_deity_alignment",
            "mishap_rule");

        var attacks = GetRequiredProperty(element, "attacks_of_opportunity", path);
        const string attacksPath = path + ".attacks_of_opportunity";
        RequireObject(attacks, attacksPath);
        RequireExactProperties(
            attacks,
            attacksPath,
            "provokes_in_melee",
            "trigger",
            "interruption_on_crit_or_stun");
        var provokes = RequireBoolean(attacks, "provokes_in_melee", attacksPath);
        RequireNonEmptyString(attacks, "trigger", attacksPath);
        var interruption =
            RequireBoolean(attacks, "interruption_on_crit_or_stun", attacksPath);

        var armorRestrictions =
            ParseArmorRestrictions(GetRequiredProperty(element, "armor_restrictions", path));
        var clericVirtues =
            ParseClericAlignment(GetRequiredProperty(element, "cleric_deity_alignment", path));

        var mishap = GetRequiredProperty(element, "mishap_rule", path);
        const string mishapPath = path + ".mishap_rule";
        RequireObject(mishap, mishapPath);
        RequireExactProperties(mishap, mishapPath, "trigger", "effect", "consequence");
        var trigger = RequireNonEmptyString(mishap, "trigger", mishapPath);
        var effect = RequireNonEmptyString(mishap, "effect", mishapPath);
        var consequence = RequireNonEmptyString(mishap, "consequence", mishapPath);
        if (!trigger.Contains("Natural 1", StringComparison.OrdinalIgnoreCase))
        {
            throw new RulesDataException(
                $"{mishapPath}.trigger must identify the natural-1 rule; found '{trigger}'.");
        }

        return new SpellcastingRules(
            provokes,
            interruption,
            1,
            effect,
            consequence,
            armorRestrictions,
            clericVirtues);
    }

    private static IDictionary<CastingTradition, ArmorRestriction> ParseArmorRestrictions(
        JsonElement element)
    {
        const string path = "$.combat_system_data.spellcasting_rules.armor_restrictions";
        RequireObject(element, path);
        var traditions = Enum.GetValues<CastingTradition>();
        RequireExactProperties(
            element,
            path,
            traditions.Select(tradition => tradition.ToString().ToLowerInvariant()).ToArray());

        var restrictions = new Dictionary<CastingTradition, ArmorRestriction>();
        foreach (var tradition in traditions)
        {
            var key = tradition.ToString().ToLowerInvariant();
            var casterPath = $"{path}.{key}";
            var restriction = GetRequiredProperty(element, key, path);
            RequireObject(restriction, casterPath);
            RequireExactProperties(
                restriction,
                casterPath,
                "allowed_armor",
                "allowed_shields",
                "description");

            var allowedArmor = ParseAllowedArmor(
                RequireNonEmptyString(restriction, "allowed_armor", casterPath),
                $"{casterPath}.allowed_armor");
            var description = RequireNonEmptyString(restriction, "description", casterPath);
            var allowedShields = ParseAllowedShields(
                GetRequiredProperty(restriction, "allowed_shields", casterPath),
                $"{casterPath}.allowed_shields");

            restrictions.Add(
                tradition,
                new ArmorRestriction(tradition, allowedArmor, allowedShields, description));
        }

        return restrictions;
    }

    private static AllowedArmor ParseAllowedArmor(string value, string path) => value switch
    {
        "none" => AllowedArmor.None,
        "non_metal" => AllowedArmor.NonMetal,
        "all" => AllowedArmor.All,
        _ => throw new RulesDataException($"{path} contains unknown allowed-armor value '{value}'."),
    };

    private static AllowedShields ParseAllowedShields(JsonElement element, string path) =>
        element.ValueKind switch
        {
            JsonValueKind.True => AllowedShields.All,
            JsonValueKind.False => AllowedShields.None,
            JsonValueKind.String => element.GetString() switch
            {
                "wooden_only" => AllowedShields.WoodenOnly,
                var other => throw new RulesDataException(
                    $"{path} contains unknown allowed-shield value '{other}'."),
            },
            _ => throw new RulesDataException($"{path} must be a boolean or string."),
        };

    private static ClericVirtueRequirement ParseClericAlignment(JsonElement element)
    {
        const string path = "$.combat_system_data.spellcasting_rules.cleric_deity_alignment";
        RequireObject(element, path);
        RequireExactProperties(
            element,
            path,
            "required_virtues_or_vices_count",
            "primary_threshold",
            "secondary_threshold",
            "failure_effect");
        var requiredCount =
            RequirePositiveInteger(element, "required_virtues_or_vices_count", path);
        var failureEffect = RequireNonEmptyString(element, "failure_effect", path);

        var primary = ParseVirtueThreshold(element, "primary_threshold", path);
        var secondary = ParseVirtueThreshold(element, "secondary_threshold", path);

        if (primary.MinimumScore < secondary.MinimumScore)
        {
            throw new RulesDataException(
                $"{path}: primary_threshold.min_score ({primary.MinimumScore}) must not be " +
                $"below secondary_threshold.min_score ({secondary.MinimumScore}).");
        }

        var totalRequired = checked(primary.RequiredCount + secondary.RequiredCount);
        if (totalRequired > requiredCount)
        {
            throw new RulesDataException(
                $"{path}: the thresholds require {totalRequired} virtues but only " +
                $"{requiredCount} are selected.");
        }

        return new ClericVirtueRequirement(requiredCount, primary, secondary, failureEffect);
    }

    private static VirtueThreshold ParseVirtueThreshold(
        JsonElement element,
        string propertyName,
        string path)
    {
        var thresholdPath = $"{path}.{propertyName}";
        var threshold = GetRequiredProperty(element, propertyName, path);
        RequireObject(threshold, thresholdPath);
        RequireExactProperties(threshold, thresholdPath, "min_score", "required_count");

        return new VirtueThreshold(
            RequirePositiveInteger(threshold, "min_score", thresholdPath),
            RequirePositiveInteger(threshold, "required_count", thresholdPath));
    }

    private static void ValidateAttackSummary(
        string path,
        IDictionary<AttackCategoryId, AttackTable> attackTables)
    {
        if (!File.Exists(path))
        {
            throw new RulesDataException($"Missing required rules file: {path}");
        }

        var rows = CsvDocument.Parse(File.ReadAllText(path), path);
        var expectedHeader = new[]
        {
            "Category_ID",
            "Category_Name",
            "Armor_Type",
            "Hit_Threshold",
            "Damage_Origin",
            "Multiplier",
            "Quadratic",
            "Base_Crit_A",
            "Crit_Interval",
        };
        RequireCsvHeader(rows[0], expectedHeader, path);

        var seen = new HashSet<(AttackCategoryId Category, ArmorType Armor)>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowNumber = rowIndex + 1;
            RequireCsvColumnCount(row, expectedHeader.Length, path, rowNumber);

            var definition = CategoryDefinitions.SingleOrDefault(
                item => string.Equals(item.CsvId, row[0], StringComparison.Ordinal));
            if (definition is null)
            {
                throw new RulesDataException(
                    $"{path}:{rowNumber}: unknown category ID '{row[0]}'.");
            }

            if (!Enum.TryParse<ArmorType>(row[2], ignoreCase: false, out var armor))
            {
                throw new RulesDataException(
                    $"{path}:{rowNumber}: unknown armor type '{row[2]}'.");
            }

            if (!seen.Add((definition.Id, armor)))
            {
                throw new RulesDataException(
                    $"{path}:{rowNumber}: duplicate row for {row[0]}/{armor}.");
            }

            var table = attackTables[definition.Id];
            var expectedName = table.Name.Contains(':', StringComparison.Ordinal)
                ? table.Name[(table.Name.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim()
                : table.Name;
            RequireCsvValue(row[1], expectedName, path, rowNumber, "Category_Name");

            var target = table.ForArmor(armor);
            RequireCsvValue(
                ParseCsvInt(row[3], path, rowNumber, "Hit_Threshold"),
                target.HitThreshold,
                path,
                rowNumber,
                "Hit_Threshold");
            RequireCsvValue(
                ParseCsvInt(row[4], path, rowNumber, "Damage_Origin"),
                target.DamageOrigin,
                path,
                rowNumber,
                "Damage_Origin");
            RequireCsvValue(
                ParseCsvDecimal(row[5], path, rowNumber, "Multiplier"),
                target.Multiplier,
                path,
                rowNumber,
                "Multiplier");
            RequireCsvValue(
                ParseCsvDecimal(row[6], path, rowNumber, "Quadratic"),
                target.Quadratic,
                path,
                rowNumber,
                "Quadratic");
            RequireCsvValue(
                ParseCsvInt(row[7], path, rowNumber, "Base_Crit_A"),
                target.BaseCriticalA,
                path,
                rowNumber,
                "Base_Crit_A");
            RequireCsvValue(
                ParseCsvDecimal(row[8], path, rowNumber, "Crit_Interval"),
                target.CriticalInterval,
                path,
                rowNumber,
                "Crit_Interval");
        }

        var expectedRowCount = CategoryDefinitions.Length * Enum.GetValues<ArmorType>().Length;
        if (seen.Count != expectedRowCount)
        {
            var missing = CategoryDefinitions
                .SelectMany(definition => Enum.GetValues<ArmorType>()
                    .Select(armor => (definition, armor)))
                .Where(pair => !seen.Contains((pair.definition.Id, pair.armor)))
                .Select(pair => $"{pair.definition.CsvId}/{pair.armor}");
            throw new RulesDataException(
                $"{path}: expected {expectedRowCount} unique data rows; missing {string.Join(", ", missing)}.");
        }
    }

    private static IDictionary<(CriticalTableId, CriticalTier, int), CriticalOutcome>
        LoadCriticalTables(string directory)
    {
        var outcomes =
            new Dictionary<(CriticalTableId, CriticalTier, int), CriticalOutcome>();
        foreach (var definition in CriticalTableDefinitions)
        {
            var path = Path.Combine(directory, definition.FileName);
            if (!File.Exists(path))
            {
                throw new RulesDataException($"Missing required rules file: {path}");
            }

            var rows = CsvDocument.Parse(File.ReadAllText(path), path);
            var expectedHeader = new[]
            {
                "Index",
                "Tier_A_Minor",
                "Tier_B_Moderate",
                "Tier_C_Severe",
                "Tier_D_Lethal",
                "Tier_E_Catastrophic",
            };
            RequireCsvHeader(rows[0], expectedHeader, path);
            if (rows.Count != 11)
            {
                throw new RulesDataException(
                    $"{path}: expected one header and 10 trauma rows; found {rows.Count} rows.");
            }

            var seenIndices = new HashSet<int>();
            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var rowNumber = rowIndex + 1;
                RequireCsvColumnCount(row, expectedHeader.Length, path, rowNumber);
                var traumaIndex = ParseCsvInt(row[0], path, rowNumber, "Index");
                if (traumaIndex is < 1 or > 10 || !seenIndices.Add(traumaIndex))
                {
                    throw new RulesDataException(
                        $"{path}:{rowNumber}: trauma index must be unique and between 1 and 10; found {traumaIndex}.");
                }

                foreach (var tier in Enum.GetValues<CriticalTier>())
                {
                    var text = row[(int)tier + 1].Trim();
                    if (text.Length == 0)
                    {
                        throw new RulesDataException(
                            $"{path}:{rowNumber}: Tier {tier} trauma text is empty.");
                    }

                    var key = (definition.Id, tier, traumaIndex);
                    outcomes.Add(
                        key,
                        new CriticalOutcome(
                            definition.Id,
                            tier,
                            traumaIndex,
                            text,
                            TraumaEffectParser.Parse(
                                text,
                                $"{path}:{rowNumber}: {definition.Id} Tier {tier} index {traumaIndex}")));
                }
            }
        }

        return outcomes;
    }

    private static ClassProgressionTable LoadClassProgression(string path)
    {
        if (!File.Exists(path))
        {
            throw new RulesDataException($"Missing required rules file: {path}");
        }

        var rows = CsvDocument.Parse(File.ReadAllText(path), path);
        var classNames = Enum.GetNames<CharacterClass>();
        var expectedHeader = new[] { "Level" }.Concat(classNames).ToArray();
        RequireCsvHeader(rows[0], expectedHeader, path);

        var expectedRowCount =
            ClassProgressionTable.MaximumLevel - ClassProgressionTable.MinimumLevel + 1;
        if (rows.Count != expectedRowCount + 1)
        {
            throw new RulesDataException(
                $"{path}: expected one header and {expectedRowCount} level rows; found {rows.Count} rows.");
        }

        var modifiers = new Dictionary<(CharacterClass Class, int Level), int>();
        var seenLevels = new HashSet<int>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowNumber = rowIndex + 1;
            RequireCsvColumnCount(row, expectedHeader.Length, path, rowNumber);

            var level = ParseCsvInt(row[0], path, rowNumber, "Level");
            if (level < ClassProgressionTable.MinimumLevel ||
                level > ClassProgressionTable.MaximumLevel ||
                !seenLevels.Add(level))
            {
                throw new RulesDataException(
                    $"{path}:{rowNumber}: Level must be unique and between " +
                    $"{ClassProgressionTable.MinimumLevel} and {ClassProgressionTable.MaximumLevel}; found {level}.");
            }

            if (level != rowIndex)
            {
                throw new RulesDataException(
                    $"{path}:{rowNumber}: rows must ascend by level; expected {rowIndex}, found {level}.");
            }

            for (var column = 0; column < classNames.Length; column++)
            {
                var characterClass = Enum.Parse<CharacterClass>(classNames[column]);
                var modifier = ParseCsvInt(row[column + 1], path, rowNumber, classNames[column]);
                if (modifier < 0)
                {
                    throw new RulesDataException(
                        $"{path}:{rowNumber}: {classNames[column]} attack modifier must not be negative; found {modifier}.");
                }

                modifiers.Add((characterClass, level), modifier);
            }
        }

        var rules = new Dictionary<CharacterClass, ClassProgressionRules>();
        foreach (var definition in ClassProgressionDefinitions)
        {
            var brackets = definition.Rates
                .Select((rate, index) => new ProgressionBracket(BracketFirstLevels[index], rate))
                .ToArray();
            rules.Add(
                definition.Class,
                new ClassProgressionRules(definition.Class, definition.HardCap, brackets));
        }

        foreach (var characterClass in Enum.GetValues<CharacterClass>())
        {
            if (!rules.ContainsKey(characterClass))
            {
                throw new RulesDataException(
                    $"{path}: no bracket rules are defined for '{characterClass}'.");
            }

            var cap = rules[characterClass].HardCap;
            for (var level = ClassProgressionTable.MinimumLevel;
                 level <= ClassProgressionTable.MaximumLevel;
                 level++)
            {
                var modifier = modifiers[(characterClass, level)];
                if (modifier > cap)
                {
                    throw new RulesDataException(
                        $"{path}: {characterClass} attack modifier +{modifier} at level {level} " +
                        $"exceeds the published hard cap of +{cap}.");
                }

                if (level > ClassProgressionTable.MinimumLevel &&
                    modifier < modifiers[(characterClass, level - 1)])
                {
                    throw new RulesDataException(
                        $"{path}: {characterClass} attack modifier decreases from level {level - 1} to {level}.");
                }
            }
        }

        return new ClassProgressionTable(modifiers, rules);
    }

    private static void ValidateExamples(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            throw new RulesDataException($"{path} must be a non-empty array.");
        }

        var index = 0;
        foreach (var example in element.EnumerateArray())
        {
            if (example.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(example.GetString()))
            {
                throw new RulesDataException($"{path}[{index}] must be a non-empty string.");
            }

            index++;
        }
    }

    private static CriticalTableId? ParseDefaultCriticalTable(string value, string path) =>
        value switch
        {
            "concussion" => CriticalTableId.Crush,
            "slashing" => CriticalTableId.Slash,
            "puncturing" => CriticalTableId.Puncture,
            "grapple_unbalance" => CriticalTableId.Unbalancing,
            "slashing_or_puncturing_or_crush" => null,
            "tooth_and_claw" => null,
            "heat_cold_or_electrical" => null,
            _ => throw new RulesDataException($"{path} contains unknown critical type '{value}'."),
        };

    private static void RequireObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new RulesDataException($"{path} must be a JSON object.");
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        string path,
        params string[] expectedNames)
    {
        RequireObject(element, path);
        var expected = expectedNames.ToHashSet(StringComparer.Ordinal);
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = expected.Except(actual, StringComparer.Ordinal).Order().ToArray();
        var unknown = actual.Except(expected, StringComparer.Ordinal).Order().ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new RulesDataException(
                $"{path} has invalid properties. Missing: [{string.Join(", ", missing)}]; " +
                $"unknown: [{string.Join(", ", unknown)}].");
        }
    }

    private static JsonElement GetRequiredProperty(
        JsonElement element,
        string propertyName,
        string path) =>
        element.TryGetProperty(propertyName, out var value)
            ? value
            : throw new RulesDataException($"{path} is missing required property '{propertyName}'.");

    private static string RequireNonEmptyString(
        JsonElement element,
        string propertyName,
        string path)
    {
        var value = GetRequiredProperty(element, propertyName, path);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new RulesDataException($"{path}.{propertyName} must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static bool RequireBoolean(JsonElement element, string propertyName, string path)
    {
        var value = GetRequiredProperty(element, propertyName, path);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new RulesDataException($"{path}.{propertyName} must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static int RequireInteger(JsonElement element, string propertyName, string path)
    {
        var value = GetRequiredProperty(element, propertyName, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new RulesDataException($"{path}.{propertyName} must be a 32-bit integer.");
        }

        return result;
    }

    private static int RequirePositiveInteger(
        JsonElement element,
        string propertyName,
        string path)
    {
        var value = RequireInteger(element, propertyName, path);
        if (value <= 0)
        {
            throw new RulesDataException($"{path}.{propertyName} must be positive.");
        }

        return value;
    }

    private static decimal RequireDecimal(JsonElement element, string propertyName, string path)
    {
        var value = GetRequiredProperty(element, propertyName, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var result))
        {
            throw new RulesDataException($"{path}.{propertyName} must be a decimal number.");
        }

        return result;
    }

    private static void RequireCsvHeader(
        IReadOnlyList<string> actual,
        IReadOnlyList<string> expected,
        string path)
    {
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new RulesDataException(
                $"{path}: invalid CSV header. Expected '{string.Join(",", expected)}'; " +
                $"found '{string.Join(",", actual)}'.");
        }
    }

    private static void RequireCsvColumnCount(
        IReadOnlyList<string> row,
        int expected,
        string path,
        int rowNumber)
    {
        if (row.Count != expected)
        {
            throw new RulesDataException(
                $"{path}:{rowNumber}: expected {expected} columns; found {row.Count}.");
        }
    }

    private static int ParseCsvInt(string value, string path, int row, string column) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new RulesDataException(
                $"{path}:{row}: {column} must be an integer; found '{value}'.");

    private static decimal ParseCsvDecimal(string value, string path, int row, string column) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new RulesDataException(
                $"{path}:{row}: {column} must be a decimal; found '{value}'.");

    private static void RequireCsvValue<T>(
        T actual,
        T expected,
        string path,
        int row,
        string column)
        where T : IEquatable<T>
    {
        if (!actual.Equals(expected))
        {
            throw new RulesDataException(
                $"{path}:{row}: {column} disagrees with {JsonFileName}; " +
                $"expected '{expected}', found '{actual}'.");
        }
    }

    private sealed record CategoryDefinition(
        AttackCategoryId Id,
        string JsonKey,
        string CsvId);

    private sealed record CriticalTableDefinition(CriticalTableId Id, string FileName);

    private sealed record ClassProgressionDefinition
    {
        public ClassProgressionDefinition(
            CharacterClass Class,
            int HardCap,
            params ProgressionRate[] Rates)
        {
            this.Class = Class;
            this.HardCap = HardCap;
            this.Rates = Rates;
        }

        public CharacterClass Class { get; }

        public int HardCap { get; }

        public IReadOnlyList<ProgressionRate> Rates { get; }
    }
}
