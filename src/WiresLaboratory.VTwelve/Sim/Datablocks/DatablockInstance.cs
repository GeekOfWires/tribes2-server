namespace WiresLaboratory.VTwelve.Sim.Datablocks;

/// <summary>
/// Thrown by <see cref="DatablockInstance"/> for a field name unknown to its class (own or
/// inherited), or a <see cref="DatablockInstance.Set{T}"/> whose value doesn't fit the field's
/// declared CLR type.
/// </summary>
public sealed class DatablockFieldAccessException(string message) : Exception(message);

/// <summary>
/// A single instance of a datablock (or other <c>initPersistFields</c>-registered) class: a
/// <see cref="DatablockClassDefinition"/> plus a value per field, own and inherited. This is a
/// value store only — it does not parse TorqueScript, resolve datablock-name references, or
/// touch the network layer; it exists so the field model in this namespace has somewhere to put
/// values once something upstream (a script loader, a ghost decoder) has them.
/// </summary>
/// <remarks>
/// Every field in <see cref="DatablockFieldRegistry.GetAllFields"/> gets a slot, initialized to
/// a type-appropriate default — see <see cref="CreateDefault"/>. Fields whose CLR type is
/// unknown (<see cref="DatablockFieldTypeInfo.ClrType"/> is <see langword="null"/> — an
/// <see cref="DatablockFieldConfidence.Unconfirmed"/> type code, or a datablock/profile
/// reference this model has no live directory to resolve through) default to
/// <see langword="null"/> and accept any value on <see cref="Set{T}"/> rather than rejecting
/// everything, since there is no known shape to check against.
/// </remarks>
public sealed class DatablockInstance
{
    private readonly DatablockFieldRegistry _registry;
    private readonly Dictionary<string, DatablockFieldDescriptor> _fieldsByName;
    private readonly Dictionary<string, object?> _values;

    public DatablockInstance(DatablockFieldRegistry registry, string className)
    {
        if (!registry.Classes.ContainsKey(className))
            throw new DatablockFieldAccessException($"'{className}' is not a registered class.");

        _registry = registry;
        ClassName = className;
        var allFields = registry.GetAllFields(className);
        _fieldsByName = new Dictionary<string, DatablockFieldDescriptor>(allFields.Count);
        _values = new Dictionary<string, object?>(allFields.Count);
        foreach (var field in allFields)
        {
            // Own fields are enumerated before inherited ones (see GetAllFields); if a name
            // were ever repeated the more-derived declaration wins, which is the direction
            // shadowing should resolve in even though the recovered data has no such case today.
            if (_fieldsByName.ContainsKey(field.FieldName)) continue;
            _fieldsByName[field.FieldName] = field;
            _values[field.FieldName] = CreateDefault(registry, field);
        }
    }

    /// <summary>The class this instance was constructed for.</summary>
    public string ClassName { get; }

    /// <summary>Every field name this instance holds a value for, own and inherited.</summary>
    public IReadOnlyCollection<string> FieldNames => _fieldsByName.Keys;

    /// <summary>The field descriptor backing <paramref name="fieldName"/>.</summary>
    public DatablockFieldDescriptor Describe(string fieldName) =>
        _fieldsByName.TryGetValue(fieldName, out var field)
            ? field
            : throw new DatablockFieldAccessException($"{ClassName} has no field '{fieldName}'.");

    /// <summary>
    /// Reads <paramref name="fieldName"/>'s current value as <typeparamref name="T"/>. For an
    /// array field (<see cref="DatablockFieldDescriptor.ElementCount"/> &gt; 1) this is
    /// <c>T[]</c>; for a scalar field it is <typeparamref name="T"/> directly.
    /// </summary>
    public T? Get<T>(string fieldName)
    {
        Describe(fieldName); // validates the name; throws with a clear message if unknown
        var value = _values[fieldName];
        return value switch
        {
            null => default,
            T typed => typed,
            _ => throw new DatablockFieldAccessException(
                $"{ClassName}.{fieldName} holds a {value.GetType().Name}, not {typeof(T).Name}."),
        };
    }

    /// <summary>Reads <paramref name="fieldName"/>'s current value, boxed, without a type check.</summary>
    public object? GetValue(string fieldName)
    {
        Describe(fieldName);
        return _values[fieldName];
    }

    /// <summary>
    /// Sets <paramref name="fieldName"/> to <paramref name="value"/>. For a scalar field with a
    /// known <see cref="DatablockFieldTypeInfo.ClrType"/>, <typeparamref name="T"/> must match
    /// that type (or be <see langword="null"/>) or this throws — see the remarks on why unknown
    /// or array-typed fields skip that check.
    /// </summary>
    public void Set<T>(string fieldName, T? value)
    {
        var field = Describe(fieldName);
        if (field.ElementCount <= 1 && _registry.Types.TryGetValue(field.TypeCode, out var info) && info.ClrType is Type expected)
        {
            if (value is not null && !expected.IsInstanceOfType(value))
            {
                throw new DatablockFieldAccessException(
                    $"{ClassName}.{fieldName} expects {expected.Name}, got {typeof(T).Name}.");
            }
        }
        _values[fieldName] = value;
    }

    /// <summary>
    /// Builds a field's default value: the type's default element (see
    /// <see cref="CreateDefaultElement"/>) for a scalar field, or an array of
    /// <see cref="DatablockFieldDescriptor.ElementCount"/> such defaults for an array field.
    /// Scalars default to their CLR type's zero value where the type is known (0, <see
    /// langword="false"/>, <see cref="string.Empty"/>, a zeroed struct); fields with no known
    /// CLR type — including every datablock/profile reference, since resolving one needs a live
    /// name-to-instance directory this static model doesn't have — default to <see
    /// langword="null"/>.
    /// </summary>
    private static object? CreateDefault(DatablockFieldRegistry registry, DatablockFieldDescriptor field)
    {
        registry.Types.TryGetValue(field.TypeCode, out var info);
        if (field.ElementCount <= 1) return CreateDefaultElement(info);

        // A real element-typed array (int[], DatablockPoint3F[], ...) when the element type is
        // known, so Get<T[]> below sees a value that is actually a T[] at runtime rather than
        // an object[] that would never pattern-match. Falls back to object[] only when the
        // element type itself isn't known (unconfirmed or reference type codes).
        var elementType = info?.ClrType ?? typeof(object);
        var array = Array.CreateInstance(elementType, field.ElementCount);
        var defaultElement = CreateDefaultElement(info);
        for (var i = 0; i < field.ElementCount; i++) array.SetValue(defaultElement, i);
        return array;
    }

    private static object? CreateDefaultElement(DatablockFieldTypeInfo? info) => info?.ClrType switch
    {
        null => null,
        var t when t == typeof(int) => 0,
        var t when t == typeof(bool) => false,
        var t when t == typeof(float) => 0f,
        var t when t == typeof(string) => string.Empty,
        var t when t == typeof(DatablockColorI) => default(DatablockColorI),
        var t when t == typeof(DatablockColorF) => default(DatablockColorF),
        var t when t == typeof(DatablockPoint2I) => default(DatablockPoint2I),
        var t when t == typeof(DatablockPoint3F) => default(DatablockPoint3F),
        var t when t == typeof(DatablockRectI) => default(DatablockRectI),
        _ => null,
    };
}
