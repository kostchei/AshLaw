using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class ProjectileTests
{
    [Fact]
    public void AShotCrossesTheRoomOverSeveralBeatsRatherThanResolvingAtOnce()
    {
        var resolver = new CountingResolver();
        using var scenario = Archer(resolver, out var bow);
        var target = scenario.SpawnMonster(20, 5, BehaviorProfile.Guard);

        var shot = scenario.Combat.TryPlayerShoot(
            scenario.Objects.Get(target).Location.Position, target);

        Assert.True(shot.Swung, shot.Message);
        Assert.Single(scenario.Projectiles.InFlight);
        Assert.Equal(0, resolver.Calls);

        // Fifteen tiles at six tiles a beat is three beats of visible flight.
        scenario.AdvanceBeat();
        Assert.Equal(0, resolver.Calls);
        Assert.Single(scenario.Projectiles.InFlight);

        var flight = scenario.Projectiles.InFlight[0];
        Assert.True(flight.TilesTravelled > 0);
        Assert.NotEqual(
            scenario.Objects.Get(scenario.PlayerId).Location.Position,
            scenario.Objects.Get(flight.ProjectileId).Location.Position);

        scenario.AdvanceBeats(4);

        Assert.Empty(scenario.Projectiles.InFlight);
        Assert.Equal(1, resolver.Calls);
        Assert.Equal(
            AttackCategoryId.MissileWeapons,
            resolver.Requests[0].AttackCategory);
        Assert.False(scenario.Objects.TryGet(flight.ProjectileId, out _));
        _ = bow;
    }

    [Fact]
    public void ATargetThatStepsOutOfTheLineIsNotHitByAShotAlreadyLoosed()
    {
        var resolver = new CountingResolver();
        using var scenario = Archer(resolver, out _);
        var target = scenario.SpawnMonster(16, 5, BehaviorProfile.Guard);

        Assert.True(
            scenario.Combat.TryPlayerShoot(
                scenario.Objects.Get(target).Location.Position, target).Swung);
        scenario.Place(target, 16, 12);
        scenario.AdvanceBeats(6);

        Assert.Empty(scenario.Projectiles.InFlight);
        Assert.Equal(0, resolver.Calls);

        // Nothing was struck, so the arrow is on the ground where it landed and
        // can be picked up again (CMB-024).
        Assert.Contains(
            scenario.Objects.Enumerate(),
            value => value.TypeId == ProjectileCatalog.ArrowTypeId &&
                value.Location.Kind == LocationKind.OnMap);
    }

    [Fact]
    public void AShotStopsAtSolidTerrainAndNeverReachesWhatIsBehindIt()
    {
        var resolver = new CountingResolver();
        using var scenario = Archer(resolver, out _);
        for (var tileY = 0; tileY < 12; tileY++)
        {
            scenario.Map.SetTerrain(10, tileY, new TerrainCell(0, TerrainFlags.Solid));
        }

        var target = scenario.SpawnMonster(14, 5, BehaviorProfile.Guard);

        Assert.True(
            scenario.Combat.TryPlayerShoot(
                scenario.Objects.Get(target).Location.Position, target).Swung);
        scenario.AdvanceBeats(6);

        Assert.Empty(scenario.Projectiles.InFlight);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public void AShotIsPaidForInAmmunitionWhenItLeavesAndRefusedWithoutAny()
    {
        var resolver = new CountingResolver();
        using var scenario = Archer(resolver, out _, arrows: 1);
        var target = scenario.SpawnMonster(12, 5, BehaviorProfile.Guard);
        var aim = scenario.Objects.Get(target).Location.Position;

        Assert.True(scenario.Combat.TryPlayerShoot(aim, target).Swung);
        Assert.DoesNotContain(
            scenario.Objects.Enumerate(),
            value => value.TypeId == ProjectileCatalog.ArrowTypeId &&
                value.Location.Kind == LocationKind.InContainer);

        // The bow is ready again after the round, and there is still nothing to
        // put in it.
        scenario.AdvanceBeats(40);
        var second = scenario.Combat.TryPlayerShoot(aim, target);

        Assert.False(second.Swung);
        Assert.Contains("ammunition", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AShotSpendsTheRoundsAttackExactlyAsASwingDoes()
    {
        using var scenario = Archer(new CountingResolver(), out _);
        var target = scenario.SpawnMonster(12, 5, BehaviorProfile.Guard);
        var aim = scenario.Objects.Get(target).Location.Position;

        Assert.True(scenario.Combat.TryPlayerShoot(aim, target).Swung);

        Assert.False(scenario.Combat.PlayerCanSwing);
        Assert.True(scenario.Combat.PlayerCooldownRemainingMilliseconds > 0);
        Assert.False(scenario.Combat.TryPlayerShoot(aim, target).Swung);
    }

    [Fact]
    public void AFlightSurvivesSaveAndLoadOnTheExactTileItHadReached()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var world = PlayableSliceWorld.CreateDemo(
                20260801, new CountingResolver());
            var rat = world.Monsters.First();
            Assert.True(world.ToggleRightHand().Succeeded);
            var flight = world.Projectiles.Launch(
                world.PlayerId,
                rat.Id,
                ProjectileCatalog.Arrow,
                world.Player.Location.Position with
                {
                    X = world.Player.Location.Position.X +
                        (2 * PlayableSliceWorld.WorldUnitsPerTile),
                });

            Assert.True(world.RequestSave(path).Succeeded);
            using var loaded = PlayableSliceWorld.Load(path);

            Assert.Equal([flight], loaded.Projectiles.InFlight);
            Assert.Equal(
                world.Objects.Get(flight.ProjectileId).Location,
                loaded.Objects.Get(flight.ProjectileId).Location);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASavedProjectileNamingUnknownContentIsRefusedRatherThanDropped()
    {
        using var scenario = Archer(new CountingResolver(), out _);
        var stray = scenario.SpawnWall(30, 30);

        Assert.Throws<ObjectWorldSaveException>(() =>
            scenario.Projectiles.Restore(
            [
                new ProjectileFlight(
                    stray,
                    scenario.PlayerId,
                    ObjectId.None,
                    "projectile.does-not-exist",
                    CombatScenario.Anchor(5, 5),
                    CombatScenario.Anchor(6, 5),
                    0,
                    0),
            ]));
    }

    private static CombatScenario Archer(
        IAttackRulesResolver resolver,
        out ObjectId bow,
        int arrows = 20)
    {
        var scenario = new CombatScenario(resolver);
        scenario.PlacePlayer(5, 5);
        bow = scenario.Equip(
            scenario.PlayerId,
            ProjectileCatalog.ShortBowTypeId,
            "Short Bow",
            EquipmentSlot.RightHand);
        scenario.GiveStack(
            scenario.PlayerId, ProjectileCatalog.ArrowTypeId, "Arrows", arrows);
        return scenario;
    }

    private sealed class CountingResolver : IAttackRulesResolver
    {
        public int Calls { get; private set; }

        public List<AttackRequest> Requests { get; } = [];

        public AttackResult Resolve(AttackRequest request)
        {
            Calls++;
            Requests.Add(request);
            return new AttackResult
            {
                Hit = true,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = 1,
                ConcussionHits = 1,
                Mishap = false,
            };
        }
    }
}
