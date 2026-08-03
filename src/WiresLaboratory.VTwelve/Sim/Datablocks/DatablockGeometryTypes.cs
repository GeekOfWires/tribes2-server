namespace WiresLaboratory.VTwelve.Sim.Datablocks;

// The five composite field types whose identity AND size are both confirmed by
// RecoveredDatablockFields.tsv: the type_guess label carries a parenthesized byte count
// ("ColorI(4B)", "ColorF(16B)", "Point2I(8B)", "Point3F(12B)", "RectI(16B)") and every row of
// that type code agrees with its own type_size_bytes column — see
// DatablockFieldTypeCatalog.LabelSizeMismatches, which checks exactly that and finds only one
// disagreement in the whole table (type code 3, "bool(4)" vs. a recorded size of 1 — not one of
// these five). That self-consistency, not external documentation, is why these five get real
// CLR struct types while every other composite/pointer type code is left as raw, untyped
// storage in <see cref="DatablockFieldTypeCatalog"/>.
//
// Field layout mirrors Torque's own (channel/component order), but nothing here has been
// checked against a live process — these are storage shapes for the recovered offsets, not a
// verified wire or memory format.

/// <summary>Type code 11, <c>ColorI(4B)</c> — four 8-bit channels.</summary>
public readonly record struct DatablockColorI(byte Red, byte Green, byte Blue, byte Alpha);

/// <summary>Type code 12, <c>ColorF(16B)</c> — four 32-bit float channels.</summary>
public readonly record struct DatablockColorF(float Red, float Green, float Blue, float Alpha);

/// <summary>Type code 14, <c>Point2I(8B)</c> — two 32-bit integers.</summary>
public readonly record struct DatablockPoint2I(int X, int Y);

/// <summary>Type code 16, <c>Point3F(12B)</c> — three 32-bit floats.</summary>
public readonly record struct DatablockPoint3F(float X, float Y, float Z);

/// <summary>Type code 18, <c>RectI(16B)</c> — an origin point plus a width/height extent.</summary>
public readonly record struct DatablockRectI(int X, int Y, int Width, int Height);
