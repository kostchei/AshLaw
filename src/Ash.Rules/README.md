# Ash.Rules

`Ash.Rules` is the dependency-free, deterministic combat calculation boundary.

```csharp
var rules = RulesDataLoader.LoadFromDirectory(vendoredDataDirectory);
var result = AttackResolver.Resolve(
    rules,
    new AttackRequest(
        RawD20: 17,
        AttackCategory: AttackCategoryId.GrapplingUnbalancing,
        AttackModifier: 5,
        DefenseModifier: 0,
        Armor: ArmorType.Leather));
```

The loader validates the runtime JSON against the generated attack-summary CSV and
imports all four physical critical CSVs. Trauma prose is retained for presentation and
converted to immutable structured effects during loading.

The resolver performs no I/O, random generation, mutation, or runtime prose parsing.
Categories with an ambiguous critical type require `AttackRequest.CriticalTable`; the
caller derives it from the weapon, creature attack, or spell definition. Elemental and
creature-specific trauma tables are not invented when no authoritative CSV exists.

