namespace Mo.Engine;

/// <summary>Which wheel an operation appears on.</summary>
public enum WheelKind { Convert, Tools, Actions }

/// <summary>
/// One thing Mo Converter can do with dragged files. Each operation is a small self-describing class;
/// adding a feature means adding one class and registering it in its module.
/// </summary>
public interface IOperation
{
    /// <summary>Stable id such as "image.to-png". Used by the CLI and by settings.</summary>
    string Id { get; }

    WheelKind Wheel { get; }

    /// <summary>Localization key of the full title, for example "op.image.to-png".</summary>
    string TitleKey { get; }

    /// <summary>Short text drawn on the wheel petal ("PNG"). Null means the wheel shows the glyph and title.</summary>
    string? Badge { get; }

    /// <summary>Segoe Fluent Icons character for the petal.</summary>
    string Glyph { get; }

    /// <summary>Position on the wheel; lower comes first.</summary>
    int Order { get; }

    /// <summary>
    /// Id of the tool window the app opens for this operation (for example "image.crop").
    /// Null means the operation runs straight away with its defaults.
    /// </summary>
    string? EditorId => null;

    bool Accepts(InputSet input);

    Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation);
}

/// <summary>Everything an operation needs for one run.</summary>
public sealed record OperationContext(
    InputSet Input,
    OutputPlanner Output,
    IProgress<OperationProgress>? Progress = null,
    IReadOnlyDictionary<string, object?>? Options = null)
{
    public T Option<T>(string name, T fallback) =>
        Options is not null && Options.TryGetValue(name, out var value) && value is T typed ? typed : fallback;

    public void Report(double fraction, string? detail = null) => Progress?.Report(new OperationProgress(fraction, detail));
}

/// <summary>Fraction is 0..1 across the whole run.</summary>
public readonly record struct OperationProgress(double Fraction, string? Detail = null);

/// <summary>Files an operation produced, plus an optional localized message for the toast.</summary>
public sealed record OperationResult(IReadOnlyList<string> Outputs, string? MessageKey = null, object?[]? MessageArgs = null)
{
    public static OperationResult Of(params string[] outputs) => new(outputs);
    public static OperationResult Message(string key, params object?[] args) => new([], key, args);
}

/// <summary>A failure the user should read. Key is a localization key; Args fill its placeholders.</summary>
public sealed class OperationException(string key, params object?[] args) : Exception(key)
{
    public string Key { get; } = key;
    public object?[] Args { get; } = args;
}
