using System.Globalization;
using System.Text.RegularExpressions;

namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// A TorqueScript console value: at the console boundary — command arguments, return values,
/// object field reads — everything is text, and the callee coerces it to int/float/bool on
/// demand. This type models that coercion rather than picking one C# type up front, because the
/// engine's own semantics (see the argument lists in EngineConsoleSurface.md, e.g. a vector
/// arriving as the single string <c>"1 2 3"</c>) depend on the string being the canonical form
/// and numbers being a derived, lossy view of it.
/// </summary>
/// <remarks>
/// <para>
/// Numeric coercion mirrors C's <c>atoi</c>/<c>atof</c> rather than .NET's strict
/// <c>int.Parse</c>/<c>float.Parse</c>: it reads the longest valid numeric prefix and ignores
/// anything after it (so <c>"3 dogs".AsInt()</c> is <c>3</c>, not a parse failure), and an
/// unparsable value is <c>0</c>, never an exception. <see cref="AsInt"/> and <see cref="AsFloat"/>
/// use different grammars on purpose (int: no decimal point or exponent; float: both) because
/// that is the actual divergence between <c>atoi</c> and <c>atof</c> — e.g. <c>AsInt()</c> on
/// <c>"1e3"</c> is <c>1</c> (stops at 'e'), while <c>AsFloat()</c> on the same text is
/// <c>1000</c>. TorqueScript numeric literals are essentially never written in scientific
/// notation, so this divergence is rarely observable in practice, but it is deliberate rather
/// than an oversight.
/// </para>
/// <para>
/// <see cref="AsBool"/> is <c>AsFloat() != 0</c>, not a check for the strings "true"/"false".
/// This is the documented behaviour across the Torque lineage (<c>dAtob</c> is a thin wrapper
/// over <c>dAtof</c>), not something the recovered manifest itself proves for this specific T2
/// binary — it names commands and argument counts, not internal coercion helpers. Treat this as
/// an informed default: <c>ConsoleValue("true").AsBool()</c> is <see langword="false"/>, because
/// <c>true</c>/<c>false</c> are compiled to <c>1</c>/<c>0</c> by the TorqueScript compiler itself
/// and never reach this layer as literal text unless something (e.g. a quoted string) put them
/// there deliberately.
/// </para>
/// </remarks>
public readonly struct ConsoleValue : IEquatable<ConsoleValue>
{
    /// <summary>The console's "undefined"/unset value — an empty string, coercing to 0/false.</summary>
    public static readonly ConsoleValue Empty = new((string?)null);

    private static readonly Regex LeadingIntPattern = new(@"^\s*[-+]?\d+", RegexOptions.Compiled);

    private static readonly Regex LeadingFloatPattern =
        new(@"^\s*[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?", RegexOptions.Compiled);

    private readonly string? _raw;

    /// <summary>The canonical string form. Never <see langword="null"/> — an unset value reads as "".</summary>
    public string Raw => _raw ?? string.Empty;

    public bool IsEmpty => Raw.Length == 0;

    public ConsoleValue(string? raw) => _raw = raw;

    public ConsoleValue(int value) => _raw = value.ToString(CultureInfo.InvariantCulture);

    public ConsoleValue(bool value) => _raw = value ? "1" : "0";

    public ConsoleValue(float value) => _raw = FormatFloat(value);

    public ConsoleValue(double value) => _raw = FormatFloat((float)value);

    public static implicit operator ConsoleValue(string? value) => new(value);
    public static implicit operator ConsoleValue(int value) => new(value);
    public static implicit operator ConsoleValue(bool value) => new(value);
    public static implicit operator ConsoleValue(float value) => new(value);
    public static implicit operator ConsoleValue(double value) => new(value);

    /// <summary><c>atoi</c>-style leading-integer parse. Unparsable text reads as 0, never throws.</summary>
    public int AsInt()
    {
        var m = LeadingIntPattern.Match(Raw);
        if (!m.Success) return 0;
        // A run of digits longer than int can hold is clamped rather than wrapped: C's atoi is
        // undefined behaviour on overflow, and a silent wrap would be a worse re-implementation
        // of "undefined" than a documented clamp.
        return int.TryParse(m.Value.AsSpan().Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : m.Value.Contains('-') ? int.MinValue : int.MaxValue;
    }

    /// <summary><c>atof</c>-style leading-float parse (digits, one decimal point, optional exponent).</summary>
    public float AsFloat()
    {
        var m = LeadingFloatPattern.Match(Raw);
        if (!m.Success) return 0f;
        return float.TryParse(m.Value.AsSpan().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    /// <summary>Numeric truthiness (<c>AsFloat() != 0</c>) — see the type-level remarks.</summary>
    public bool AsBool() => AsFloat() != 0f;

    public override string ToString() => Raw;

    public bool Equals(ConsoleValue other) => Raw == other.Raw;

    public override bool Equals(object? obj) => obj is ConsoleValue other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    public static bool operator ==(ConsoleValue left, ConsoleValue right) => left.Equals(right);

    public static bool operator !=(ConsoleValue left, ConsoleValue right) => !left.Equals(right);

    /// <summary>
    /// Formats a float the way the console prints numeric results: compact, roughly 6
    /// significant digits, no forced trailing zeros. .NET's round-trip "G6" is the closest
    /// built-in match to C's default <c>%g</c>; the only adjustment is lower-casing the
    /// exponent marker ("E+06" -&gt; "e+06") to match C's convention. This is an approximation,
    /// not a recovered fact — the manifest records argument *shapes*, not the engine's exact
    /// float-to-string formatting routine.
    /// </summary>
    internal static string FormatFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
        var s = value.ToString("G6", CultureInfo.InvariantCulture);
        return s.Contains('E') ? s.Replace("E", "e") : s;
    }
}
