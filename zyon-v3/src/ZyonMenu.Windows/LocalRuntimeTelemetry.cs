// C# / src/ZyonMenu.Windows/LocalRuntimeTelemetry.cs / .NET 8 / observations of the existing external profile.
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZyonMenu.Windows;

internal readonly record struct DiagnosticVector(float X, float Y, float Z)
{
    public static DiagnosticVector From(Vector3 value) => new(value.X, value.Y, value.Z);
}

internal sealed class LocalPedTelemetry
{
    public DateTimeOffset? AttemptUtc { get; set; }
    public string World { get; set; } = "0x0";
    public string Ped { get; set; } = "0x0";
    public DiagnosticVector? Position { get; set; }
    public float? Health { get; set; }
    public DiagnosticVector? TransformTranslation { get; set; }
    public float? TranslationDistance { get; set; }
    public float[] BasisLengthsSquared { get; set; } = [];
    public string Failure { get; set; } = "Ainda não lido.";
}

internal sealed class ActorScanTelemetry
{
    public DateTimeOffset? AttemptUtc { get; set; }
    public string Viewport { get; set; } = "0x0";
    public string Replay { get; set; } = "0x0";
    public string PedInterface { get; set; } = "0x0";
    public string List { get; set; } = "0x0";
    public float[] ViewMatrix { get; set; } = [];
    public bool ChainRead { get; set; }
    public string Failure { get; set; } = "Ainda não lido.";
    public int SlotsSampled { get; set; }
    public int SlotReadFailures { get; set; }
    public int InvalidPointers { get; set; }
    public int LocalPedSkipped { get; set; }
    public int HealthRejected { get; set; }
    public int PositionRejected { get; set; }
    public int DistanceFiltered { get; set; }
    public int PlayerFiltered { get; set; }
    public int TransformReadFailures { get; set; }
    public int BoneReadFailures { get; set; }
    public int BoneValidationFailures { get; set; }
    public int BoneProjectionFailures { get; set; }
    public int BonesProjected { get; set; }
    public int ActorsProjected { get; set; }
    public int ActorsEligibleForEsp { get; set; }
    public bool SampleLimitReached { get; set; }
}

internal sealed class CameraTelemetry
{
    public DateTimeOffset? AttemptUtc { get; set; }
    public string Camera { get; set; } = "0x0";
    public string Target { get; set; } = "0x0";
    public string TargetBone { get; set; } = "";
    public DiagnosticVector? TargetPosition { get; set; }
    public float? TargetDistanceFromCrosshair { get; set; }
    public DiagnosticVector? Eye { get; set; }
    public DiagnosticVector? Forward { get; set; }
    public float? ForwardLength { get; set; }
    public float? TargetDeltaLengthSquared { get; set; }
    public DiagnosticVector? RequestedDirection { get; set; }
    public DiagnosticVector? PrimaryReadback { get; set; }
    public DiagnosticVector? SecondaryReadback { get; set; }
    public bool WriteAttempted { get; set; }
    public bool ReadbackMatched { get; set; }
    public string Outcome { get; set; } = "Nenhuma tentativa de aim.";
}

internal sealed class WeaponFieldTelemetry
{
    public int? Offset { get; set; }
    public string Address { get; set; } = "0x0";
    public uint? OriginalBits { get; set; }
    public uint? BeforeBits { get; set; }
    public uint? ReadbackBits { get; set; }
    public bool WriteAttempted { get; set; }
    public bool ReadbackMatched { get; set; }
    public DateTimeOffset? ObservedUtc { get; set; }
    public string Outcome { get; set; } = "Ainda não lido.";
}

internal sealed class WeaponTelemetry
{
    public DateTimeOffset? AttemptUtc { get; set; }
    public string Manager { get; set; } = "0x0";
    public string Info { get; set; } = "0x0";
    public uint? Hash { get; set; }
    public string Outcome { get; set; } = "Ainda não lido.";
    public WeaponFieldTelemetry Recoil { get; set; } = new();
    public WeaponFieldTelemetry Spread { get; set; } = new();
}

internal sealed class SignatureTelemetry
{
    public int ExecutableSectionsRead { get; set; }
    public int ExecutableSectionsSkipped { get; set; }
    public int RecoilMatchCount { get; set; }
    public int SpreadMatchCount { get; set; }
    public List<int> RecoilCandidates { get; set; } = [];
    public List<int> SpreadCandidates { get; set; } = [];
    public string RecoilReason { get; set; } = "Processo ainda não analisado.";
    public string SpreadReason { get; set; } = "Processo ainda não analisado.";
}

internal sealed class GodModeTelemetry
{
    public DateTimeOffset? AttemptUtc { get; set; }
    public uint? BeforeFlags { get; set; }
    public uint? ReadbackFlags { get; set; }
    public bool WriteAttempted { get; set; }
    public bool OwnedBitObserved { get; set; }
    public string Outcome { get; set; } = "Ainda não lido.";
}

internal static class ExternalStateSerialization
{
    private static readonly JsonSerializerOptions DiagnosticOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static string SerializeDiagnostics<T>(T value) => JsonSerializer.Serialize(value, DiagnosticOptions);
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
