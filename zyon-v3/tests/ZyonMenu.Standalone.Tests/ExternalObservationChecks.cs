// C# / tests/ZyonMenu.Standalone.Tests/ExternalObservationChecks.cs / .NET 8 / external readback and diagnostic safety.
using System.Numerics;
using System.Text.Json;
using ZyonMenu.Core;
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class ExternalObservationChecks
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        var requested = Vector3.Normalize(new Vector3(1, 2, 3));
        Check(ExternalReadback.DirectionMatches(requested, requested), "Exact camera readback must match.");
        Check(ExternalReadback.DirectionMatches(requested, requested + new Vector3(0.00001f, -0.00001f, 0)),
            "A small floating point readback difference must stay within the declared tolerance.");
        Check(!ExternalReadback.DirectionMatches(requested, requested + new Vector3(0, 0.001f, 0)),
            "A camera direction overwritten by the game must not count as observed.");
        Check(!ExternalReadback.DirectionMatches(requested, -requested), "An opposite camera direction must fail readback.");
        Check(!ExternalReadback.DirectionMatches(requested, new Vector3(float.NaN, 0, 0)), "NaN must never validate a camera readback.");
        Check(!ExternalReadback.DirectionMatches(new Vector3(float.PositiveInfinity, 0, 0), requested),
            "A nonfinite requested vector must not validate a readback.");
        Check(!ExternalReadback.DirectionMatches(requested, requested, -1), "A negative tolerance must not validate a readback.");
        Check(!ExternalReadback.DirectionMatches(requested, requested, float.NaN), "A nonfinite tolerance must not validate a readback.");
        Check(ExternalReadback.UniqueFieldOffset([]) is null, "A missing executable signature must not invent a field offset.");
        Check(ExternalReadback.UniqueFieldOffset([0x80]) == 0x80, "A unique bounded signature operand must resolve.");
        Check(ExternalReadback.UniqueFieldOffset([0x80, 0x80]) is null, "Even equal operands from ambiguous matches must disable a field.");
        Check(ExternalReadback.UniqueFieldOffset([0x80, 0x90]) is null, "Different ambiguous operands must disable a field.");
        Check(ExternalReadback.UniqueFieldOffset([0x1F]) is null && ExternalReadback.UniqueFieldOffset([0x1001]) is null,
            "Out-of-profile operands must not become writable field offsets.");
        Check(ExternalReadback.SignedDisplacement8(0x7F) == 127, "A positive disp8 must preserve its signed operand value.");
        Check(ExternalReadback.SignedDisplacement8(0x80) == -128, "An 0x80 disp8 must decode as minus 128, not plus 128.");
        Check(ExternalReadback.SignedDisplacement8(0xFF) == -1, "An 0xFF disp8 must decode as minus one.");
        Check(ExternalReadback.UniqueFieldOffset([ExternalReadback.SignedDisplacement8(0x80)]) is null,
            "A negative decoded displacement must not enable a positive writable field address.");

        var projection = Matrix4x4.Identity;
        Check(ExternalReadback.ProjectionCoefficientsValid(projection), "A finite projection matrix with a nonzero W plane must pass.");
        projection.M11 = float.NaN;
        projection.M21 = float.PositiveInfinity;
        projection.M31 = float.NegativeInfinity;
        projection.M41 = float.NaN;
        Check(!ExternalReadback.ProjectionCoefficientsValid(projection), "A nonfinite viewport matrix must be blocked consistently with the projection contract.");
        projection = Matrix4x4.Identity;
        projection.M12 = float.NaN;
        Check(!ExternalReadback.ProjectionCoefficientsValid(projection), "A nonfinite coefficient actually used by projection must block actor scanning.");
        Check(!ExternalReadback.ProjectionCoefficientsValid(default), "A zero W plane must report an invalid matrix instead of silently reporting no actors.");

        var camera = new CameraTelemetry { Forward = new DiagnosticVector(float.NaN, float.PositiveInfinity, float.NegativeInfinity) };
        var actors = new ActorScanTelemetry { ViewMatrix = [1, float.NaN, float.PositiveInfinity] };
        string diagnostic = ExternalStateSerialization.SerializeDiagnostics(new { Camera = camera, Actors = actors });
        using var json = JsonDocument.Parse(diagnostic);
        var forward = json.RootElement.GetProperty("Camera").GetProperty("Forward");
        Check(forward.GetProperty("X").GetString() == "NaN" && forward.GetProperty("Y").GetString() == "Infinity" &&
            forward.GetProperty("Z").GetString() == "-Infinity", "Invalid camera observations must still produce valid diagnostic JSON.");
        Check(json.RootElement.GetProperty("Actors").GetProperty("ViewMatrix")[1].GetString() == "NaN",
            "A failed projection matrix must remain inspectable instead of breaking diagnostic capture.");

        var observedUtc = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        var status = new ServerStatus
        {
            DesiredRevision = 2, AppliedRevision = 1,
            Capabilities = ["aim"],
            FeatureStates = [new FeatureStatus { Name = "aim", State = "observed", Message = "readback", ObservedUtc = observedUtc }]
        };
        var snapshot = ExternalStateSerialization.Clone(status);
        Check(!ReferenceEquals(snapshot.FeatureStates, status.FeatureStates) && !ReferenceEquals(snapshot.FeatureStates[0], status.FeatureStates[0]),
            "A returned status must not expose mutable backend feature objects.");
        snapshot.FeatureStates[0].State = "blocked";
        snapshot.Capabilities[0] = "changed";
        Check(status.FeatureStates[0].State == "observed" && status.Capabilities[0] == "aim",
            "Editing a caller's snapshot must not change backend states or capabilities.");
        Check(snapshot.FeatureStates[0].ObservedUtc == observedUtc && snapshot.DesiredRevision == 2 && snapshot.AppliedRevision == 1,
            "Snapshot cloning must preserve observation time and pending configuration revisions.");
        return assertions;
    }
}
