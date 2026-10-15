// Authorized FiveM server administration through the resource HTTP API and local pairing.
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal sealed class AdminMainForm : Form
{
    private readonly MenuSettings _settings = new();
    private readonly Dictionary<string, Control> _pages = new();
    private readonly Dictionary<string, Button> _navigation = new();
    private readonly Dictionary<string, List<Control>> _capabilityControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(ToggleSwitch Control, Func<bool> Getter)> _toggleBindings = new();
    private readonly ToolTip _tooltips = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 60 };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1600 };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(24, 16, 12, 12) };
    private readonly Label _connectionLabel = Theme.Label("DESCONECTADO", true);
    private readonly Label _hotkeyLabel = Theme.Label("Insert · ocultar / mostrar", true);
    private readonly Label _revisionLabel = Theme.Label("Aguardando detecção automática do FiveM.", true);
    private readonly Label _serverLabel = Theme.Label("", true);
    private readonly Label _connectionDetails = Theme.Label("Abra o FiveM e entre na cidade com o recurso zyon_admin_menu atualizado.", true);
    private readonly TextBox _endpoint = Theme.TextBox();
    private readonly TextBox _eventLog = new();
    private readonly Button _connect = Theme.Button("Detectar FiveM", true);
    private readonly Button _disconnect = Theme.Button("Desconectar");
    private readonly Button _revive = Theme.Button("Reviver personagem", true);
    private readonly Button _suicide = Theme.Button("Suicídio");
    private readonly LocalPairingService _pairing = new();
    private readonly CancellationTokenSource _lifetime = new();
    private MenuApiClient? _client;
    private CancellationTokenSource? _session;
    private ServerStatus? _lastStatus;
    private bool _healthy;
    private bool _connecting;
    private bool _polling;
    private bool _sending;
    private bool _disconnecting;
    private bool _acting;
    private bool _closing;
    private bool _dirty = true;
    private bool _synchronizing;
    private bool _hotkeyRegistered;
    private bool _pairingStarted;
    private bool _pairingStarting;
    private bool _handlingPairing;
    private PairingTicket? _pendingTicket;
    private string _boundTicketIdentity = "";
    private string _lastPairingError = "";
    private long _changeVersion;
    private long _connectionGeneration;
    private long _eventsCursor;
    private string _pendingActionType = "";
    private long _pendingActionSince;
    private long _pendingActionCursor;

    public AdminMainForm()
    {
        Text = "Zyon v3 — modo administrativo";
        Size = new Size(1100, 790);
        MinimumSize = new Size(950, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        BuildShell();
        BuildAimPage();
        BuildPlayerPage();
        BuildNoclipPage();
        BuildEspPage();
        BuildWeaponsPage();
        BuildConnectionPage();
        ShowPage("Player");

        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SendSettingsAsync(); };
        _poll.Tick += async (_, _) => await PollAsync();
        _connect.Click += async (_, _) => await StartDetectionAsync();
        _disconnect.Click += async (_, _) => await DisconnectAsync();
        _revive.Click += async (_, _) => await ReviveAsync();
        _suicide.Click += async (_, _) => await SuicideAsync();
        FormClosing += OnFormClosing;
        Shown += async (_, _) => await StartDetectionAsync();
        _pairing.TicketReceived += OnPairingTicketReceived;
        UpdateAvailability();
    }

    private void BuildShell()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 86, BackColor = Theme.Sidebar, Padding = new Padding(10, 18, 10, 12) };
        var mark = new Label
        {
            Text = "Z",
            Font = new Font("Segoe UI", 27, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = Theme.Sidebar,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 64
        };
        var links = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 22, 0, 0),
            BackColor = Theme.Sidebar
        };
        foreach (var (name, icon, caption) in new[]
                 {
                     ("Aim", "◎", "Aim"),
                     ("Player", "♟", "Personagem"),
                     ("Noclip", "↟", "Noclip / voar"),
                     ("ESP", "◉", "ESP"),
                     ("Weapons", "⌁", "Armas"),
                     ("Connection", "⚙", "Conexão")
                 })
        {
            var button = Theme.Button(icon);
            button.Width = 64;
            button.Height = 58;
            button.Margin = new Padding(0, 0, 0, 12);
            button.Font = new Font("Segoe UI Symbol", 24f);
            button.BackColor = Theme.Sidebar;
            button.FlatAppearance.BorderSize = 0;
            button.AccessibleName = caption;
            button.Click += (_, _) => ShowPage(name);
            _tooltips.SetToolTip(button, caption);
            _navigation.Add(name, button);
            links.Controls.Add(button);
        }
        sidebar.Controls.Add(links);
        sidebar.Controls.Add(mark);

        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var header = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(26, 15, 26, 8), BackColor = Theme.Background };
        var title = Theme.Label("ZYON  /  MODO ADMINISTRATIVO");
        title.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        title.Location = new Point(26, 16);
        var subtitle = Theme.Label("FiveM · controle do recurso instalado no seu servidor", true);
        subtitle.Location = new Point(27, 45);
        _connectionLabel.AutoSize = false;
        _connectionLabel.Size = new Size(290, 24);
        _connectionLabel.TextAlign = ContentAlignment.MiddleRight;
        _connectionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _connectionLabel.Location = new Point(header.Width - 316, 17);
        _hotkeyLabel.AutoSize = false;
        _hotkeyLabel.Size = new Size(290, 24);
        _hotkeyLabel.TextAlign = ContentAlignment.MiddleRight;
        _hotkeyLabel.Font = new Font("Segoe UI", 8.5f);
        _hotkeyLabel.Location = new Point(header.Width - 316, 44);
        _hotkeyLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Resize += (_, _) =>
        {
            _connectionLabel.Location = new Point(header.Width - 316, 17);
            _hotkeyLabel.Location = new Point(header.Width - 316, 44);
        };
        _tooltips.SetToolTip(_hotkeyLabel, "Insert oculta ou mostra o menu. A sessão permanece conectada enquanto a janela está oculta.");
        header.Controls.AddRange(new Control[] { title, subtitle, _connectionLabel, _hotkeyLabel });

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(26, 5, 24, 5), BackColor = Theme.Sidebar };
        _revisionLabel.Location = new Point(26, 8);
        _serverLabel.Location = new Point(26, 29);
        _serverLabel.Font = new Font("Segoe UI", 8f);
        footer.Controls.AddRange(new Control[] { _revisionLabel, _serverLabel });
        main.Controls.Add(_content);
        main.Controls.Add(footer);
        main.Controls.Add(header);
        Controls.Add(main);
        Controls.Add(sidebar);
    }

    private FlowLayoutPanel NewPage(string name)
    {
        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = Theme.Background,
            Visible = false,
            Margin = Padding.Empty
        };
        _pages[name] = page;
        _content.Controls.Add(page);
        return page;
    }

    private static RoundedCard Card(string title, int width = 408, int height = 440)
    {
        var card = new RoundedCard { Width = width, Height = height };
        var heading = Theme.Label(title);
        heading.ForeColor = Theme.Accent;
        heading.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        heading.Location = new Point(20, 18);
        card.Controls.Add(heading);
        return card;
    }

    private void BuildAimPage()
    {
        var page = NewPage("Aim");
        page.Controls.Add(AimCard("Aimbot Aim", _settings.Aim, "aim"));
        var silentCard = AimCard("Silent Aim", _settings.Silent, "silent");
        silentCard.Height = 485;
        AddNote(silentCard, "Redirecionamento de projéteis não implementado nesta integração.", 424, 360);
        page.Controls.Add(silentCard);
        page.Controls.Add(WeaponsCard(408));
        var note = Card("Aplicação", 408, 186);
        AddNote(note, "As opções são enviadas ao recurso autorizado do servidor. A barra inferior informa quando o cliente FiveM confirmou a revisão. Sem o recurso ativo, nenhuma função é aplicada.", 61, 360);
        page.Controls.Add(note);
    }

    private RoundedCard AimCard(string title, AimSettings settings, string capability)
    {
        var card = Card(title);
        var y = 60;
        AddToggle(card, "Ativar " + title, () => settings.Enabled, v => settings.Enabled = v, capability, y); y += 40;
        AddToggle(card, "Somente visível", () => settings.OnlyVisible, v => settings.OnlyVisible = v, capability == "aim" ? "aim.visibility" : capability, y); y += 40;
        AddChoice(card, "Alvo", new[] { "Head", "Neck", "Chest" }, new[] { "Cabeça", "Pescoço", "Peito" }, settings.TargetBone, v => settings.TargetBone = v, capability, y); y += 40;
        AddToggle(card, "Apenas jogadores", () => settings.OnlyPlayers, v => settings.OnlyPlayers = v, capability == "aim" ? "aim.playersOnly" : capability, y); y += 40;
        AddChoice(card, "Tecla de ativação", new[] { "RMB", "LMB", "Alt", "Shift", "Ctrl" }, new[] { "Mouse [Mirar]", "Mouse [Atirar]", "Alt", "Shift", "Ctrl" }, settings.ActivationKey, v => settings.ActivationKey = v, capability, y); y += 40;
        AddSlider(card, "Suavização", 1, 20, settings.Smooth, v => settings.Smooth = v, capability, y); y += 40;
        AddToggle(card, "Desenhar FOV", () => settings.DrawFov, v => settings.DrawFov = v, capability, y); y += 40;
        AddSlider(card, "FOV", 10, 500, settings.Fov, v => settings.Fov = v, capability, y);
        return card;
    }

    private void BuildPlayerPage()
    {
        var page = NewPage("Player");
        var card = Card("Personagem", 408, 344);
        AddToggle(card, "GodMode", () => _settings.Player.GodMode, v => _settings.Player.GodMode = v, "player.godMode", 64);
        AddToggle(card, "Invisibilidade", () => _settings.Player.Invisible, v => _settings.Player.Invisible = v, "player.invisible", 106);
        AddToggle(card, "Silenciar voz recebida", () => _settings.Player.MuteVoice, v => _settings.Player.MuteVoice = v, "player.muteVoice", 148);
        _revive.Text = "Reviver personagem";
        _tooltips.SetToolTip(_revive, "Usa o sistema médico selecionado pelo servidor: standalone, ESX ou QBCore.");
        _revive.Location = new Point(20, 208);
        _revive.Width = 220;
        _suicide.Location = new Point(20, 260);
        _suicide.Width = 220;
        _tooltips.SetToolTip(_suicide, "Encerra a vida do seu personagem pelo recurso administrativo. GodMode e noclip são desativados para esta ação.");
        card.Controls.AddRange(new Control[] { _revive, _suicide });
        RegisterCapability("player.revive", _revive);
        RegisterCapability("player.suicide", _suicide);
        page.Controls.Add(card);
        var note = Card("Estado do personagem", 408, 344);
        AddNote(note, "Reviver usa o fluxo médico do standalone, ESX ou QBCore selecionado pelo servidor e aguarda confirmação. Hospitais personalizados podem precisar de adaptação.\n\nSilenciar voz recebida reduz localmente a voz dos outros jogadores; seu microfone permanece disponível.\n\nUse a aba Noclip para voar e ajustar a velocidade. Os efeitos exigem sessão conectada e são removidos ao desconectar ou expirar.", 62, 360);
        page.Controls.Add(note);
    }

    private void BuildNoclipPage()
    {
        var page = NewPage("Noclip");
        var card = Card("Noclip / voar", 408, 248);
        AddToggle(card, "Ativar noclip", () => _settings.Player.Noclip, v => _settings.Player.Noclip = v, "player.noclip", 64);
        AddSlider(card, "Velocidade (m/s)", 1, 50, _settings.Player.NoclipSpeed, v => _settings.Player.NoclipSpeed = v, "player.noclip", 112);
        AddNote(card, "A velocidade muda durante o voo. O recurso usa o tempo de cada quadro para manter o movimento constante.", 168, 360);
        page.Controls.Add(card);
        var controls = Card("Controles no FiveM", 408, 248);
        AddNote(controls, "W / A / S / D · mover conforme a câmera\nEspaço · subir\nCtrl · descer\nShift · acelerar enquanto pressionado\n\nInsert · ocultar o menu e voltar ao jogo. Desative o noclip antes de sair do servidor.", 62, 360);
        page.Controls.Add(controls);
    }

    private void BuildEspPage()
    {
        var page = NewPage("ESP");
        var card = Card("ESP", 408, 475);
        var y = 62;
        AddToggle(card, "Ativar ESP", () => _settings.Esp.Enabled, v => _settings.Esp.Enabled = v, "esp", y); y += 40;
        AddToggle(card, "Caixas", () => _settings.Esp.Boxes, v => _settings.Esp.Boxes = v, "esp.boxes", y); y += 40;
        AddToggle(card, "Esqueleto", () => _settings.Esp.Skeletons, v => _settings.Esp.Skeletons = v, "esp.skeletons", y); y += 40;
        AddToggle(card, "Nomes", () => _settings.Esp.Names, v => _settings.Esp.Names = v, "esp.names", y); y += 40;
        AddToggle(card, "Distância", () => _settings.Esp.Distance, v => _settings.Esp.Distance = v, "esp.distance", y); y += 40;
        AddToggle(card, "Apenas jogadores", () => _settings.Esp.OnlyPlayers, v => _settings.Esp.OnlyPlayers = v, "esp.playersOnly", y); y += 40;
        AddToggle(card, "Somente visível", () => _settings.Esp.OnlyVisible, v => _settings.Esp.OnlyVisible = v, "esp.visibility", y); y += 40;
        AddToggle(card, "Mostrar ADMs próximos", () => _settings.Esp.Admins, v => _settings.Esp.Admins = v, "esp.admins", y); y += 40;
        AddSlider(card, "Alcance (m)", 25, 1000, _settings.Esp.MaxDistance, v => _settings.Esp.MaxDistance = v, "esp", y);
        page.Controls.Add(card);
        var note = Card("Renderização em tempo real", 408, 270);
        AddNote(note, "O recurso FiveM desenha o ESP a cada quadro, acompanhando o personagem e a câmera. O menu envia somente as configurações.\n\nOs ADMs são identificados pela permissão ACE do servidor; aparecem somente quando estão ao alcance do cliente. O ajuste de velocidade do noclip não interrompe a renderização.", 62, 360);
        page.Controls.Add(note);
    }

    private void BuildWeaponsPage()
    {
        var page = NewPage("Weapons");
        page.Controls.Add(WeaponsCard(408));
        var note = Card("Armas", 408, 220);
        AddNote(note, "A disponibilidade vem do recurso conectado. Opções que o servidor não suporta ficam bloqueadas. Os efeitos são confirmados na revisão aplicada exibida abaixo.", 62, 360);
        page.Controls.Add(note);
    }

    private RoundedCard WeaponsCard(int width)
    {
        var card = Card("Weapons Settings", width, 212);
        AddToggle(card, "No Recoil", () => _settings.Weapons.NoRecoil, v => _settings.Weapons.NoRecoil = v, "weapons.noRecoil", 64);
        AddToggle(card, "No Spread", () => _settings.Weapons.NoSpread, v => _settings.Weapons.NoSpread = v, "weapons.noSpread", 106);
        AddToggle(card, "Freeze Ammo", () => _settings.Weapons.FreezeAmmo, v => _settings.Weapons.FreezeAmmo = v, "weapons.freezeAmmo", 148);
        return card;
    }

    private void BuildConnectionPage()
    {
        var page = NewPage("Connection");
        var card = Card("Detecção automática do FiveM", 834, 315);
        var endpointLabel = Theme.Label("Cidade detectada");
        endpointLabel.Location = new Point(20, 60);
        _endpoint.Dock = DockStyle.None;
        _endpoint.Location = new Point(20, 87);
        _endpoint.Size = new Size(790, 30);
        _endpoint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _endpoint.Text = "Aguardando FiveM…";
        _endpoint.ReadOnly = true;
        _endpoint.AccessibleName = "Cidade detectada automaticamente";
        var note = Theme.Label("Abra o FiveM e entre na cidade com o recurso zyon_admin_menu atualizado.", true);
        note.Location = new Point(20, 137);
        note.AutoSize = false;
        note.Size = new Size(790, 42);
        note.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _connect.Location = new Point(20, 187);
        _disconnect.Location = new Point(180, 187);
        _connectionDetails.AutoSize = false;
        _connectionDetails.Location = new Point(20, 245);
        _connectionDetails.Size = new Size(790, 55);
        _connectionDetails.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.AddRange(new Control[] { endpointLabel, _endpoint, note, _connect, _disconnect, _connectionDetails });
        page.Controls.Add(card);

        var events = Card("Eventos e diagnóstico", 834, 263);
        _eventLog.Multiline = true;
        _eventLog.ReadOnly = true;
        _eventLog.BorderStyle = BorderStyle.None;
        _eventLog.BackColor = Theme.Card;
        _eventLog.ForeColor = Theme.Muted;
        _eventLog.Font = new Font("Consolas", 9f);
        _eventLog.ScrollBars = ScrollBars.Vertical;
        _eventLog.Location = new Point(20, 57);
        _eventLog.Size = new Size(790, 185);
        _eventLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        events.Controls.Add(_eventLog);
        page.Controls.Add(events);
        page.SizeChanged += (_, _) =>
        {
            var width = Math.Max(430, Math.Min(834, page.ClientSize.Width - 24));
            card.Width = width;
            events.Width = width;
        };
    }

    private static void AddNote(Control card, string text, int top, int width)
    {
        var label = Theme.Label(text, true);
        label.AutoSize = false;
        label.Location = new Point(20, top);
        label.Size = new Size(width, card.Height - top - 15);
        card.Controls.Add(label);
    }

    private void AddToggle(Control card, string caption, Func<bool> getter, Action<bool> setter, string capability, int y)
    {
        var label = Theme.Label(caption);
        label.Location = new Point(20, y + 3);
        var toggle = new ToggleSwitch { Checked = getter(), Location = new Point(card.Width - 66, y), AccessibleName = caption };
        toggle.CheckedChanged += (_, _) =>
        {
            if (_synchronizing) return;
            setter(toggle.Checked);
            SettingsChanged();
        };
        _toggleBindings.Add((toggle, getter));
        if (capability == "silent")
            _tooltips.SetToolTip(toggle, "Redirecionamento de projéteis não implementado nesta integração.");
        if (capability == "weapons.noRecoil")
            _tooltips.SetToolTip(toggle, "Recuo nativo da arma; scripts da cidade podem controlar a câmera separadamente.");
        if (capability == "player.godMode")
            _tooltips.SetToolTip(toggle, "Imunidade a dano nativo; scripts que alteram saúde ou estado de morte do framework podem prevalecer.");
        card.Controls.AddRange(new Control[] { label, toggle });
        RegisterCapability(capability, toggle);
    }

    private void AddChoice(Control card, string caption, string[] values, string[] labels, string value, Action<string> setter, string capability, int y)
    {
        var label = Theme.Label(caption);
        label.Location = new Point(20, y + 4);
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9f),
            Size = new Size(168, 26),
            Location = new Point(card.Width - 192, y),
            AccessibleName = caption
        };
        combo.Items.AddRange(labels.Cast<object>().ToArray());
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, value));
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_synchronizing || combo.SelectedIndex < 0) return;
            setter(values[combo.SelectedIndex]);
            SettingsChanged();
        };
        card.Controls.AddRange(new Control[] { label, combo });
        RegisterCapability(capability, combo);
    }

    private void AddSlider(Control card, string caption, int min, int max, int value, Action<int> setter, string capability, int y)
    {
        var label = Theme.Label(caption);
        label.Location = new Point(20, y + 3);
        var slider = new AccentSlider { Minimum = min, Maximum = max, Value = value, Location = new Point(card.Width - 211, y), Width = 133, AccessibleName = caption };
        var number = Theme.Label(value.ToString());
        number.AutoSize = false;
        number.Size = new Size(50, 24);
        number.TextAlign = ContentAlignment.MiddleCenter;
        number.BackColor = Theme.Field;
        number.Location = new Point(card.Width - 72, y + 1);
        slider.ValueChanged += (_, _) =>
        {
            number.Text = slider.Value.ToString();
            if (_synchronizing) return;
            setter(slider.Value);
            SettingsChanged();
        };
        card.Controls.AddRange(new Control[] { label, slider, number });
        RegisterCapability(capability, slider);
    }

    private void RegisterCapability(string capability, Control control)
    {
        if (!_capabilityControls.TryGetValue(capability, out var controls))
            _capabilityControls[capability] = controls = new List<Control>();
        controls.Add(control);
    }

    private void ShowPage(string name)
    {
        foreach (var (key, page) in _pages) page.Visible = key == name;
        foreach (var (key, button) in _navigation)
        {
            button.BackColor = key == name ? Theme.Field : Theme.Sidebar;
            button.ForeColor = key == name ? Theme.Accent : Theme.Muted;
        }
    }

    private void SettingsChanged()
    {
        SynchronizeToggles();
        _dirty = true;
        _changeVersion++;
        if (_healthy && !_disconnecting && _lastStatus is { Ready: true, PlayerOnline: true })
        {
            _revisionLabel.Text = "Alterações aguardando envio…";
            // Keep the current tick deadline while dragging; send the newest snapshot.
            if (!_debounce.Enabled) _debounce.Start();
        }
    }

    private void SynchronizeToggles()
    {
        _synchronizing = true;
        try
        {
            foreach (var binding in _toggleBindings) binding.Control.Checked = binding.Getter();
        }
        finally { _synchronizing = false; }
    }

    private async Task StartDetectionAsync()
    {
        if (_closing || _pairingStarting) return;
        _pairing.Enabled = true;
        if (_client is null)
        {
            _connectionLabel.Text = "AGUARDANDO FIVEM";
            _connectionLabel.ForeColor = Theme.Warning;
            _connectionDetails.Text = "Abra o FiveM e entre na cidade com o recurso zyon_admin_menu atualizado.";
            _revisionLabel.Text = "Detecção automática ativa. Aguardando a cidade…";
        }
        if (_pairingStarted)
        {
            UpdateAvailability();
            return;
        }

        _pairingStarting = true;
        UpdateAvailability();
        try
        {
            await _pairing.StartAsync(_lifetime.Token);
            _pairingStarted = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_closing) return;
            _pairing.Enabled = false;
            _connectionLabel.Text = "DETECÇÃO INDISPONÍVEL";
            _connectionLabel.ForeColor = Theme.Warning;
            _connectionDetails.Text = "Não foi possível iniciar a detecção local: " + error.Message;
            AppendLog(_connectionDetails.Text);
        }
        finally
        {
            _pairingStarting = false;
            if (!_closing) UpdateAvailability();
        }
    }

    private void OnPairingTicketReceived(object? sender, PairingTicket ticket)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(() => _ = QueuePairingTicketAsync(ticket))); }
        catch (InvalidOperationException) { }
    }

    private async Task QueuePairingTicketAsync(PairingTicket ticket)
    {
        if (_closing || !_pairing.Enabled) return;
        _pendingTicket = ticket;
        if (_handlingPairing) return;
        _handlingPairing = true;
        try
        {
            while (_pendingTicket is not null && !_closing && _pairing.Enabled)
            {
                var pending = _pendingTicket;
                _pendingTicket = null;
                await BindPairingTicketAsync(pending);
            }
        }
        finally { _handlingPairing = false; }
    }

    private async Task BindPairingTicketAsync(PairingTicket ticket)
    {
        if (_closing || !_pairing.Enabled) return;
        if (!string.IsNullOrWhiteSpace(ticket.Error))
        {
            if (_client is not null) await DisconnectAsync(stopDetection: false);
            if (_closing || !_pairing.Enabled) return;
            var detail = ticket.Error;
            if (!string.IsNullOrEmpty(ticket.Token)) detail = detail.Replace(ticket.Token, "[omitido]", StringComparison.Ordinal);
            _connectionLabel.Text = "AGUARDANDO FIVEM";
            _connectionLabel.ForeColor = Theme.Warning;
            _connectionDetails.Text = detail;
            _revisionLabel.Text = "Opções locais desativadas. Efeitos anteriores podem persistir até 15 s; aguardando a cidade.";
            if (_lastPairingError != detail)
            {
                AppendLog(detail);
                _lastPairingError = detail;
            }
            return;
        }

        Uri endpoint;
        string identity;
        try
        {
            endpoint = MenuApiClient.ValidateBaseUri(ticket.Endpoint);
            identity = endpoint.AbsoluteUri + "\n" + ticket.PlayerId + "\n" + ticket.Version + "\n" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket.Token)));
        }
        catch (ArgumentException)
        {
            _connectionDetails.Text = "A cidade enviou informações de conexão inválidas. Atualize o recurso zyon_admin_menu.";
            return;
        }
        if (_client is not null && _boundTicketIdentity == identity) return;
        if (_client is not null) await DisconnectAsync(stopDetection: false);
        if (_closing || !_pairing.Enabled) return;

        MenuApiClient candidate;
        try { candidate = new MenuApiClient(endpoint, ticket.Token); }
        catch (ArgumentException)
        {
            _connectionDetails.Text = "O recurso da cidade enviou uma sessão inválida. Atualize o recurso zyon_admin_menu.";
            return;
        }

        ResetActiveEffects();
        _connecting = true;
        _healthy = false;
        _lastStatus = null;
        _client = candidate;
        _boundTicketIdentity = identity;
        _lastPairingError = "";
        _endpoint.Text = endpoint.AbsoluteUri;
        _session = new CancellationTokenSource();
        var generation = ++_connectionGeneration;
        var cancellation = _session.Token;
        _connectionLabel.Text = "FIVEM DETECTADO";
        _connectionLabel.ForeColor = Theme.Warning;
        _connectionDetails.Text = "Conectando automaticamente ao recurso da cidade…";
        UpdateAvailability();

        try
        {
            var status = await candidate.GetStatusAsync(cancellation);
            if (generation != _connectionGeneration || _closing || !_pairing.Enabled) return;
            _connecting = false;
            _healthy = true;
            _lastStatus = status;
            _eventsCursor = 0;
            _dirty = true;
            _poll.Start();
            ApplyStatus(status);
            AppendLog("Conexão automática estabelecida com o recurso da cidade.");
            await SendSettingsAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation != _connectionGeneration || _closing) return;
            AppendLog("Conexão automática falhou: " + error.Message);
            ClearSession();
            _connectionDetails.Text = error.Message;
        }
        finally
        {
            if (generation == _connectionGeneration && !_closing)
            {
                _connecting = false;
                UpdateAvailability();
            }
        }
    }

    private async Task SendSettingsAsync()
    {
        if (_sending || _acting || _disconnecting || !_dirty || !_healthy || _lastStatus is not { Ready: true, PlayerOnline: true } || _client is null || _session is null) return;
        var client = _client;
        var cancellation = _session.Token;
        var generation = _connectionGeneration;
        var changeVersion = _changeVersion;
        var snapshot = JsonSerializer.Deserialize<MenuSettings>(JsonSerializer.Serialize(_settings))!;
        _sending = true;
        _revisionLabel.Text = "Enviando configurações…";
        try
        {
            await client.PushSettingsAsync(snapshot, cancellation);
            if (generation != _connectionGeneration || _closing) return;
            _dirty = changeVersion != _changeVersion;
            _revisionLabel.Text = _dirty ? "Novas alterações aguardando envio…" : "Configurações enviadas; aguardando confirmação do FiveM…";
            if (_dirty) _debounce.Start();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation != _connectionGeneration || _closing) return;
            _healthy = false;
            ResetActiveEffects();
            _connectionLabel.Text = "ERRO DE CONEXÃO";
            _connectionLabel.ForeColor = Theme.Warning;
            _connectionDetails.Text = error.Message;
            _revisionLabel.Text = "Envio não confirmado. Efeitos anteriores podem persistir até 15 s; verificando conexão…";
            AppendLog("Falha ao enviar configurações: " + error.Message);
            UpdateAvailability();
        }
        finally { if (generation == _connectionGeneration) _sending = false; }
    }

    private async Task PollAsync()
    {
        ExpirePendingAction();
        if (_polling || _connecting || _client is null || _session is null) return;
        var client = _client;
        var cancellation = _session.Token;
        var generation = _connectionGeneration;
        _polling = true;
        try
        {
            var status = await client.GetStatusAsync(cancellation);
            if (generation != _connectionGeneration || _closing) return;
            var recovered = !_healthy;
            _healthy = true;
            _lastStatus = status;
            ApplyStatus(status);
            if (recovered) AppendLog("Conexão com o recurso recuperada.");
            if (_dirty && !_debounce.Enabled && !_sending) _debounce.Start();
            var events = await client.GetEventsAsync(_eventsCursor, cancellation);
            if (generation != _connectionGeneration || _closing) return;
            foreach (var item in events.Events)
            {
                AppendLog(item.Type + ": " + item.Message);
                if (_pendingActionType.Length != 0 && item.Id > _pendingActionCursor &&
                    (string.Equals(item.Type, _pendingActionType, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.Type, "error", StringComparison.OrdinalIgnoreCase)))
                {
                    ClearPendingAction();
                    UpdateAvailability();
                }
            }
            _eventsCursor = events.Cursor;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation != _connectionGeneration || _closing) return;
            if (_healthy) AppendLog("Conexão indisponível: " + error.Message);
            _healthy = false;
            ResetActiveEffects();
            _connectionLabel.Text = "SEM CONFIRMAÇÃO";
            _connectionLabel.ForeColor = Theme.Warning;
            _connectionDetails.Text = error.Message;
            _revisionLabel.Text = "Estado indisponível. Efeitos anteriores podem persistir até 15 s; verificando conexão…";
            UpdateAvailability();
        }
        finally { if (generation == _connectionGeneration) _polling = false; }
    }

    private void ApplyStatus(ServerStatus status)
    {
        if (!status.Ready || !status.PlayerOnline || !status.SessionActive) ResetActiveEffects();
        ResetUnsupportedEffects(status);
        _connectionLabel.Text = status.Ready && status.PlayerOnline && status.SessionActive ? "CONECTADO"
            : status.Ready && status.PlayerOnline ? "INICIANDO SESSÃO"
            : status.Ready ? "AGUARDANDO JOGADOR" : "RECURSO INDISPONÍVEL";
        _connectionLabel.ForeColor = status.Ready && status.PlayerOnline && status.SessionActive ? Theme.Success : Theme.Warning;
        _connectionDetails.Text = status.Message;
        if (!string.IsNullOrWhiteSpace(status.MedicalMessage))
            _connectionDetails.Text += Environment.NewLine + status.MedicalMessage;
        _serverLabel.Text = status.PlayerOnline
            ? $"Jogador: {status.PlayerName} (ID {status.PlayerId}) · recurso {status.Version}"
            : $"Recurso {status.Version} · jogador vinculado ainda não está online";
        if (!status.Ready || !status.PlayerOnline)
            _revisionLabel.Text = "Sem sessão disponível. Opções locais desativadas; efeitos anteriores podem persistir até 15 s.";
        else if (!status.SessionActive)
            _revisionLabel.Text = "Sem sessão ativa. Enviando opções desativadas para iniciar uma nova sessão…";
        else if (!_dirty && !_sending)
        {
            _revisionLabel.Text = status.AppliedRevision == status.DesiredRevision
                ? $"Revisão {status.AppliedRevision}: o script do cliente reportou a aplicação."
                : $"Revisão enviada {status.DesiredRevision}; aplicada {status.AppliedRevision}. Aguardando cliente FiveM…";
        }
        else if (_dirty && !_sending) _revisionLabel.Text = "Opções locais aguardando envio ao cliente FiveM…";
        UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        var connected = _client is not null;
        _connect.Enabled = !_pairing.Enabled && !_pairingStarting && !_connecting && !_disconnecting;
        _connect.Text = _pairing.Enabled ? "Detectando FiveM" : "Detectar FiveM";
        _disconnect.Enabled = (connected || _pairing.Enabled) && !_closing && !_disconnecting;
        var available = !_disconnecting && _healthy && _lastStatus is { Ready: true, PlayerOnline: true, SessionActive: true };
        var capabilities = new HashSet<string>(_lastStatus?.Capabilities ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var medicalLabel = _lastStatus?.MedicalProvider switch
        {
            "esx" => "ESX",
            "qbcore" => "QBCore",
            "standalone" => "nativo",
            _ => "personagem"
        };
        _revive.Text = capabilities.Contains("player.revive") ? $"Reviver ({medicalLabel})" : "Reviver indisponível";
        _tooltips.SetToolTip(_revive, _lastStatus?.MedicalMessage ?? "Aguardando integração médica do servidor.");
        foreach (var (capability, controls) in _capabilityControls)
            foreach (var control in controls)
                control.Enabled = capability != "silent" && available && capabilities.Contains(capability);
        if (_acting || _pendingActionType.Length != 0)
        {
            _revive.Enabled = false;
            _suicide.Enabled = false;
        }
    }

    private bool HasActiveCapability(string capability) =>
        !_closing && !_connecting && !_disconnecting && _healthy && _client is not null && _session is not null &&
        _lastStatus is { Ready: true, PlayerOnline: true, SessionActive: true } &&
        _lastStatus.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);

    private Task ReviveAsync() => ExecutePlayerActionAsync("player.revive", (client, cancellation) => client.ReviveAsync(cancellation), "Reviver");

    private Task SuicideAsync() => ExecutePlayerActionAsync("player.suicide", (client, cancellation) => client.SuicideAsync(cancellation), "Suicídio");

    private async Task ExecutePlayerActionAsync(string capability, Func<MenuApiClient, CancellationToken, Task> dispatch, string description)
    {
        // Recheck authoritative session state even if a stale UI button still looks enabled.
        ExpirePendingAction();
        if (_acting || _pendingActionType.Length != 0 || !HasActiveCapability(capability)) return;
        var client = _client!;
        var cancellation = _session!.Token;
        var generation = _connectionGeneration;
        _acting = true;
        UpdateAvailability();
        try
        {
            await dispatch(client, cancellation);
            if (generation != _connectionGeneration || _closing) return;
            _pendingActionType = capability == "player.suicide" ? "suicide" : "revived";
            _pendingActionSince = Environment.TickCount64;
            _pendingActionCursor = _eventsCursor;
            AppendLog(description + ": pedido enviado; aguardando evento do cliente.");
            if (capability == "player.suicide")
            {
                // Keep the local snapshot consistent with the server suicide reset.
                _settings.Player.GodMode = false;
                _settings.Player.Noclip = false;
                SettingsChanged();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation == _connectionGeneration && !_closing)
            {
                AppendLog(description + " falhou: " + error.Message);
                _healthy = false;
                ResetActiveEffects();
                _connectionLabel.Text = "SEM CONFIRMAÇÃO";
                _connectionLabel.ForeColor = Theme.Warning;
                _connectionDetails.Text = error.Message;
                _revisionLabel.Text = "Estado indisponível. Efeitos anteriores podem persistir até 15 s; verificando conexão…";
                UpdateAvailability();
            }
        }
        finally
        {
            if (generation == _connectionGeneration)
            {
                _acting = false;
                if (!_closing)
                {
                    UpdateAvailability();
                    if (_dirty && _healthy && !_debounce.Enabled) _debounce.Start();
                }
            }
        }
    }

    private void ClearPendingAction()
    {
        _pendingActionType = "";
        _pendingActionSince = 0;
        _pendingActionCursor = 0;
    }

    private void ExpirePendingAction()
    {
        if (_pendingActionType.Length == 0 || Environment.TickCount64 - _pendingActionSince < 10000) return;
        var description = _pendingActionType == "suicide" ? "Suicídio" : "Reviver";
        ClearPendingAction();
        AppendLog(description + ": sem confirmação em 10 s. O resultado da ação não foi confirmado.");
        if (!_closing) UpdateAvailability();
    }

    private async Task DisconnectAsync(bool stopDetection = true)
    {
        if (stopDetection)
        {
            _pairing.Enabled = false;
            _pendingTicket = null;
        }
        if (_client is null)
        {
            ClearSession();
            return;
        }
        if (_disconnecting) return;
        _disconnecting = true;
        ClearPendingAction();
        var client = _client;
        var wasHealthy = _healthy;
        _poll.Stop();
        _debounce.Stop();
        _disconnect.Enabled = false;
        _connectionLabel.Text = "DESCONECTANDO…";
        UpdateAvailability();
        _session?.Cancel();
        ++_connectionGeneration;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await client.DisconnectAsync(timeout.Token);
            AppendLog("Solicitação de desativação aceita pelo servidor.");
        }
        catch (Exception error)
        {
            AppendLog("Reset no servidor não confirmado: " + error.Message);
            if (wasHealthy) AppendLog("O recurso deve encerrar os efeitos quando a sessão expirar.");
        }
        finally
        {
            if (!_closing) ClearSession();
            else client.Dispose();
        }
    }

    private void ClearSession()
    {
        _poll.Stop();
        _debounce.Stop();
        _session?.Cancel();
        _session?.Dispose();
        _session = null;
        _client?.Dispose();
        _client = null;
        _boundTicketIdentity = "";
        _lastStatus = null;
        _healthy = false;
        _connecting = false;
        _sending = false;
        _polling = false;
        _disconnecting = false;
        _acting = false;
        ResetActiveEffects();
        _eventsCursor = 0;
        _connectionLabel.Text = _pairing.Enabled ? "AGUARDANDO FIVEM" : "DESCONECTADO";
        _connectionLabel.ForeColor = _pairing.Enabled ? Theme.Warning : Theme.Muted;
        _connectionDetails.Text = _pairing.Enabled
            ? "Abra o FiveM e entre na cidade com o recurso zyon_admin_menu atualizado."
            : "Detecção interrompida. Clique em Detectar FiveM para procurar a cidade novamente.";
        _revisionLabel.Text = "Opções locais desativadas. Efeitos anteriores podem persistir até 15 s.";
        _endpoint.Text = "Nenhuma cidade detectada";
        _serverLabel.Text = "";
        UpdateAvailability();
    }

    private void ResetActiveEffects()
    {
        ClearPendingAction();
        if (!_dirty || _settings.Aim.Enabled || _settings.Aim.DrawFov || _settings.Silent.Enabled || _settings.Silent.DrawFov ||
            _settings.Weapons.NoRecoil || _settings.Weapons.NoSpread || _settings.Weapons.FreezeAmmo ||
            _settings.Player.GodMode || _settings.Player.Noclip || _settings.Player.Invisible || _settings.Player.MuteVoice ||
            _settings.Esp.Enabled || _settings.Esp.Admins)
            _changeVersion++;
        _settings.Aim.Enabled = false;
        _settings.Aim.DrawFov = false;
        _settings.Silent.Enabled = false;
        _settings.Silent.DrawFov = false;
        _settings.Weapons.NoRecoil = false;
        _settings.Weapons.NoSpread = false;
        _settings.Weapons.FreezeAmmo = false;
        _settings.Player.GodMode = false;
        _settings.Player.Noclip = false;
        _settings.Player.Invisible = false;
        _settings.Player.MuteVoice = false;
        _settings.Esp.Enabled = false;
        _settings.Esp.Admins = false;
        SynchronizeToggles();
        _dirty = true;
    }

    private void ResetUnsupportedEffects(ServerStatus status)
    {
        var capabilities = new HashSet<string>(status.Capabilities, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        void DisableIfMissing(string capability, Func<bool> getter, Action<bool> setter)
        {
            if ((capabilities.Contains(capability) && capability != "silent") || !getter()) return;
            setter(false);
            changed = true;
        }
        DisableIfMissing("aim", () => _settings.Aim.Enabled, v => _settings.Aim.Enabled = v);
        DisableIfMissing("aim", () => _settings.Aim.DrawFov, v => _settings.Aim.DrawFov = v);
        DisableIfMissing("silent", () => _settings.Silent.Enabled, v => _settings.Silent.Enabled = v);
        DisableIfMissing("silent", () => _settings.Silent.DrawFov, v => _settings.Silent.DrawFov = v);
        DisableIfMissing("weapons.noRecoil", () => _settings.Weapons.NoRecoil, v => _settings.Weapons.NoRecoil = v);
        DisableIfMissing("weapons.noSpread", () => _settings.Weapons.NoSpread, v => _settings.Weapons.NoSpread = v);
        DisableIfMissing("weapons.freezeAmmo", () => _settings.Weapons.FreezeAmmo, v => _settings.Weapons.FreezeAmmo = v);
        DisableIfMissing("player.godMode", () => _settings.Player.GodMode, v => _settings.Player.GodMode = v);
        DisableIfMissing("player.noclip", () => _settings.Player.Noclip, v => _settings.Player.Noclip = v);
        DisableIfMissing("player.invisible", () => _settings.Player.Invisible, v => _settings.Player.Invisible = v);
        DisableIfMissing("player.muteVoice", () => _settings.Player.MuteVoice, v => _settings.Player.MuteVoice = v);
        DisableIfMissing("esp", () => _settings.Esp.Enabled, v => _settings.Esp.Enabled = v);
        DisableIfMissing("esp.boxes", () => _settings.Esp.Boxes, v => _settings.Esp.Boxes = v);
        DisableIfMissing("esp.skeletons", () => _settings.Esp.Skeletons, v => _settings.Esp.Skeletons = v);
        DisableIfMissing("esp.names", () => _settings.Esp.Names, v => _settings.Esp.Names = v);
        DisableIfMissing("esp.distance", () => _settings.Esp.Distance, v => _settings.Esp.Distance = v);
        DisableIfMissing("esp.playersOnly", () => _settings.Esp.OnlyPlayers, v => _settings.Esp.OnlyPlayers = v);
        DisableIfMissing("esp.visibility", () => _settings.Esp.OnlyVisible, v => _settings.Esp.OnlyVisible = v);
        DisableIfMissing("esp.admins", () => _settings.Esp.Admins, v => _settings.Esp.Admins = v);
        if (!changed) return;
        _changeVersion++;
        _dirty = true;
        SynchronizeToggles();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotkeyRegistered = NativeHotKeys.RegisterHotKey(Handle, NativeHotKeys.ToggleMenuId, NativeHotKeys.NoRepeat, NativeHotKeys.InsertKey);
        if (_hotkeyRegistered)
            _hotkeyLabel.Text = "Insert · ocultar / mostrar";
        else
        {
            _hotkeyLabel.Text = "Insert indisponível nesta sessão";
            _tooltips.SetToolTip(_hotkeyLabel, "Outro programa pode estar usando Insert. O menu continua acessível pela barra de tarefas.");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered)
        {
            NativeHotKeys.UnregisterHotKey(Handle, NativeHotKeys.ToggleMenuId);
            _hotkeyRegistered = false;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeHotKeys.HotKeyMessage && message.WParam.ToInt32() == NativeHotKeys.ToggleMenuId)
        {
            if (!_closing)
            {
                if (Visible) Hide();
                else
                {
                    Show();
                    if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                    Activate();
                }
            }
            return;
        }
        base.WndProc(ref message);
    }

    private void AppendLog(string message)
    {
        if (_closing || IsDisposed) return;
        if (_eventLog.TextLength > 50000) _eventLog.Text = _eventLog.Text[^30000..];
        _eventLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        Enabled = false;
        _pairing.Enabled = false;
        _pendingTicket = null;
        _lifetime.Cancel();
        await DisconnectAsync();
        _session?.Cancel();
        _session?.Dispose();
        _debounce.Dispose();
        _poll.Dispose();
        _tooltips.Dispose();
        _pairing.TicketReceived -= OnPairingTicketReceived;
        _pairing.Dispose();
        _lifetime.Dispose();
        Close();
    }
}
