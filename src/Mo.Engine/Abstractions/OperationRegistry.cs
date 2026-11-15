namespace Mo.Engine;

/// <summary>Holds every operation and answers "what fits these files on this wheel?".</summary>
public sealed class OperationRegistry
{
    private readonly List<IOperation> _operations = [];

    public IReadOnlyList<IOperation> All => _operations;

    public OperationRegistry Add(IOperation operation)
    {
        if (_operations.Any(o => o.Id == operation.Id))
            throw new InvalidOperationException($"Duplicate operation id: {operation.Id}");
        _operations.Add(operation);
        return this;
    }

    public OperationRegistry AddRange(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations) Add(operation);
        return this;
    }

    public IOperation? Find(string id) => _operations.FirstOrDefault(o => o.Id == id);

    public IReadOnlyList<IOperation> Match(WheelKind wheel, InputSet input) =>
        input.Count == 0
            ? []
            : _operations.Where(o => o.Wheel == wheel && o.Accepts(input)).OrderBy(o => o.Order).ToList();

    /// <summary>The registry the app and the CLI use. Each feature area registers itself in Modules.cs.</summary>
    public static OperationRegistry CreateDefault()
    {
        var registry = new OperationRegistry();
        Modules.RegisterAll(registry);
        return registry;
    }
}
