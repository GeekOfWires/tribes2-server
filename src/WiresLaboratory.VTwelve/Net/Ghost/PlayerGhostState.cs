using System.Numerics;

namespace WiresLaboratory.VTwelve.Net.Ghost;

/// <summary>
/// The <c>Player +0x894</c> sub-object inside the move/position block: three optional 16-bit
/// fields and three 6-bit fields. Per <c>GhostProtocol.md</c>'s "Unidentified" section, "structure
/// recovered, semantics unknown" — so this type carries the recovered shape only, with neutral
/// field names.
/// </summary>
public struct PlayerMoveSubObjectFields
{
    /// <summary>First of three optional 16-bit fields (each individually flag-gated).</summary>
    public ushort? Optional16A;
    /// <summary>Second of three optional 16-bit fields.</summary>
    public ushort? Optional16B;
    /// <summary>Third of three optional 16-bit fields.</summary>
    public ushort? Optional16C;

    /// <summary>First of three unconditional 6-bit fields.</summary>
    public uint Field6A;
    /// <summary>Second of three unconditional 6-bit fields.</summary>
    public uint Field6B;
    /// <summary>Third of three unconditional 6-bit fields.</summary>
    public uint Field6C;
}

/// <summary>
/// Bit 25 payload ("action animation"): an 8-bit sequence, two unconditional flags, and a third
/// flag that gates an optional 6-bit signed-float position.
/// </summary>
public struct PlayerActionAnimationFields
{
    /// <summary>8-bit action animation sequence.</summary>
    public uint Action;

    /// <summary>First of two unconditional flags following <see cref="Action"/>; not named by the document.</summary>
    public bool Flag0;
    /// <summary>Second of two unconditional flags; not named by the document.</summary>
    public bool Flag1;

    /// <summary>
    /// Optional 6-bit signed-float position. The document lists "3 flags, optional
    /// writeSignedFloat(pos, 6)" — modelled here as the third flag being the presence gate for
    /// this field (the natural reading of "N flags, optional field" when only one of them could
    /// plausibly gate the field that immediately follows).
    /// </summary>
    public float? Position;
}

/// <summary>Bit 26 payload ("move + position") — the largest single block in the format.</summary>
public struct PlayerMoveFields
{
    /// <summary>Leading unconditional 3-bit field; not named by the document.</summary>
    public uint LeadingField3;

    /// <summary>Optional 7-bit field, gated by the flag immediately preceding it.</summary>
    public byte? Optional7;

    /// <summary>Two unconditional flags following the optional 7-bit field; not named by the document.</summary>
    public bool Flag0;
    /// <summary>See <see cref="Flag0"/>.</summary>
    public bool Flag1;

    /// <summary>Absolute world position, sent via <c>writeCompressedPoint(position, 0.01)</c>.</summary>
    public Vector3 Position;

    /// <summary>
    /// Velocity, split as magnitude (13-bit, <c>min(len*32, 8191)</c> — 1/32 unit precision,
    /// clamped to 255.97 units/s) and a 10-bit-per-angle direction (21 bits). Magnitude is sent
    /// before direction.
    /// </summary>
    public Vector3 Velocity;

    /// <summary>First look angle (yaw), 6-bit signed float.</summary>
    public float LookYaw;
    /// <summary>Second look angle (pitch), 6-bit signed float.</summary>
    public float LookPitch;

    /// <summary>Body rotation as a fraction of a full turn (<c>rot * 1/2pi</c>), 7-bit unsigned float.</summary>
    public float RotationFraction;

    /// <summary><c>Player +0x894</c> — see <see cref="PlayerMoveSubObjectFields"/>.</summary>
    public PlayerMoveSubObjectFields SubObject;

    /// <summary>
    /// The move block's trailing flag: <c>!(mask &amp; NoWarp)</c> on the write side. On Pack this
    /// field is not consulted — the value is derived straight from the mask bit, since that is
    /// what the recovered code does; on Unpack it is populated from the flag actually read,
    /// since the mask itself is never available to a decoder.
    /// </summary>
    public bool WarpAllowed;
}

/// <summary>
/// <c>Player::packUpdate</c> (<c>0x5dae80</c>) / <c>unpackUpdate</c> (<c>0x5db2d0</c>). Writes
/// <see cref="ShapeBaseGhostState"/>'s fields (which in turn write <see cref="GameBaseGhostState"/>'s)
/// first, then its own — the top of the <c>Player -&gt; ShapeBase -&gt; GameBase</c> chain.
/// </summary>
/// <remarks>
/// <para>
/// The most consequential field in this class is <see cref="Pack"/>'s
/// <paramref name="suppressOwnerState"/> parameter: <c>writeFlag(controllingClient == this
/// connection &amp;&amp; !initial)</c> — if that condition is true, <c>packUpdate</c> returns
/// immediately, writing nothing more. In other words, a connection is never sent its own
/// predicted movement state (after the initial ghost). This codec cannot know which connection
/// is "this" one — that is a property of the server's connection/ownership model, which lives
/// outside the wire format — so it is threaded through as an explicit parameter the caller
/// computes.
/// </para>
/// <para>
/// Mask bits 25/26/27 map onto this class's three leading fields, in order: bit 27 first (a
/// bare 3-bit field), then bit 25 (action animation), then — after the owner short-circuit —
/// bit 26 (move + position). That ordering, and which bit gates which block, follows the
/// document's own annotations ("(bit 27)" on the first field, and the mask table's "action anim /
/// move+position / 3-bit field" ordering for bits 25/26/27 respectively).
/// </para>
/// </remarks>
public class PlayerGhostState : ShapeBaseGhostState
{
    /// <summary>Bit 27 payload: a bare 3-bit field. Meaning not identified by the document.</summary>
    public uint UnknownField27;

    public PlayerActionAnimationFields ActionAnimation;

    /// <summary>
    /// Item 3 in the document's field list: <c>writeFlag -&gt; writeInt(.., 8)</c>, an optional
    /// 8-bit field not tied to any of the three Player mask bits (all three are already
    /// accounted for by <see cref="UnknownField27"/>, <see cref="ActionAnimation"/> and the move
    /// block) — so this one is state-based, like the owner short-circuit that follows it, not
    /// mask-gated.
    /// </summary>
    public byte? UnnamedOptionalField;

    public PlayerMoveFields Move;

    /// <summary><c>energy / maxEnergy</c>, quantised to 5 bits. Always sent, unconditionally, at the very end of the pack.</summary>
    public float EnergyFraction;

    /// <param name="isInitial">Whether this is the object's initial ghost to this connection.</param>
    /// <param name="suppressOwnerState">
    /// <c>controllingClient == this connection &amp;&amp; !isInitial</c> — computed by the
    /// caller, since ownership is outside this type's scope. When true, nothing beyond this flag
    /// is written.
    /// </param>
    /// <param name="scopePosition">The connection's current scope position, for <see cref="Move"/>'s compressed-point position.</param>
    public void Pack(BitStream stream, GhostMaskBits mask, bool isInitial, bool suppressOwnerState, Vector3? scopePosition)
    {
        base.Pack(stream, mask, isInitial);

        var hasField27 = (mask & GhostMaskBits.PlayerUnknownField27) != 0;
        stream.WriteFlag(hasField27);
        if (hasField27) stream.WriteInt(UnknownField27, 3);

        // The document's prose for this block ("writeInt(action,8), 3 flags, optional
        // writeSignedFloat(pos,6)") does not spell out a leading gate flag the way the sound/anim
        // thread blocks explicitly do — but the mask is never itself transmitted on the wire (see
        // GameBaseGhostState.Unpack's remarks), so an optional block MUST carry its own presence
        // flag or a decoder could never know it was skipped. Every other mask-owned block in this
        // format does exactly that, so one is modelled here too, consistent with that pattern
        // rather than with the terseness of this one line of prose.
        var hasActionAnimation = (mask & GhostMaskBits.PlayerActionAnimation) != 0;
        stream.WriteFlag(hasActionAnimation);
        if (hasActionAnimation)
        {
            stream.WriteInt(ActionAnimation.Action, 8);
            stream.WriteFlag(ActionAnimation.Flag0);
            stream.WriteFlag(ActionAnimation.Flag1);
            var hasPosition = ActionAnimation.Position.HasValue;
            stream.WriteFlag(hasPosition);
            if (hasPosition) stream.WriteSignedFloat(ActionAnimation.Position!.Value, 6);
        }

        var hasField3 = UnnamedOptionalField.HasValue;
        stream.WriteFlag(hasField3);
        if (hasField3) stream.WriteInt(UnnamedOptionalField!.Value, 8);

        stream.WriteFlag(suppressOwnerState);
        if (suppressOwnerState) return;

        // Same reasoning as the action-animation gate above: the block needs its own wire flag
        // to be decodable, even though the document's prose does not call it out explicitly.
        var hasMove = (mask & GhostMaskBits.PlayerMoveAndPosition) != 0;
        stream.WriteFlag(hasMove);
        if (hasMove)
        {
            stream.WriteInt(Move.LeadingField3, 3);

            var hasOptional7 = Move.Optional7.HasValue;
            stream.WriteFlag(hasOptional7);
            if (hasOptional7) stream.WriteInt(Move.Optional7!.Value, 7);

            stream.WriteFlag(Move.Flag0);
            stream.WriteFlag(Move.Flag1);

            stream.WriteCompressedPoint(Move.Position, scopePosition);

            var speed = Move.Velocity.Length();
            var magnitude = (uint)Math.Min((int)MathF.Round(speed * 32f), 8191);
            stream.WriteInt(magnitude, 13);
            var direction = speed > 1e-6f ? Move.Velocity / speed : Vector3.Zero;
            stream.WriteNormalVector(direction, 10);

            stream.WriteSignedFloat(Move.LookYaw, 6);
            stream.WriteSignedFloat(Move.LookPitch, 6);
            stream.WriteFloat(Move.RotationFraction, 7);

            var sub = Move.SubObject;
            WriteOptional16(stream, sub.Optional16A);
            WriteOptional16(stream, sub.Optional16B);
            WriteOptional16(stream, sub.Optional16C);
            stream.WriteInt(sub.Field6A, 6);
            stream.WriteInt(sub.Field6B, 6);
            stream.WriteInt(sub.Field6C, 6);

            // Bit 4 (NoWarp): the move block's own trailing flag is the negation of that bit.
            stream.WriteFlag((mask & GhostMaskBits.NoWarp) == 0);
        }

        stream.WriteFloat(EnergyFraction, 5);
    }

    /// <summary>
    /// Inverse of <see cref="Pack"/>. Returns after the owner-suppression flag if that flag was
    /// set, matching the writer's early return. Takes no <c>mask</c> parameter — see
    /// <see cref="GameBaseGhostState.Unpack"/> — <paramref name="scopePosition"/> must be the
    /// same value the writer used for <see cref="Move"/>'s compressed point.
    /// </summary>
    public void Unpack(BitStream stream, bool isInitial, Vector3? scopePosition)
    {
        base.Unpack(stream, isInitial);

        if (stream.ReadFlag())
            UnknownField27 = stream.ReadInt(3);

        if (stream.ReadFlag())
        {
            ActionAnimation.Action = stream.ReadInt(8);
            ActionAnimation.Flag0 = stream.ReadFlag();
            ActionAnimation.Flag1 = stream.ReadFlag();
            ActionAnimation.Position = stream.ReadFlag() ? stream.ReadSignedFloat(6) : null;
        }

        if (stream.ReadFlag())
            UnnamedOptionalField = (byte)stream.ReadInt(8);

        var suppressed = stream.ReadFlag();
        if (suppressed) return;

        if (stream.ReadFlag())
        {
            Move.LeadingField3 = stream.ReadInt(3);

            Move.Optional7 = stream.ReadFlag() ? (byte)stream.ReadInt(7) : null;

            Move.Flag0 = stream.ReadFlag();
            Move.Flag1 = stream.ReadFlag();

            Move.Position = stream.ReadCompressedPoint(scopePosition);

            var magnitude = stream.ReadInt(13);
            var direction = stream.ReadNormalVector(10);
            Move.Velocity = direction * (magnitude / 32f);

            Move.LookYaw = stream.ReadSignedFloat(6);
            Move.LookPitch = stream.ReadSignedFloat(6);
            Move.RotationFraction = stream.ReadFloat(7);

            Move.SubObject = new PlayerMoveSubObjectFields
            {
                Optional16A = ReadOptional16(stream),
                Optional16B = ReadOptional16(stream),
                Optional16C = ReadOptional16(stream),
                Field6A = stream.ReadInt(6),
                Field6B = stream.ReadInt(6),
                Field6C = stream.ReadInt(6),
            };

            // The writer computes this flag from `mask & NoWarp` (a write-side-only value); the
            // reader has no access to that mask, so it just captures the boolean the wire
            // actually carries.
            Move.WarpAllowed = stream.ReadFlag();
        }

        EnergyFraction = stream.ReadFloat(5);
    }

    private static void WriteOptional16(BitStream stream, ushort? value)
    {
        var has = value.HasValue;
        stream.WriteFlag(has);
        if (has) stream.WriteInt(value!.Value, 16);
    }

    private static ushort? ReadOptional16(BitStream stream) =>
        stream.ReadFlag() ? (ushort)stream.ReadInt(16) : null;
}
