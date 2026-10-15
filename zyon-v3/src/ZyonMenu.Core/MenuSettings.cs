// C# / src/ZyonMenu.Core/MenuSettings.cs / .NET 8 / Windows 10 FiveM b3258 standalone settings.
namespace ZyonMenu.Core;

public sealed class MenuSettings
{
    public AimSettings Aim { get; set; } = new();
    public AimSettings Silent { get; set; } = new();
    public WeaponSettings Weapons { get; set; } = new();
    public PlayerSettings Player { get; set; } = new();
    public EspSettings Esp { get; set; } = new();

    public void Validate()
    {
        if (Aim is null || Silent is null || Weapons is null || Player is null || Esp is null)
            throw new ArgumentException("As configurações precisam conter todas as seções.");
        Aim.Validate();
        Silent.Validate();
        if (Silent.Enabled)
            throw new ArgumentException("Silent aim não está implementado nesta integração.");
        if (Esp.MaxDistance is < 25 or > 1000)
            throw new ArgumentException("A distância do ESP deve ficar entre 25 e 1000 metros.");
        if (Player.NoclipSpeed is < 1 or > 50)
            throw new ArgumentException("A velocidade do noclip deve ficar entre 1 e 50 metros por segundo.");
    }
}

public sealed class AimSettings
{
    public bool Enabled { get; set; }
    public bool OnlyVisible { get; set; } = true;
    public bool OnlyPlayers { get; set; } = true;
    public string TargetBone { get; set; } = "Head";
    public string ActivationKey { get; set; } = "RMB";
    public int Smooth { get; set; } = 1;
    public bool DrawFov { get; set; }
    public int Fov { get; set; } = 100;

    internal void Validate()
    {
        if (TargetBone is not ("Head" or "Neck" or "Chest"))
            throw new ArgumentException("O alvo precisa ser cabeça, pescoço ou peito.");
        if (ActivationKey is not ("RMB" or "LMB" or "Alt" or "Shift" or "Ctrl"))
            throw new ArgumentException("Tecla de ativação inválida.");
        if (Smooth is < 1 or > 20 || Fov is < 10 or > 500)
            throw new ArgumentException("Suavização ou FOV fora do intervalo permitido.");
    }
}

public sealed class WeaponSettings
{
    public bool NoRecoil { get; set; }
    public bool NoSpread { get; set; }
    public bool FreezeAmmo { get; set; }
}

public sealed class PlayerSettings
{
    public bool GodMode { get; set; }
    public bool Noclip { get; set; }
    public int NoclipSpeed { get; set; } = 5;
    public bool Invisible { get; set; }
    public bool MuteVoice { get; set; }
}

public sealed class EspSettings
{
    public bool Enabled { get; set; }
    public bool Boxes { get; set; } = true;
    public bool Skeletons { get; set; }
    public bool Names { get; set; } = true;
    public bool Distance { get; set; } = true;
    public bool OnlyPlayers { get; set; } = true;
    public bool OnlyVisible { get; set; } = true;
    public int MaxDistance { get; set; } = 250;
    public bool Admins { get; set; }
}

public sealed class ServerStatus
{
    public string Version { get; set; } = "";
    public bool Ready { get; set; }
    public bool SessionActive { get; set; }
    public bool PlayerOnline { get; set; }
    public int? PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public long DesiredRevision { get; set; }
    public long AppliedRevision { get; set; }
    public string Message { get; set; } = "";
    public string[] Capabilities { get; set; } = [];
    public string MedicalProvider { get; set; } = "";
    public string MedicalMessage { get; set; } = "";
    public List<FeatureStatus> FeatureStates { get; set; } = [];
}

public sealed class FeatureStatus
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset? ObservedUtc { get; set; }
}

public sealed class MenuEvents
{
    public long Cursor { get; set; }
    public List<MenuEvent> Events { get; set; } = [];
}

public sealed class MenuEvent
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}
