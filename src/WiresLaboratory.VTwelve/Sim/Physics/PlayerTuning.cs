using WiresLaboratory.VTwelve.Sim.Datablocks;

namespace WiresLaboratory.VTwelve.Sim.Physics;

/// <summary>
/// The subset of <c>PlayerData</c>'s (and inherited <c>ShapeBaseData</c>'s) tunables the
/// movement model reads, plus the two derived cosine fields the engine precomputes at preload.
/// </summary>
/// <remarks>
/// <para>
/// Built from a <see cref="DatablockInstance"/> of class <c>PlayerData</c> — see
/// <see cref="FromDatablock"/> — rather than duplicating field storage. Every field name below
/// is a verified row in <c>Sim/RecoveredDatablockFields.tsv</c> (own on <c>PlayerData</c> unless
/// noted as inherited from <c>ShapeBaseData</c>/<c>GameBaseData</c>).
/// </para>
/// <para>
/// <b>Surface angles are cosines, not degrees</b> — <c>PlayerPhysics.md</c> ("Surface angles are
/// precomputed, not read directly"): <c>PlayerData::preload</c> (<c>0x005cddf0</c>) converts
/// <c>runSurfaceAngle</c>/<c>jumpSurfaceAngle</c> once into cosines stored in derived fields at
/// <c>PlayerData+0xcd8</c>/<c>+0xcdc</c>, which are not registered <c>addField</c> fields and so
/// do not appear in the TSV. <see cref="RunSurfaceAngleCosine"/>/<see cref="JumpSurfaceAngleCosine"/>
/// reproduce that conversion at construction time; <see cref="PlayerForcePhase"/> and the
/// collision code compare <c>contactNormal.Z</c> against these, never against a degree value.
/// </para>
/// <para>
/// <b>No <c>gravityMod</c> field:</b> unlike <c>ItemData</c>/<c>GrenadeProjectileData</c>/
/// <c>PhysicalZone</c>, neither <c>PlayerData</c> nor its ancestor chain
/// (<c>ShapeBaseData</c>/<c>GameBaseData</c>) registers a <c>gravityMod</c> field in the TSV.
/// That is consistent with <c>PlayerPhysics.md</c>: gravity is a mutable engine global
/// (<see cref="PhysicsWorld.Gravity"/>), not a per-datablock multiplier, for the player.
/// </para>
/// </remarks>
public sealed class PlayerTuning
{
    public required float Mass { get; init; }
    public required float Drag { get; init; }
    public required float Density { get; init; }
    public required float MaxEnergy { get; init; }

    public required float MaxForwardSpeed { get; init; }
    public required float MaxBackwardSpeed { get; init; }
    public required float MaxSideSpeed { get; init; }

    public required float RunForce { get; init; }
    public required float RunEnergyDrain { get; init; }
    public required float MinRunEnergy { get; init; }

    public required float MaxStepHeight { get; init; }

    /// <summary>Cosine of <c>runSurfaceAngle</c> (degrees in the datablock), precomputed once — see the type remarks.</summary>
    public required float RunSurfaceAngleCosine { get; init; }

    public required float HorizMaxSpeed { get; init; }
    public required float HorizResistSpeed { get; init; }
    public required float HorizResistFactor { get; init; }

    public required float UpMaxSpeed { get; init; }
    public required float UpResistSpeed { get; init; }
    public required float UpResistFactor { get; init; }

    public required float JumpForce { get; init; }
    public required float JumpEnergyDrain { get; init; }
    public required float MinJumpEnergy { get; init; }
    public required float MinJumpSpeed { get; init; }
    public required float MaxJumpSpeed { get; init; }

    /// <summary>Cosine of <c>jumpSurfaceAngle</c> (degrees in the datablock), precomputed once — see the type remarks.</summary>
    public required float JumpSurfaceAngleCosine { get; init; }

    /// <summary>Ticks a jump must cool down for before another may fire (datablock <c>jumpDelay</c>, integer ticks).</summary>
    public required int JumpDelayTicks { get; init; }

    public required float JetForce { get; init; }
    public required float UnderwaterJetForce { get; init; }
    public required float JetEnergyDrain { get; init; }
    public required float MinJetEnergy { get; init; }
    public required float MaxJetForwardSpeed { get; init; }
    public required float MaxJetHorizontalPercentage { get; init; }

    /// <summary>
    /// Builds tuning from a <c>PlayerData</c> <see cref="DatablockInstance"/>. Throws (via
    /// <see cref="DatablockInstance.Get{T}"/>/<see cref="DatablockInstance.Describe"/>) if the
    /// instance isn't class <c>PlayerData</c> or a field name below isn't registered — both would
    /// mean the TSV or the registry changed underneath this reader.
    /// </summary>
    public static PlayerTuning FromDatablock(DatablockInstance playerData)
    {
        float F(string name) => playerData.Get<float>(name);
        int I(string name) => playerData.Get<int>(name);

        return new PlayerTuning
        {
            Mass = F("mass"),
            Drag = F("drag"),
            Density = F("density"),
            MaxEnergy = F("maxEnergy"),

            MaxForwardSpeed = F("maxForwardSpeed"),
            MaxBackwardSpeed = F("maxBackwardSpeed"),
            MaxSideSpeed = F("maxSideSpeed"),

            RunForce = F("runForce"),
            RunEnergyDrain = F("runEnergyDrain"),
            MinRunEnergy = F("minRunEnergy"),

            MaxStepHeight = F("maxStepHeight"),
            RunSurfaceAngleCosine = MathF.Cos(F("runSurfaceAngle") * DegreesToRadians),

            HorizMaxSpeed = F("horizMaxSpeed"),
            HorizResistSpeed = F("horizResistSpeed"),
            HorizResistFactor = F("horizResistFactor"),

            UpMaxSpeed = F("upMaxSpeed"),
            UpResistSpeed = F("upResistSpeed"),
            UpResistFactor = F("upResistFactor"),

            JumpForce = F("jumpForce"),
            JumpEnergyDrain = F("jumpEnergyDrain"),
            MinJumpEnergy = F("minJumpEnergy"),
            MinJumpSpeed = F("minJumpSpeed"),
            MaxJumpSpeed = F("maxJumpSpeed"),
            JumpSurfaceAngleCosine = MathF.Cos(F("jumpSurfaceAngle") * DegreesToRadians),
            JumpDelayTicks = I("jumpDelay"),

            JetForce = F("jetForce"),
            UnderwaterJetForce = F("underwaterJetForce"),
            JetEnergyDrain = F("jetEnergyDrain"),
            MinJetEnergy = F("minJetEnergy"),
            MaxJetForwardSpeed = F("maxJetForwardSpeed"),
            MaxJetHorizontalPercentage = F("maxJetHorizontalPercentage"),
        };
    }

    private const float DegreesToRadians = MathF.PI / 180f;
}
