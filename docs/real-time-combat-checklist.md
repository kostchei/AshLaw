# Real-time combat implementation checklist

This checklist tracks replacement of the playable slice's fixed player and NPC
damage with the shared `Ash.Rules.AttackResolver`. The completed path must use
equipped weapons and armour, derived attack and defence, critical trauma,
persistent conditions, real-time impact timing, and one authoritative
application path for player and AI attacks.

The wider M6 work on schedules, dialogue, general pathfinding, projectiles, and
spells remains in `build-plan.md`. This checklist covers physical melee combat
end to end and establishes the shared pipeline those later attack types use.

## How to use this checklist

- Check an item only when its implementation and named verification both pass.
- Add the implementing file or test beside the checkbox when the final location
  differs from the suggested one.
- A phase is complete only when every implementation and exit-proof item in it
  is checked.
- Do not count presentation-only behavior as implemented simulation behavior.
- Unsupported structured trauma effects must fail explicitly; silently ignoring
  one does not satisfy a checkbox.

## Reviewed completion for Phases 1-8

Review date: 2026-08-01. This is the short execution queue; the numbered
checkboxes in the phases below remain authoritative.

- **Phase 1:** complete. Demo body armour, shield, and helmet are authored; live
  equipment changes, every playable monster attack, and broken defensive gear
  behavior are covered by `ActorSheetTests`.
- **Phase 2:** complete. Player and AI impacts share one resolver boundary;
  seeded armour, Cave Rat natural-attack, and live unequip behavior are covered
  by `AttackActionTests` and `CombatAttackServiceTests`.
- **Phase 3:** complete. Post-mutation rollback covers object state, external
  actor conditions, one-use condition consumption, and deterministic dice;
  injected failures after every mutation stage preserve the spatial revision.
- **Phase 4:** complete. Condition stacking/removal policies are explicit and
  saved; persistent injuries affect rolls and movement; Graze uses the
  governing ability; Stable-at-zero rolls and resumes its exact recovery beat;
  Cleave requires a legal second target; effect-specific runtime proofs replace
  the former no-throw smoke test.
- **Phase 5:** complete. The timing and deterministic-order proofs pass again
  after the Phase 3 atomicity and Phase 4 condition-semantics repairs.
- **Phase 6:** complete. Committed combat events retain complete resolver
  evidence in a bounded deterministic history; Godot consumes presentation
  keys for animation poses, audio, HUD summaries, and the calculation inspector.
  Evidence: `AttackActionTests` (270-test simulation suite) and
  `tools/run-game.ps1 -Demo -Combat` on Godot 4.7-stable.
- **Phase 7:** complete. Save format 9 persists active actions, cooldowns, and
  deterministic continuation; packaged text rules and canonical rule/profile
  fingerprints replace the source-tree runtime assumption. Evidence:
  `MidWindUpSaveResumesTheExactImpactRecoveryAndFutureRolls`,
  `RulesRepositoryTests`, and the exported dual-impact combat smoke.
- **Phase 8:** complete. Authored demo-monster vitality is documented in
  `combat-balance.md`; direct resolver, equipment, critical, collision, atomic
  death, replay, and save fixtures pass. The final Release headless matrix,
  complete solution, source Godot smoke, and exported Godot smoke are green.

## Existing prerequisites

- [x] **RTC-P01 — Pure attack resolution:** `AttackResolver.Resolve` consumes an
  `AttackRequest` and returns deterministic hit, concussion, critical, trauma,
  and message data. Evidence: `src/Ash.Rules/AttackResolver.cs` and
  `tests/Ash.Rules.Tests/AttackResolverTests.cs`.
- [x] **RTC-P02 — Validated combat data:** AT-1 through AT-8 and the physical
  critical tables load and validate from vendored data. Evidence:
  `src/Ash.Rules/RulesDataLoader.cs` and the `Ash.Rules.Tests` fixtures.
- [x] **RTC-P03 — Derived actor sheets:** class, ability, equipped armour,
  shield, finesse, attack modifier, and defence modifier can be read from the
  object graph. Evidence: `src/Ash.Sim/ActorSheet.cs` and
  `tests/Ash.Sim.Tests/ActorSheetTests.cs`.
- [x] **RTC-P04 — Authoritative vitality:** damage lands through
  `ActorVitality` and the complete injury state is committed together. Evidence:
  `src/Ash.Sim/ActorVitality.cs` and its tests.
- [x] **RTC-P05 — Deterministic real-time clock:** combat advances on 200 ms
  beats, with a six-second round and deterministic world dice. Evidence:
  `CombatClock`, `AttackSpeed`, and `CombatTimingTests`.
- [x] **RTC-P06 — Active hostile enemies:** hostile monsters recognize, decide,
  pursue, and attack on the combat clock using shared collision and spatial
  state. Evidence: `docs/monster-encounters.md`.

## Phase 1 — Weapon, natural-attack, and armour profiles

- [x] **RTC-101 — Define `AttackProfile`:** represent attack category, explicit
  critical table, weapon handling modifier, melee range, natural/equipped
  origin, attack size, and maximum critical tier without copying attack-table
  numbers. Evidence: `src/Ash.Sim/CombatProfiles.cs`.
- [x] **RTC-102 — Choose profile ownership:** document whether immutable combat
  profiles live on saved world objects or in a content catalogue keyed by
  `TypeId`. Chosen: immutable catalogue keyed by persistent `TypeId`.
- [x] **RTC-103 — Persist or fingerprint profiles:** if profiles are object
  state, add them to the object store and the next save format; if profiles are
  content, include their canonical data in the content fingerprint. Evidence:
  `PlayableSliceWorld.ContentFingerprint` v2.
- [x] **RTC-104 — Validate authored profiles:** reject unknown categories,
  invalid ranges, weapon profiles without an accepted hand slot, and ambiguous
  attack categories without an explicit critical table. Profile enums, range,
  and natural/weapon ownership validate in `CombatProfileCatalog`; equipment
  slot validity remains enforced by `ObjectTransferService`.
- [x] **RTC-105 — Author Rusty Sword:** one-handed slashing with an appropriate
  physical critical table.
- [x] **RTC-106 — Author Bronze Dagger:** finesse weapon with an explicit
  physical critical table.
- [x] **RTC-107 — Author Goblin weapon:** its equipped object must carry a real
  weapon profile rather than being identified only by its name or hand slot.
- [x] **RTC-108 — Author Cave Rat attack:** tooth-and-claw natural attack with
  an explicit critical table.
- [x] **RTC-109 — Author unarmed fallback:** an actor without a weapon must have
  a defined attack or receive a clear refusal; it must not silently borrow sword
  rules.
- [x] **RTC-110 — Author defensive equipment:** add at least one body armour,
  shield, and helmet object suitable for end-to-end and conditional-effect tests.
  Evidence: `PlayableSliceWorld.SpawnAvatar` authors a Leather Jerkin, Wooden
  Shield, and Iron Helm; `DemoDefensiveGearChangesTheLiveSheetWithoutRebuildingIt`.
- [x] **RTC-111 — Correct weapon selection:** update `ActorSheets` so it selects
  only an equipped object with an attack profile, not any hand item whose
  defence bonus is zero.
- [x] **RTC-112 — Profile tests:** prove sword, dagger, natural attack, unarmed,
  shield, and armour selection from the authoritative equipment graph.
- [x] **RTC-113 — Separate attack adjustments:** keep an inherent weapon
  modifier and saved per-item quality modifier distinct, then include both in
  the derived attack modifier. Rusty Sword is quality -1; Bronze Dagger is
  source handling -3 plus quality -1; Notched Blade is quality -1.
- [x] **RTC-114 — Implement attack-size result caps:** cap Tiny, Small, Medium,
  Large, and Huge attacks at the AT-5 source bands before damage and critical
  resolution. Evidence: `AttackSizeRules` and `AttackResolverTests`.
- [x] **RTC-115 — Implement critical caps:** enforce both size-derived and
  attack-specific maximum critical tiers. Bronze Dagger caps at C, Cave Rat
  Small bite at B, and unarmed at A.
- [x] **RTC-116 — Broken defensive equipment stops defending:** broken body
  armour supplies neither its armour category nor its defence bonus, and a
  broken shield supplies no shield bonus or conditional protection. Evidence:
  `BrokenDefensiveGearSuppliesNoArmourOrDefence` and
  `BreakingAnEquippedShieldEnablesNoShieldTrauma`.

### Phase 1 exit proof

- [x] Equipping or removing a weapon changes the selected attack profile and
  governing ability without rebuilding or caching the actor sheet. Evidence:
  `EquippingDifferentWeaponsChangesTheLiveProfileAndAbility`.
- [x] Equipping body armour and a shield changes armour category and defence.
  Evidence: `DemoDefensiveGearChangesTheLiveSheetWithoutRebuildingIt`.
- [x] Every playable monster has either a valid natural attack or a valid
  equipped weapon profile. Evidence:
  `EveryDemoMonsterHasAValidatedNaturalOrEquippedAttack`.

## Phase 2 — One shared attack-resolution service

- [x] **RTC-201 — Add an injectable rules boundary:** wrap the static resolver
  behind a small interface or delegate so simulation tests can count and stub
  calls without duplicating the rules formulas.
- [x] **RTC-202 — Create `CombatAttackService`:** add the sole simulation entry
  point for resolving a physical attack from attacker and target IDs.
- [x] **RTC-203 — Validate attacker:** require a live, upright actor allowed to
  act under its current conditions.
- [x] **RTC-204 — Validate target:** require a live target on the same map and
  within the selected profile's range.
- [x] **RTC-205 — Resolve equipment at impact:** read the equipped weapon or
  natural attack at resolution time so stale input cannot attack with an item
  that has since been dropped or broken.
- [x] **RTC-206 — Derive both sheets:** source attack modifier, defence modifier,
  armour category, shield state, and governing ability from `ActorSheets`.
- [x] **RTC-207 — Roll deterministic attack dice:** take the raw d20 from the
  world's saved `Dice` at the impact point; do not add RNG inside
  `AttackResolver`.
- [x] **RTC-208 — Build `AttackRequest`:** supply raw d20, attack category,
  attack modifier, defence modifier, armour, and critical table from the
  authoritative runtime state.
- [x] **RTC-209 — Resolve exactly once:** call the injected `AttackResolver`
  boundary once per impact and retain the complete `AttackResult` trace.
- [x] **RTC-210 — Evaluate conditional trauma:** determine `NoArmArmor`,
  `NoLegArmor`, `NoHelm`, and `NoShield` from equipped objects using a documented
  slot mapping.
- [x] **RTC-211 — Calculate immediate damage:** combine concussion hits with all
  applicable `AdditionalHits` and apply the total once.
- [x] **RTC-212 — Handle miss and zero damage:** do not call
  `ActorVitality.Damage` with zero; distinguish miss, zero-damage hit, and
  critical result in the outcome.
- [x] **RTC-213 — Return structured outcome:** include attacker, target, weapon,
  request, rules result, applicable trauma effects, applied damage, and final
  target state.
- [x] **RTC-214 — Route player attacks through the service:** remove fixed
  `PlayerAttackDamage` use from `PlayableSliceWorld.AttackAdjacentMonster`.
- [x] **RTC-215 — Route NPC attacks through the service:** remove fixed
  `NpcSwingDamage` use from `CombatDirector.Swing`.
- [x] **RTC-216 — Remove fixed damage constants:** no runtime physical attack
  may subtract a hand-authored constant instead of using the resolver.

### Phase 2 exit proof

- [x] A test spy proves player and AI attacks use the same simulation service
  and enter the rules resolver once per impact. Evidence:
  `PlayerAndAiImpactsUseTheSameInjectedRulesBoundaryOnceEach`.
- [x] A seeded sword attack against each armour category matches a direct
  `AttackResolver` fixture. Evidence:
  `SeededSwordAttackMatchesDirectResolverForEveryArmourCategory`.
- [x] A seeded Cave Rat natural attack resolves through tooth-and-claw rather
  than the sword table. Evidence:
  `SeededCaveRatAttackUsesToothAndClawAndPunctureRules`.
- [x] Unequipping the player's weapon changes the next attack without restarting
  the world. Evidence:
  `UnequippingPlayerWeaponChangesTheNextResolutionInTheSameWorld`.

## Phase 3 — Atomic combat mutation

- [x] **RTC-301 — Define `CombatMutation`:** represent injury, condition,
  movement, equipment, item-condition, death, and corpse changes before any of
  them commit.
- [x] **RTC-302 — Prevalidate target state:** reject stale handles, dead targets,
  changed equipment sources, invalid destinations, and incompatible condition
  changes before mutation.
- [x] **RTC-303 — Add an object-store combat commit:** commit all touched state
  under one `ObjectStore.IsCommitting` boundary and publish one coherent change.
- [x] **RTC-304 — Apply injury atomically:** concussion, wounds, death-clock
  counters, impairments, and vitality state must remain one update.
- [x] **RTC-305 — Integrate forced movement:** resolve displacement through the
  shared `MovementSolver` and include the validated placement/physics update in
  the combat commit.
- [x] **RTC-306 — Integrate item effects:** dropping, damaging, or breaking a
  held item must use authoritative equipment and transfer rules.
- [x] **RTC-307 — Integrate death transformation:** death, corpse conversion,
  contained equipment, and spills must not leave a dead non-corpse if a later
  step fails. Evidence: `FailureAfterEveryMutationStageRestoresTheWholeWorld`.
- [x] **RTC-308 — Publish after commit:** combat events and messages must describe
  committed state only.
- [x] **RTC-309 — Rollback tests:** inject a failure into each mutation class and
  prove injury, positions, equipment, conditions, corpses, and spatial-index
  revision remain unchanged. Evidence:
  `FailureAfterEveryMutationStageRestoresTheWholeWorld`.
- [x] **RTC-310 — Transactional external combat state:** condition application
  and one-use Sap, Vex, and Cleave consumption must commit or roll back with the
  object mutation and deterministic dice result. Evidence:
  `FailedImpactRestoresDamageConditionsOneUseTokensAndDice`.

### Phase 3 exit proof

- [x] A critical that damages, moves, disarms, conditions, and kills a target is
  observed as one coherent commit. Evidence:
  `LethalMultiEffectTraumaPublishesOneCoherentCorpseCommit`.
- [x] No failure can leave partial damage, orphaned equipment, overlapping
  bodies, or an invalid corpse/container graph. Evidence:
  `FailureAfterEveryMutationStageRestoresTheWholeWorld`.

## Phase 4 — Persistent actor conditions and trauma application

- [x] **RTC-401 — Define actor condition state:** include kind, source ID,
  magnitude, applied tick, expiry/removal policy, next periodic tick, stacking
  policy, and presentation key. Evidence: `ActorCondition`, save format v8, and
  `TimedReplacementAndAnatomicalInjuriesFollowExplicitPolicies`.
- [x] **RTC-402 — Keep item durability separate:** do not reuse
  `WorldObject.Condition`, which currently means an item's physical condition.
- [x] **RTC-403 — Centralize duration conversion:** expose one named conversion
  where one six-second round equals 30 combat beats; individual effects must not
  embed their own milliseconds.
- [x] **RTC-404 — Add `ActorConditionService`:** query, add, stack, replace,
  consume, expire, and remove conditions deterministically. Evidence:
  `ActorConditionTests`.
- [x] **RTC-405 — Persist conditions:** include their exact remaining/next ticks
  in object-world snapshots and save/load migration.
- [x] **RTC-406 — Implement immediate effects:** `AdditionalHits`, `Death`,
  `DropHeldItem`, and `BreakItem`.
- [x] **RTC-407 — Implement periodic/timed effects:** `Bleeding`, `Stun`,
  `Restrained`, `Incapacitated`, and `Slow`.
- [x] **RTC-408 — Implement positional effects:** `Prone`, `ForcedMovement`,
  `Push`, and `Topple`, all through shared placement/collision.
- [x] **RTC-409 — Implement persistent injuries:** `BreakBone`, `DisableLimb`,
  `DestroyEye`, `Paralyzed`, `Injured`, and `Exhaustion`. Evidence:
  `PersistentInjuriesCommitDistinctMechanicalPenalties`.
- [x] **RTC-410 — Implement one-use mastery effects:** `Sap`, `Vex`, `Graze`,
  and `Cleave`, including deterministic consumption rules. Evidence:
  `GrazeUsesTheGoverningAbilityModifierAndMultiplier`,
  `CleaveOnlyGrantsAndConsumesAgainstALegalSecondTarget`, and
  `CombatAttackServiceTests`.
- [x] **RTC-411 — Implement special body states:** `Unconscious`, `Dying`,
  `Suffocating`, and `StableAtZero` without contradicting `ActorVitality`.
  Evidence: `DyingAndUnconsciousDoNotContradictAuthoritativeVitality`,
  `SuffocationTicksAsExhaustionWhileBleedingTicksAsDamage`, and
  `StableAtZeroRollsD4HoursRecoversOnTheExactBeatAndStaysInjured`.
- [x] **RTC-412 — Make action legality condition-aware:** stunned,
  incapacitated, unconscious, dying, stable, and dead actors cannot start
  forbidden attacks or movement.
- [x] **RTC-413 — Make derivation condition-aware:** applicable temporary
  penalties, Sap, Vex, injuries, and exhaustion must affect attack/defence or
  the attack roll in one documented layer.
- [x] **RTC-414 — Make movement condition-aware:** prone, restrained, slow,
  paralysis, and injury must affect movement without bypassing collision.
- [x] **RTC-415 — Exhaustive effect dispatcher:** a switch or registered mapping
  covers every `TraumaEffectKind`; adding a new enum member breaks a test until
  its runtime policy is defined. Evidence: `TraumaEffectDispatcher.SupportedKinds`
  and `DispatcherPolicyListMatchesEveryStructuredTraumaKind`.
- [x] **RTC-416 — Condition timing tests:** verify application, stacking,
  periodic damage, expiration, one-use consumption, healing/removal, and save
  resumption on exact combat beats. Evidence: `ActorConditionTests` and the
  exact-beat/save tests in `TraumaPhaseTests`.
- [x] **RTC-417 — Effect-specific runtime proofs:** replace the current
  every-enum no-throw smoke test with assertions for each effect's committed
  mechanical result, including Graze, Stable-at-zero, persistent injuries, and
  Cleave eligibility. Evidence: `TraumaPhaseTests`.

### Phase 4 exit proof

- [x] Every structured trauma effect has an implemented runtime policy or an
  explicit refusal that prevents affected content from shipping.
- [x] A critical result produces immediate damage, persistent conditions, and
  matching narrative text from one rules result. Evidence:
  `CriticalNarrativeImmediateDamageAndPersistentStateShareOneResult`.
- [x] Conditions change subsequent player and AI decisions rather than existing
  only as log entries.

## Phase 5 — Real-time attack actions and impact timing

- [x] **RTC-501 — Define attack action state:** record attacker, target, weapon,
  start tick, impact tick, recovery tick, action phase, and interruption policy.
- [x] **RTC-502 — Schedule player intent:** input starts an attack action instead
  of applying damage on the keypress.
- [x] **RTC-503 — Schedule AI intent:** hostile NPC attack decisions create the
  same action type used by the player.
- [x] **RTC-504 — Enforce a non-zero wind-up:** no attack may resolve on the same
  tick on which its action decision is made.
- [x] **RTC-505 — Resolve at the impact tick:** roll and call
  `CombatAttackService` only when the configured action frame/impact beat arrives.
- [x] **RTC-506 — Revalidate at impact:** actor state, target life, range, map,
  and equipped weapon must still be legal.
- [x] **RTC-507 — Define whiffs:** a target leaving range causes a miss/whiff
  presentation and consumes the action without invoking damage.
- [x] **RTC-508 — Define interruptions:** death, incapacitation, disarm, weapon
  breakage, map transition, and scripted cancellation have explicit outcomes.
- [x] **RTC-509 — Preserve recovery pacing:** the existing six-second swing
  allowance, and later mastery-derived extra attacks, gate the next action
  independently of movement.
- [x] **RTC-510 — Deterministic simultaneous impacts:** attacks due on the same
  beat resolve in a documented stable order, ending or cancelling later actions
  consistently when an earlier impact kills or incapacitates an actor.
- [x] **RTC-511 — Timing tests:** prove no damage before impact, exact impact and
  recovery ticks, whiffs, interruptions, and deterministic same-beat ordering.

### Phase 5 exit proof

- [x] Player input, AI intent, animation, resolver invocation, mutation, and
  recovery form one observable timed process.
- [x] Identical save state and input timing produce identical rolls, impacts,
  conditions, deaths, and events.

## Phase 6 — Combat events, log, animation, audio, and HUD

- [x] **RTC-601 — Expand combat events:** represent attack start, impact, miss,
  hit, critical, condition, forced movement, interruption, death, and corpse
  results.
- [x] **RTC-602 — Include resolution evidence:** events/log entries retain
  attacker, target, weapon, raw roll, modifiers, net roll, armour, damage,
  critical tier/table, trauma text, and applied effects.
- [x] **RTC-603 — Add retained combat history:** keep a bounded deterministic log
  rather than exposing only the last status message or current beat's events.
- [x] **RTC-604 — Drive actor animation from events/state:** Godot presents wind-
  up, attack, hit reaction, prone, interruption, and death without owning combat
  authority.
- [x] **RTC-605 — Add audio cues:** retain encounter voices and add weapon swing,
  miss, impact, critical, condition, and death cues from committed events.
- [x] **RTC-606 — Add compact HUD feedback:** show weapon/category, attack
  readiness, hit or miss, damage, critical tier, and important conditions.
- [x] **RTC-607 — Add inspectable calculation log:** the UI can reproduce the
  `AttackResolver.Messages` trace for debugging and player-facing detail.
- [x] **RTC-608 — Presentation tests/smoke proof:** headless simulation verifies
  event payloads; a Godot smoke run verifies events select the expected
  presentation without changing simulation state.

## Phase 7 — Save compatibility and exported runtime rules

- [x] **RTC-701 — Choose mid-attack save policy:** either persist active attack
  actions/cooldowns or make the save gate defer until combat actions reach a
  documented safe point; do not silently reset wind-ups for advantage.
- [x] **RTC-702 — Bump save format:** serialize all new authoritative combat
  state and update `MinimumReaderVersion` as required.
- [x] **RTC-703 — Add migration:** define safe defaults for older saves that lack
  profiles, conditions, and active actions; add corrupt/unknown enum rejection.
- [x] **RTC-704 — Preserve deterministic continuation:** save/load during or
  between attacks resumes the same dice state, condition ticks, action order,
  and future result.
- [x] **RTC-705 — Add stream/text rules loaders:** remove the source-tree-only
  assumption from `RulesRepository` so an exported game can load JSON and CSV
  data from packaged resources.
- [x] **RTC-706 — Package combat data:** ship attack tables, critical tables,
  progression, character bonuses, and vitality configuration in the Godot pack.
- [x] **RTC-707 — Fingerprint runtime rules:** incompatible rule/profile changes
  alter the content fingerprint and are refused or migrated deliberately.
- [x] **RTC-708 — Export smoke test:** a packaged/headless game resolves one
  player and one NPC attack without reading the repository filesystem.

### Phase 7 exit proof

- [x] Saving and loading preserves consequential combat state and produces a
  byte-stable save when no state changes.
- [x] The exported runtime resolves combat using packaged rules data with no
  duplicated constants in Godot code.

## Phase 8 — End-to-end verification and tuning

- [x] **RTC-801 — Player-versus-monster fixture:** equipped sword, derived
  attack, monster defence/armour, deterministic result, damage, and log all
  agree with a direct rules fixture.
- [x] **RTC-802 — Monster-versus-player fixture:** natural or equipped monster
  attack uses the player's current armour, shield, and defence.
- [x] **RTC-803 — Equipment mutation fixture:** changing weapon, armour, shield,
  or helmet changes the next applicable request and conditional trauma result.
- [x] **RTC-804 — Critical condition fixture:** a seeded critical applies
  immediate damage, at least one timed condition, narrative text, and later
  condition consequences.
- [x] **RTC-805 — Collision effect fixture:** forced movement cannot enter solid
  terrain, occupied cells, or invalid elevation and leaves the spatial index
  valid.
- [x] **RTC-806 — Atomic death fixture:** a lethal critical creates a coherent
  corpse and loot graph with no intermediate dead actor visible.
- [x] **RTC-807 — Replay fixture:** two worlds with the same snapshot and timed
  inputs produce identical attack outcomes, event sequences, condition state,
  positions, corpses, and dice state.
- [x] **RTC-808 — Save round trip fixture:** conditions and active/recovering
  attacks survive save/load and continue on the same beats.
- [x] **RTC-809 — Balance pass:** review resolver-scale damage against current
  Avatar and monster concussion/wound pools; replace test-era health values with
  authored, documented values rather than clamping resolver output.
- [x] **RTC-810 — Full regression:** `Ash.Rules.Tests`, `Ash.Sim.Tests`,
  architecture checks, save compatibility, and the Godot smoke test pass.

## Final definition of done

- [x] Player and AI physical attacks use one timed simulation pipeline and one
  call boundary into `AttackResolver`.
- [x] Equipped weapon, natural attack, armour, shield, abilities, class, level,
  and active conditions determine the request; no fixed player/NPC damage
  remains.
- [x] Critical effects change authoritative world state atomically and every
  supported effect has deterministic timing and stacking behavior.
- [x] Presentation is event-driven and cannot alter combat outcomes.
- [x] Combat state and future deterministic rolls survive save/load.
- [x] Exported builds load the same data-backed rules used by tests.
- [x] The complete calculation and applied consequences are inspectable in the
  combat log.
