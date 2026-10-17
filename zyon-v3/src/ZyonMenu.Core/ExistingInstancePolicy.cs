namespace ZyonMenu.Core;

public enum ExistingInstanceAction
{
    Activate,
    VersionConflict,
    ModeConflict,
    Unidentified
}

public enum LocalMenuMode { External, Administration, ClientModule }

public static class ExistingInstancePolicy
{
    public static bool TryParseWindowTitle(string title, out Version? version, out bool? administration)
    {
        bool parsed = TryParseModeWindowTitle(title, out version, out var mode);
        administration = mode == LocalMenuMode.Administration;
        if (!parsed || mode == LocalMenuMode.ClientModule)
        {
            version = null;
            administration = null;
            return false;
        }
        return true;
    }

    public static bool TryParseModeWindowTitle(string title, out Version? version, out LocalMenuMode? mode)
    {
        version = null;
        mode = null;
        const string prefix = "Zyon ";
        const string separator = " — ";
        if (!title.StartsWith(prefix, StringComparison.Ordinal)) return false;
        int split = title.IndexOf(separator, prefix.Length, StringComparison.Ordinal);
        if (split < 0) return false;
        var versionText = title[prefix.Length..split];
        if (!Version.TryParse(versionText, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 ||
            parsed.ToString(3) != versionText) return false;
        mode = title[(split + separator.Length)..] switch
        {
            "FiveM b3258 / standalone" => LocalMenuMode.External,
            "modo administrativo" => LocalMenuMode.Administration,
            "FiveM b3258 / cliente local" => LocalMenuMode.ClientModule,
            // Releases 3.3.x used the neutral title for the retired plugin backend.
            "FiveM b3258" => parsed >= new Version(3, 4, 0) ? LocalMenuMode.External : LocalMenuMode.ClientModule,
            _ => null
        };
        if (mode is null) return false;
        version = new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
        return true;
    }

    public static ExistingInstanceAction Decide(Version requestedVersion, Version? existingVersion,
        bool requestAdministration, bool? existingAdministration)
        => Decide(requestedVersion, existingVersion,
            requestAdministration ? LocalMenuMode.Administration : LocalMenuMode.External,
            existingAdministration is null ? null : existingAdministration.Value ? LocalMenuMode.Administration : LocalMenuMode.External);

    public static ExistingInstanceAction Decide(Version requestedVersion, Version? existingVersion,
        LocalMenuMode requestedMode, LocalMenuMode? existingMode)
    {
        ArgumentNullException.ThrowIfNull(requestedVersion);
        if (existingVersion is null || existingMode is null)
            return ExistingInstanceAction.Unidentified;
        if (requestedVersion != existingVersion)
            return ExistingInstanceAction.VersionConflict;
        return requestedMode == existingMode
            ? ExistingInstanceAction.Activate : ExistingInstanceAction.ModeConflict;
    }
}
