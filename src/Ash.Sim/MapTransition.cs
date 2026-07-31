using Ash.Core;

namespace Ash.Sim;

public enum MapTransitionFailure
{
    None = 0,

    /// <summary>The traveller is not standing on a map to leave.</summary>
    NotOnAMap,

    /// <summary>No map with that id is registered in this world.</summary>
    UnknownMap,

    /// <summary>The arrival cell is outside the destination map.</summary>
    OffMap,

    /// <summary>Nothing at the arrival cell to stand on.</summary>
    NoFooting,

    /// <summary>Something is resting on the traveller.</summary>
    CarryingDependants,

    /// <summary>The destination is occupied, or otherwise refused placement.</summary>
    Blocked,
}

public readonly record struct MapTransitionResult(
    bool Succeeded,
    MapTransitionFailure Failure,
    string Message,
    ushort MapId,
    Vec3i Position)
{
    public static MapTransitionResult Arrived(
        ushort mapId,
        Vec3i position,
        string message) =>
        new(true, MapTransitionFailure.None, message, mapId, position);

    public static MapTransitionResult Refused(
        MapTransitionFailure failure,
        string message) =>
        new(false, failure, message, 0, default);
}

/// <summary>
/// Moves an actor from the map it is on to a cell of another one.
/// </summary>
/// <remarks>
/// Eighteen subzones are eighteen maps, and nothing in the engine yet carries a
/// body between two of them. The transaction already validates a destination
/// against whichever map it names, so this is not new physics — it is the small
/// amount of bookkeeping that a cross-map move needs and an ordinary step does
/// not: finding the floor on a map the traveller cannot yet see, and dropping a
/// support reference that means nothing once it is somewhere else.
///
/// Carried inventory needs no help. Anything in a pack or worn is located
/// <see cref="LocationKind.InContainer"/> or
/// <see cref="LocationKind.Equipped"/> relative to its owner, so it travels
/// because its owner does.
/// </remarks>
public sealed class MapTransitionService
{
    private readonly ObjectStore _objects;
    private readonly ObjectTransferService _transfers;

    public MapTransitionService(ObjectStore objects)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _transfers = new ObjectTransferService(objects);
    }

    /// <summary>
    /// How far above a cell footing is searched for. One Ultima VIII level
    /// short of the highest platform the world builds, which is well clear of
    /// any floor a subzone generates.
    /// </summary>
    public const int SearchCeilingZ = 4096;

    /// <summary>
    /// Moves <paramref name="travellerId"/> to cell
    /// (<paramref name="cellX"/>, <paramref name="cellY"/>) of
    /// <paramref name="mapId"/>, landing it on whatever it finds to stand on
    /// there.
    /// </summary>
    public MapTransitionResult Move(
        ObjectId travellerId,
        ushort mapId,
        int cellX,
        int cellY)
    {
        var traveller = _objects.Get(travellerId);
        if (traveller.Location.Kind != LocationKind.OnMap)
        {
            return MapTransitionResult.Refused(
                MapTransitionFailure.NotOnAMap,
                $"{traveller.Name} is not on a map to leave.");
        }

        if (!_objects.Maps.TryGet(mapId, out var destination))
        {
            return MapTransitionResult.Refused(
                MapTransitionFailure.UnknownMap,
                $"Map {mapId} is not registered in this world.");
        }

        // Anything resting on the traveller would be left holding a support
        // reference to an object on another map. Refusing is the honest answer:
        // the world has no rule for a crate that is on your head and in another
        // subzone, and inventing one here would be inventing physics.
        var source = _objects.Maps.Get(traveller.Location.MapId);
        var dependants = source.SupportedObjects(travellerId);
        if (dependants.Count > 0)
        {
            return MapTransitionResult.Refused(
                MapTransitionFailure.CarryingDependants,
                $"{dependants.Count} object(s) rest on {traveller.Name}; " +
                "it cannot leave the map holding them up.");
        }

        var anchor = new Vec3i(
            checked((cellX + 1) * WorldMap.WorldUnitsPerTile),
            checked((cellY + 1) * WorldMap.WorldUnitsPerTile),
            SearchCeilingZ);
        if (!destination.WorldBounds.Contains(anchor.X - 1, anchor.Y - 1))
        {
            return MapTransitionResult.Refused(
                MapTransitionFailure.OffMap,
                $"Cell ({cellX}, {cellY}) is outside map {mapId}.");
        }

        // The traveller is placed on the destination map before its footing is
        // asked for, because support is resolved per map and a body cannot be
        // asked what it stands on somewhere it is not.
        var arriving = traveller with
        {
            Location = ObjectLocation.OnMap(mapId, anchor),
        };
        var footing = destination.FindSupport(arriving, SearchCeilingZ);
        if (footing.IsNone)
        {
            return MapTransitionResult.Refused(
                MapTransitionFailure.NoFooting,
                $"Nothing at ({cellX}, {cellY}) on map {mapId} to stand on.");
        }

        var landing = anchor with { Z = footing.TopZ };
        var moved = _transfers.Execute(
            [
                new ObjectTransferRequest(
                    travellerId,
                    traveller.Location,
                    ObjectLocation.OnMap(mapId, landing)),
            ],
            [ObjectPhysicsUpdate.Resting(travellerId, footing)]);
        if (!moved.Succeeded)
        {
            return MapTransitionResult.Refused(
                MapTransitionFailure.Blocked,
                moved.Message);
        }

        return MapTransitionResult.Arrived(
            mapId,
            landing,
            $"{traveller.Name} arrives on map {mapId}.");
    }

    /// <summary>
    /// Moves <paramref name="travellerId"/> to the first beat of
    /// <paramref name="subzone"/>, which is where a subzone is entered.
    /// </summary>
    public MapTransitionResult Enter(ObjectId travellerId, SubzonePlan subzone)
    {
        ArgumentNullException.ThrowIfNull(subzone);
        var (cellX, cellY) = WorldPlanner.EntranceCell(subzone);
        return Move(travellerId, subzone.MapId, cellX, cellY);
    }
}
