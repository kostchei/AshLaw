namespace Ash.Sim;

public readonly record struct CorpseResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<ObjectId> Stowed,
    IReadOnlyList<ObjectId> Spilled);

/// <summary>
/// What death does to the things a body was wearing.
/// </summary>
/// <remarks>
/// A corpse is one container holding everything the body had. Worn gear cost no
/// gear slots while it was worn, so moving it into the body can overflow: the
/// excess falls at the body's feet, the same rule a full pack follows. Gear
/// transfers and transformation are submitted as one combat mutation, so no
/// observer can see a stripped dead actor between the two operations.
/// </remarks>
public static class Death
{
    public static CorpseResult MakeCorpse(
        ObjectStore objects,
        ObjectId bodyId,
        string typeId,
        string name,
        string shapeId,
        ObjectFlags flags,
        int? height = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var body = objects.Get(bodyId);
        if (!flags.HasFlag(ObjectFlags.Container))
        {
            throw new ArgumentException(
                "A corpse holds what the body had, so it must be a container.",
                nameof(flags));
        }

        var worn = objects.Enumerate()
            .Where(value =>
                value.Location.Kind == LocationKind.Equipped &&
                value.Location.Parent == bodyId)
            .OrderBy(value => value.Id)
            .ToArray();
        var (result, transfers) = PlanStowOrSpill(objects, body, worn);
        try
        {
            objects.CommitCombatMutation(new CombatMutation(
                bodyId,
                Transfers: transfers,
                Transform: new CombatTransform(
                    bodyId, typeId, name, shapeId, flags, height)));
            return result;
        }
        catch (ObjectTransferException exception)
        {
            return new CorpseResult(false, exception.Message, [], []);
        }
    }

    private static (CorpseResult Result, IReadOnlyList<ObjectTransferRequest> Transfers)
        PlanStowOrSpill(
        ObjectStore objects,
        WorldObject body,
        IReadOnlyList<WorldObject> worn)
    {
        var into = ObjectLocation.InContainer(body.Id);
        var carried = objects.GetContents(body.Id)
            .Select(objects.Get)
            .ToList();
        var requests = new List<ObjectTransferRequest>();
        var stowed = new List<ObjectId>();
        var spilled = new List<ObjectId>();
        foreach (var item in worn)
        {
            var projected = carried
                .Append(item with { Location = into })
                .ToArray();
            var fits = GearSlots.UsedBy(projected) <= body.CarryCapacity;
            requests.Add(
                new ObjectTransferRequest(
                    item.Id,
                    item.Location,
                    fits ? into : body.Location));
            if (fits)
            {
                carried.Add(item with { Location = into });
                stowed.Add(item.Id);
            }
            else
            {
                spilled.Add(item.Id);
            }
        }

        if (requests.Count == 0)
        {
            return (new CorpseResult(true, "The body carried nothing worn.", [], []), []);
        }

        return (new CorpseResult(
            true,
            spilled.Count == 0
                ? $"The body's gear stays with it ({stowed.Count} items)."
                : $"{spilled.Count} of the body's things spill onto the ground.",
            stowed,
            spilled), requests);
    }
}
