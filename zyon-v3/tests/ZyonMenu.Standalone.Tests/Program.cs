// C# / tests/ZyonMenu.Standalone.Tests/Program.cs / .NET 8 / portable projection, signature and UI policy contract runner.
using System.Numerics;
using ZyonMenu.Core;
using ZyonMenu.Standalone.Tests;

if (ExistingInstanceChecks.TryHandleChild(args, out var childExitCode)) return childExitCode;

var assertions = 0;
var suites = new List<(string Name, Action Body)>
{
    ("projection raw b3258 columns and perspective", ProjectionLayout),
    ("projection invalid values and off-screen geometry", ProjectionRejection),
    ("bone affine row-major transform", BoneTransform),
    ("circular FOV and fractional smoothing", AimGeometry),
    ("byte signature parser validation", PatternParser),
    ("unique, absent, ambiguous and overlapping matches", PatternMatches),
    ("standalone UI capability and lifecycle policy", () => assertions += UiPolicyChecks.Run()),
    ("overlay freshness and partial actor geometry", () => assertions += OverlayChecks.Run()),
    ("bone transform ignores unused padding without weakening projection", () => assertions += AffinePaddingChecks.Run()),
    ("startup version policy and process mutex exclusion", () => assertions += ExistingInstanceChecks.Run()),
    ("external observation readback and diagnostic snapshots", () => assertions += ExternalObservationChecks.Run())
};

var failures = 0;
foreach (var (name, body) in suites)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}
Console.WriteLine($"{suites.Count - failures}/{suites.Count} suites passed; {assertions} assertions.");
return failures == 0 ? 0 : 1;

void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

void Near(float actual, float expected, string message, float tolerance = 0.0001f) =>
    Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
        $"{message}: expected {expected}, got {actual}.");

void Throws<T>(Action action, string message) where T : Exception
{
    assertions++;
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

Matrix4x4 ProjectionFixture() => new(
    17, 0.1f, 0.4f, 0.01f,
    19, 0.2f, 0.5f, 0.02f,
    23, 0.3f, 0.6f, 0.03f,
    29, 0.7f, 0.8f, 2);

void ProjectionLayout()
{
    var matrix = ProjectionFixture();
    Check(ProjectionMath.TryProject(new Vector3(1, 2, 3), matrix, 800, 600, out var point), "Projection must succeed.");
    Near(point.X, 400 + 2.1f / 2.14f * 400, "Column-two clip X", 0.001f);
    Near(point.Y, 300 - 4f / 2.14f * 300, "Column-three clip Y", 0.001f);

    matrix.M11 = -1234;
    matrix.M21 = 765;
    matrix.M31 = -938;
    matrix.M41 = 602;
    Check(ProjectionMath.TryProject(new Vector3(1, 2, 3), matrix, 800, 600, out var changed), "Unused first column remains finite.");
    Check(point == changed, "Projection must not use the first column or a transposed convention.");

    matrix.M42 = 0;
    matrix.M43 = 0;
    Check(ProjectionMath.TryProject(Vector3.Zero, matrix, 1919, 1079, out var center), "Viewport origin should project.");
    Near(center.X, 959.5f, "Odd viewport horizontal center");
    Near(center.Y, 539.5f, "Odd viewport vertical center");

    var perspective = new Matrix4x4 { M12 = 1, M23 = 1, M34 = 1 };
    Check(ProjectionMath.TryProject(new Vector3(1, 1, 2), perspective, 100, 100, out var near), "Front-facing point should project.");
    Check(ProjectionMath.TryProject(new Vector3(1, 1, 4), perspective, 100, 100, out var far), "Farther point should project.");
    Near(near.X, 75, "Near X");
    Near(near.Y, 25, "Positive world Y moves upward");
    Near(far.X, 62.5f, "Perspective depth scales X");
    Near(far.Y, 37.5f, "Perspective depth scales Y");
}

void ProjectionRejection()
{
    var matrix = Matrix4x4.Identity;
    Check(!ProjectionMath.TryProject(Vector3.Zero, matrix, 0, 600, out var rejected) && rejected == default, "Zero width must fail and clear output.");
    Check(!ProjectionMath.TryProject(Vector3.Zero, matrix, 800, -1, out _), "Negative height must fail.");
    Check(!ProjectionMath.TryProject(new Vector3(float.NaN, 0, 0), matrix, 800, 600, out _), "NaN world point must fail.");
    Check(!ProjectionMath.TryProject(new Vector3(0, float.PositiveInfinity, 0), matrix, 800, 600, out _), "Infinite world point must fail.");
    matrix.M11 = float.NaN;
    Check(!ProjectionMath.TryProject(Vector3.Zero, matrix, 800, 600, out _), "A corrupt matrix must fail even in an unused column.");
    matrix = Matrix4x4.Identity;
    matrix.M44 = -1;
    Check(!ProjectionMath.TryProject(Vector3.Zero, matrix, 800, 600, out _), "Behind-camera W must fail.");
    matrix.M44 = 0;
    Check(!ProjectionMath.TryProject(Vector3.Zero, matrix, 800, 600, out _), "Zero W must fail.");
    matrix.M44 = 0.0009f;
    Check(!ProjectionMath.TryProject(Vector3.Zero, matrix, 800, 600, out _), "W below the near threshold must fail.");
    matrix.M44 = 0.001f;
    Check(!ProjectionMath.TryProject(Vector3.Zero, matrix, 800, 600, out _), "W exactly at the near threshold must fail.");
    matrix.M44 = 0.0011f;
    Check(ProjectionMath.TryProject(Vector3.Zero, matrix, 800, 600, out _), "W above the near threshold must succeed.");
    matrix = Matrix4x4.Identity;
    matrix.M12 = 1;
    Check(ProjectionMath.TryProject(new Vector3(3, 0, 0), matrix, 800, 600, out var outside), "Off-screen geometry remains projectable for clipping.");
    Near(outside.X, 1600, "Off-screen X is not clamped");
    matrix.M12 = float.MaxValue;
    Check(!ProjectionMath.TryProject(new Vector3(float.MaxValue, 0, 0), matrix, 800, 600, out _), "Nonrepresentable screen coordinates must fail.");
}

void BoneTransform()
{
    var ped = new Matrix4x4(
        0, 1, 0, 0,
        -1, 0, 0, 0,
        0, 0, 2, 0,
        100, 200, 300, 1);
    var point = ProjectionMath.TransformBone(new Vector3(1, 2, 3), ped);
    Near(point.X, 98, "Bone rotation X and translation");
    Near(point.Y, 201, "Bone rotation Y and translation");
    Near(point.Z, 306, "Bone scaled Z and translation");
    Check(ProjectionMath.TransformBone(Vector3.Zero, ped) == new Vector3(100, 200, 300), "Ped origin uses row-four translation.");
    Check(ProjectionMath.TransformBone(new Vector3(1, 2, 3), Matrix4x4.Identity) == new Vector3(1, 2, 3), "Identity leaves the local bone unchanged.");
    Throws<ArgumentException>(() => ProjectionMath.TransformBone(new Vector3(0, 0, float.NaN), ped), "Corrupt bone coordinates must be rejected.");
    ped.M43 = float.PositiveInfinity;
    Throws<ArgumentException>(() => ProjectionMath.TransformBone(Vector3.Zero, ped), "Corrupt bone matrix must be rejected.");
}

void AimGeometry()
{
    var center = new Vector2(10, 20);
    Check(ProjectionMath.TargetDistanceWithinFov(new Vector2(13, 24), center, 5), "A 3-4-5 boundary lies within the circular FOV.");
    Check(!ProjectionMath.TargetDistanceWithinFov(new Vector2(14, 24), center, 5), "Diagonal point outside the circle is not inside a rectangular FOV.");
    Check(ProjectionMath.TargetDistanceWithinFov(center, center, 0), "Zero FOV accepts the exact center.");
    Check(!ProjectionMath.TargetDistanceWithinFov(center, center, -1), "Negative FOV is invalid.");
    Check(!ProjectionMath.TargetDistanceWithinFov(center, center, float.NaN), "Nonfinite FOV is invalid.");
    Check(!ProjectionMath.TargetDistanceWithinFov(new Vector2(float.NaN, 0), center, 100), "Nonfinite point is invalid.");
    Check(!ProjectionMath.TargetDistanceWithinFov(center, new Vector2(0, float.PositiveInfinity), 100), "Nonfinite center is invalid.");
    Check(!ProjectionMath.TargetDistanceWithinFov(new Vector2(float.MaxValue, 0), new Vector2(-float.MaxValue, 0), float.MaxValue), "Large values must not turn an overflowed squared distance into an accepted target.");
    var raw = ProjectionMath.SmoothDelta(new Vector2(13, 16), center, 1);
    Check(raw == new Vector2(3, -4), "Smooth one preserves signed displacement.");
    var smoothed = ProjectionMath.SmoothDelta(new Vector2(13, 16), center, 20);
    Near(smoothed.X, 0.15f, "Fractional horizontal displacement is preserved");
    Near(smoothed.Y, -0.2f, "Fractional vertical displacement is preserved");
    Check(ProjectionMath.SmoothDelta(center, center, 5) == Vector2.Zero, "Already-centered target produces no motion.");
    Throws<ArgumentOutOfRangeException>(() => ProjectionMath.SmoothDelta(center, center, 0), "Smooth zero must reject division by zero.");
    Throws<ArgumentOutOfRangeException>(() => ProjectionMath.SmoothDelta(center, center, 21), "Smooth outside settings range must fail.");
    Throws<ArgumentException>(() => ProjectionMath.SmoothDelta(new Vector2(float.NaN, 0), center, 1), "Nonfinite target must fail.");
    Throws<ArgumentException>(() => ProjectionMath.SmoothDelta(new Vector2(float.MaxValue, 0), new Vector2(-float.MaxValue, 0), 1), "An unrepresentable delta must fail rather than return infinity.");
}

void PatternParser()
{
    var pattern = BytePattern.Parse("  48\t8b\n?? ff  ");
    Check(pattern.Length == 4, "Mixed whitespace and letter case must parse four bytes.");
    Check(pattern.FindUnique(new byte[] { 0x48, 0x8B, 0, 0xFF }) == 0, "A whole-byte wildcard matches zero.");
    Check(pattern.FindUnique(new byte[] { 0x48, 0x8B, 0xAA, 0xFF }) == 0, "A whole-byte wildcard matches a nonzero byte.");
    Throws<ArgumentNullException>(() => BytePattern.Parse(null!), "Null patterns must fail.");
    foreach (var invalid in new[] { "", " \r\n ", "4", "488B", "GG", "?", "?A", "A?", "0x48", "48,8B", "48 **" })
        Throws<FormatException>(() => BytePattern.Parse(invalid), $"Invalid pattern '{invalid}' must fail.");
}

void PatternMatches()
{
    var exact = BytePattern.Parse("12 34 56");
    Check(exact.FindUnique(new byte[] { 0x12, 0x34, 0x56, 0 }) == 0, "Start-boundary match must resolve to offset zero.");
    Check(exact.FindUnique(new byte[] { 0, 0x12, 0x34, 0x56 }) == 1, "End-boundary match must be scanned.");
    Throws<InvalidOperationException>(() => exact.FindUnique(new byte[] { 0x12, 0x34 }), "Data shorter than the pattern cannot match.");
    Throws<InvalidOperationException>(() => exact.FindUnique(Array.Empty<byte>()), "An empty source cannot match a nonempty pattern.");
    Throws<InvalidOperationException>(() => exact.FindUnique(new byte[] { 0x12, 0x34, 0 }), "Missing pattern must fail.");
    Throws<InvalidOperationException>(() => exact.FindUnique(new byte[] { 0x12, 0x34, 0x56, 0, 0x12, 0x34, 0x56 }), "An ambiguous pattern cannot resolve to its first match.");
    var overlaps = BytePattern.Parse("AA AA");
    var all = overlaps.FindAll(new byte[] { 0xAA, 0xAA, 0xAA });
    Check(all.SequenceEqual(new[] { 0, 1 }), "Overlapping matches must both be found.");
    Throws<InvalidOperationException>(() => overlaps.FindUnique(new byte[] { 0xAA, 0xAA, 0xAA }), "Overlapping ambiguity must fail.");
    Check(exact.FindAll(new byte[] { 0x12 }).Length == 0, "FindAll returns no matches when the source is too short.");
    var wildcard = BytePattern.Parse("??");
    Check(wildcard.FindUnique(new byte[] { 0xFF }) == 0, "A single wildcard on one byte has a unique match.");
    Throws<InvalidOperationException>(() => wildcard.FindUnique(new byte[] { 0, 1 }), "All-wildcard ambiguity must fail.");
}
