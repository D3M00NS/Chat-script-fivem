namespace ZyonMenu.Core;

public enum ExistingInstanceAction
{
    Activate,
    VersionConflict,
    ModeConflict,
    Unidentified
}

public static class ExistingInstancePolicy
{
    public static bool TryParseWindowTitle(string title, out Version? version, out bool? administration)
    {
        version = null;
        administration = null;
        const string prefix = "Zyon ";
        const string separator = " — ";
        if (!title.StartsWith(prefix, StringComparison.Ordinal)) return false;
        int split = title.IndexOf(separator, prefix.Length, StringComparison.Ordinal);
        if (split < 0) return false;
        var versionText = title[prefix.Length..split];
        if (!Version.TryParse(versionText, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 ||
            parsed.ToString(3) != versionText) return false;
        administration = title[(split + separator.Length)..] switch
        {
            "FiveM b3258 / standalone" => false,
            "modo administrativo" => true,
            _ => null
        };
        if (administration is null) return false;
        version = new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
        return true;
    }

    public static ExistingInstanceAction Decide(Version requestedVersion, Version? existingVersion,
        bool requestAdministration, bool? existingAdministration)
    {
        ArgumentNullException.ThrowIfNull(requestedVersion);
        if (existingVersion is null || existingAdministration is null)
            return ExistingInstanceAction.Unidentified;
        if (requestedVersion != existingVersion)
            return ExistingInstanceAction.VersionConflict;
        return requestAdministration == existingAdministration
            ? ExistingInstanceAction.Activate : ExistingInstanceAction.ModeConflict;
    }
}
