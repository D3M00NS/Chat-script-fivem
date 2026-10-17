// C# / src/ZyonMenu.Windows/MainForm.cs / .NET 8 WinForms / Windows 10+ x64.
using System.Text.Json;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal sealed class MainForm : Form
{
    private readonly MenuSettings _settings = CreateLocalDefaults();
    private readonly Dictionary<string, Control> _pages = new();
    private readonly Dictionary<string, Button> _navigation = new();
    private readonly Dictionary<string, List<Control>> _capabilityControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(ToggleSwitch Control, Func<bool> Getter)> _toggleBindings = new();
    private readonly ToolTip _tooltips = new();
    private readonly Dictionary<Control, string> _configurationTooltips = new();
    private readonly Dictionary<Control, string> _disabledControlReasons = new();
    private readonly HashSet<Control> _tooltipParents = new();
    private Control? _disabledTooltipControl;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 60 };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1600 };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(24, 16, 12, 12) };
    private readonly Label _connectionLabel = Theme.Label("DESCONECTADO", true);
    private readonly Label _hotkeyLabel = Theme.Label("Insert · ocultar / mostrar", true);
    private readonly Label _revisionLabel = Theme.Label("Aguardando detecção automática do FiveM.", true);
    private readonly Label _serverLabel = Theme.Label("", true);
    private readonly Label _connectionDetails = Theme.Label("Abra o FiveM na build b3258. As escolhas locais aguardam um processo compatível.", true);
    private readonly Label _readinessDetails = Theme.Label("Verificando a conexão do cliente…", true);
    private readonly Button _readinessRefresh = Theme.Button("Verificar novamente");
    private readonly TextBox _process = Theme.TextBox();
    private readonly TextBox _eventLog = new();
    private readonly Button _connect = Theme.Button("Detectar FiveM", true);
    private readonly Button _disconnect = Theme.Button("Desconectar");
    private readonly Button _saveDiagnostic = Theme.Button("Salvar diagnóstico");
    private readonly Button _revive = Theme.Button("Reviver indisponível");
    private readonly Button _suicide = Theme.Button("Suicídio");
    private readonly Button _lockVehicle = Theme.Button("Bloquear carro");
    private readonly Button _unlockVehicle = Theme.Button("Desbloquear carro");
    private readonly Label _playerActionStatus = Theme.Label("Ações locais exigem uma sessão ativa. Suicídio pede confirmação.", true);
    private readonly Label _vehicleActionStatus = Theme.Label("Sem implementação externa nesta versão.", true);
    private readonly DataGridView _featureStates = new();
    private readonly LocalGameSession _client = new();
    private readonly StandaloneUiPolicy _policy = new();
    private readonly HashSet<string> _knownCapabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _session;
    private Task? _settingsRequest;
    private ServerStatus? _lastStatus;
    private bool _healthy;
    private bool _polling;
    private bool _sending;
    private bool _disconnecting;
    private bool _suiciding;
    private bool _closing;
    private bool _dirty = true;
    private bool _synchronizing;
    private bool _hotkeyRegistered;
    private bool _capabilitiesKnown;
    private long _changeVersion;
    private long _connectionGeneration;
    private long _eventsCursor;
    private string? _lastPlayerBlockReason;
    private DateTimeOffset _playerActionFeedbackUntilUtc;

    private static MenuSettings CreateLocalDefaults()
    {
        var settings = new MenuSettings();
        settings.Aim.OnlyVisible = false;
        settings.Silent.OnlyVisible = true;
        settings.Silent.OnlyPlayers = true;
        settings.Silent.DrawFov = false;
        settings.Esp.OnlyVisible = false;
        settings.Esp.Names = false;
        return settings;
    }

    public MainForm()
    {
        Text = AppIdentity.StandaloneTitle;
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
        BuildVehiclePage();
        BuildEspPage();
        BuildWeaponsPage();
        BuildConnectionPage();
        ShowPage("Aim");

        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SendSettingsAsync(); };
        _poll.Tick += async (_, _) => await PollAsync();
        _connect.Click += async (_, _) => await StartDetectionAsync();
        _disconnect.Click += async (_, _) => await DisconnectAsync();
        _saveDiagnostic.Click += (_, _) => SaveDiagnostic();
        _readinessRefresh.Click += async (_, _) => await StartDetectionAsync();
        _suicide.Click += async (_, _) => await SuicideAsync();
        FormClosing += OnFormClosing;
        Shown += async (_, _) =>
        {
            FitToWorkingArea();
            await StartDetectionAsync();
        };
        UpdateAvailability();
    }

    private void BuildShell()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 104, BackColor = Theme.Sidebar, Padding = new Padding(10, 18, 10, 12) };
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
            AutoScroll = true,
            Padding = new Padding(0, 22, 0, 0),
            BackColor = Theme.Sidebar
        };
        foreach (var (name, icon, caption) in new[]
                 {
                     ("Aim", "◎", "Aim"),
                     ("Player", "♟", "Personagem"),
                     ("Noclip", "↟", "Noclip"),
                     ("Vehicle", "▱", "Veículo"),
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
        var header = new Panel { Dock = DockStyle.Top, Height = 190, Padding = new Padding(26, 15, 26, 8), BackColor = Theme.Background };
        var title = Theme.Label($"ZYON {AppIdentity.DisplayVersion}  /  FiveM");
        title.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        title.Location = new Point(26, 16);
        var subtitle = Theme.Label("FiveM b3258 · detecção automática", true);
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
            _readinessDetails.Width = Math.Max(100, header.ClientSize.Width - 52);
        };
        _tooltips.SetToolTip(_hotkeyLabel, "Insert oculta ou mostra o menu. A sessão permanece conectada enquanto a janela está oculta.");
        _readinessDetails.AutoSize = false;
        _readinessDetails.AutoEllipsis = true;
        _readinessDetails.Location = new Point(26, 76);
        _readinessDetails.Size = new Size(790, 48);
        _readinessDetails.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _readinessRefresh.Location = new Point(26, 136);
        _readinessRefresh.Width = 210;
        _saveDiagnostic.Location = new Point(256, 136);
        _saveDiagnostic.Width = 185;
        header.Controls.AddRange(new Control[] { title, subtitle, _connectionLabel, _hotkeyLabel,
            _readinessDetails, _readinessRefresh, _saveDiagnostic });

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
        page.Controls.Add(SilentCard());
        page.Controls.Add(WeaponsCard(408));
        var note = Card("Configuração local", 408, 258);
        AddNote(note, "Você pode preparar as opções antes de abrir o FiveM. Elas ficam pendentes até validar o processo b3258 e o personagem local. A barra inferior mostra a configuração recebida pelo backend. A situação de cada função indica o que foi confirmado em leitura; o comportamento no jogo precisa ser conferido. Peito (aprox.) no Aimbot usa um ponto estimado na parte superior do tronco. Linha de visão do Aimbot não está disponível neste perfil.", 61, 360);
        page.Controls.Add(note);
    }

    private static RoundedCard SilentCard()
    {
        var card = Card("Silent Aim · indisponível", 408, 188);
        AddNote(card, "Silent Aim não está implementado no backend externo. Nenhuma alteração de tiro é enviada por esta versão.", 62, 360);
        return card;
    }

    private RoundedCard AimCard(string title, AimSettings settings, string capability)
    {
        var card = Card(title);
        var y = 60;
        AddToggle(card, "Ativar " + title, () => settings.Enabled, v => settings.Enabled = v, capability, y); y += 40;
        AddToggle(card, "Somente visível", () => settings.OnlyVisible, v => settings.OnlyVisible = v, capability == "aim" ? "aim.visibility" : capability, y); y += 40;
        AddChoice(card, "Alvo", new[] { "Head", "Neck", "Chest" }, new[] { "Cabeça", "Pescoço", "Peito (aprox.)" }, settings.TargetBone, v => settings.TargetBone = v, capability, y); y += 40;
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
        const int height = 326;
        var card = Card("Personagem", 408, height);
        AddToggle(card, "GodMode", () => _settings.Player.GodMode, v => _settings.Player.GodMode = v, "player.godMode", 64);
        const int buttonTop = 124;
        _tooltips.SetToolTip(_revive, "Reviver externo ainda não implementado neste perfil.");
        _revive.Location = new Point(20, buttonTop);
        _revive.Width = 220;
        card.Controls.Add(_revive);
        RegisterCapability("player.revive", _revive);
        _suicide.Location = new Point(20, buttonTop + 52);
        _suicide.Width = 220;
        _tooltips.SetToolTip(_suicide, "Pede confirmação, desativa GodMode e define a vida local como zero. O fluxo médico da cidade pode tratar a morte de outra forma.");
        _playerActionStatus.AutoSize = false;
        _playerActionStatus.Location = new Point(20, buttonTop + 104);
        _playerActionStatus.Size = new Size(360, 76);
        card.Controls.AddRange(new Control[] { _suicide, _playerActionStatus });
        RegisterCapability("player.suicide", _suicide);
        page.Controls.Add(card);
        var note = Card("Estado do personagem", 408, height);
        AddNote(note, "GodMode pode ser preparado localmente e depende de um personagem validado. Consulte Situação das funções para distinguir configuração recebida de valores confirmados em leitura.\n\nSuicídio define a vida local como zero após confirmação e desativa GodMode. Confirme a morte dentro da cidade. Reviver e noclip permanecem indisponíveis neste perfil externo.", 62, 360);
        page.Controls.Add(note);
    }

    private void BuildEspPage()
    {
        var page = NewPage("ESP");
        var card = Card("ESP", 408, 435);
        var y = 62;
        AddToggle(card, "Ativar ESP", () => _settings.Esp.Enabled, v => _settings.Esp.Enabled = v, "esp", y); y += 40;
        AddToggle(card, "Caixas", () => _settings.Esp.Boxes, v => _settings.Esp.Boxes = v, "esp.boxes", y); y += 40;
        AddToggle(card, "Esqueleto", () => _settings.Esp.Skeletons, v => _settings.Esp.Skeletons = v, "esp.skeletons", y); y += 40;
        AddToggle(card, "Nomes", () => _settings.Esp.Names, v => _settings.Esp.Names = v, "esp.names", y); y += 40;
        AddToggle(card, "Distância", () => _settings.Esp.Distance, v => _settings.Esp.Distance = v, "esp.distance", y); y += 40;
        AddToggle(card, "Apenas jogadores", () => _settings.Esp.OnlyPlayers, v => _settings.Esp.OnlyPlayers = v, "esp.playersOnly", y); y += 40;
        AddToggle(card, "Somente visível", () => _settings.Esp.OnlyVisible, v => _settings.Esp.OnlyVisible = v, "esp.visibility", y); y += 40;
        AddSlider(card, "Alcance (m)", 25, 1000, _settings.Esp.MaxDistance, v => _settings.Esp.MaxDistance = v, "esp", y);
        page.Controls.Add(card);
        var note = Card("Renderização", 408, 222);
        AddNote(note, "O backend local fornece dados ao overlay externo após validar a build. O ESP fica pendente enquanto o processo ou o personagem não estiver pronto. Nomes e linha de visão não estão disponíveis neste perfil.", 62, 360);
        page.Controls.Add(note);
    }

    private void BuildNoclipPage()
    {
        var page = NewPage("Noclip");
        var card = Card("Noclip / voar · indisponível", 408, 215);
        AddToggle(card, "Noclip indisponível", () => _settings.Player.Noclip, v => _settings.Player.Noclip = v, "player.noclip", 64);
        AddSlider(card, "Velocidade (m/s)", 1, 50, _settings.Player.NoclipSpeed,
            v => _settings.Player.NoclipSpeed = v, "player.noclip", 108);
        AddNote(card, "Noclip externo ainda não implementado.", 166, 360);
        page.Controls.Add(card);
        var note = Card("Disponibilidade", 408, 248);
        AddNote(note, "Movimento e velocidade do noclip não são enviados ao jogo por esta versão. O controle de velocidade permanece desativado enquanto essa função não tem suporte real.", 62, 360);
        page.Controls.Add(note);
    }

    private void BuildVehiclePage()
    {
        var page = NewPage("Vehicle");
        var card = Card("Veículo · indisponível", 408, 302);
        _lockVehicle.Location = new Point(20, 65);
        _lockVehicle.Width = 220;
        _unlockVehicle.Location = new Point(20, 117);
        _unlockVehicle.Width = 220;
        _vehicleActionStatus.AutoSize = false;
        _vehicleActionStatus.Location = new Point(20, 178);
        _vehicleActionStatus.Size = new Size(360, 106);
        _vehicleActionStatus.Text = "Sem implementação externa nesta versão.";
        card.Controls.AddRange(new Control[] { _lockVehicle, _unlockVehicle, _vehicleActionStatus });
        RegisterCapability("vehicle.lock", _lockVehicle);
        RegisterCapability("vehicle.unlock", _unlockVehicle);
        page.Controls.Add(card);
    }

    private void BuildWeaponsPage()
    {
        var page = NewPage("Weapons");
        page.Controls.Add(WeaponsCard(408));
        var note = Card("Armas", 408, 220);
        AddNote(note, "No Recoil e No Spread só ficam disponíveis quando o perfil confirma os campos da arma. Configuração recebida não confirma efeito no jogo; consulte a situação de cada função. Freeze Ammo externo ainda não implementado.", 62, 360);
        page.Controls.Add(note);
    }

    private RoundedCard WeaponsCard(int width)
    {
        var card = Card("Weapons Settings", width, 212);
        AddToggle(card, "No Recoil", () => _settings.Weapons.NoRecoil, v => _settings.Weapons.NoRecoil = v, "weapons.noRecoil", 64);
        AddToggle(card, "No Spread", () => _settings.Weapons.NoSpread, v => _settings.Weapons.NoSpread = v, "weapons.noSpread", 106);
        AddToggle(card, "Freeze Ammo · indisponível", () => _settings.Weapons.FreezeAmmo, v => _settings.Weapons.FreezeAmmo = v, "weapons.freezeAmmo", 148);
        return card;
    }

    private void BuildConnectionPage()
    {
        var page = NewPage("Connection");
        var featureCard = BuildFeatureStatusCard();
        page.Controls.Add(featureCard);
        var card = Card("Detecção automática do processo", 834, 315);
        var processLabel = Theme.Label("Processo detectado");
        processLabel.Location = new Point(20, 60);
        _process.Dock = DockStyle.None;
        _process.Location = new Point(20, 87);
        _process.Size = new Size(790, 30);
        _process.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _process.Text = "Aguardando FiveM_b3258_GTAProcess.exe…";
        _process.ReadOnly = true;
        _process.AccessibleName = "Processo local detectado automaticamente";
        var note = Theme.Label("Abra o FiveM na build b3258. As escolhas locais aguardam um processo compatível.", true);
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
        card.Controls.AddRange(new Control[] { processLabel, _process, note, _connect, _disconnect, _connectionDetails });
        var removeLegacy = Theme.Button("Remover integração antiga");
        removeLegacy.Location = new Point(350, 187);
        removeLegacy.Width = 240;
        removeLegacy.Click += (_, _) => LegacyPluginRemovalDialog.Show(this);
        card.Controls.Add(removeLegacy);
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
            featureCard.Width = width;
            events.Width = width;
        };
    }

    private RoundedCard BuildFeatureStatusCard()
    {
        var card = Card("Situação das funções", 834, 328);
        var note = Theme.Label("Leitura confirmada indica valores observados pelo backend; confira o comportamento no FiveM.", true);
        note.AutoSize = false;
        note.Location = new Point(20, 54);
        note.Size = new Size(790, 38);
        note.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _featureStates.Location = new Point(20, 100);
        _featureStates.Size = new Size(790, 208);
        _featureStates.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _featureStates.ReadOnly = true;
        _featureStates.AllowUserToAddRows = false;
        _featureStates.AllowUserToDeleteRows = false;
        _featureStates.AllowUserToResizeRows = false;
        _featureStates.RowHeadersVisible = false;
        _featureStates.MultiSelect = false;
        _featureStates.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _featureStates.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _featureStates.BackgroundColor = Theme.Card;
        _featureStates.BorderStyle = BorderStyle.None;
        _featureStates.GridColor = Theme.Line;
        _featureStates.EnableHeadersVisualStyles = false;
        _featureStates.ColumnHeadersDefaultCellStyle.BackColor = Theme.Field;
        _featureStates.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        _featureStates.DefaultCellStyle.BackColor = Theme.Card;
        _featureStates.DefaultCellStyle.ForeColor = Theme.Text;
        _featureStates.DefaultCellStyle.SelectionBackColor = Theme.Field;
        _featureStates.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _featureStates.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _featureStates.Columns.Add(new DataGridViewTextBoxColumn { Name = "Feature", HeaderText = "Função", Width = 125 });
        _featureStates.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Situação", Width = 175 });
        _featureStates.Columns.Add(new DataGridViewTextBoxColumn { Name = "Detail", HeaderText = "Detalhes", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 160 });
        card.Controls.AddRange(new Control[] { note, _featureStates });
        UpdateFeatureStates(null);
        return card;
    }

    private void UpdateFeatureStates(IEnumerable<FeatureStatus>? states)
    {
        if (_featureStates.Columns.Count == 0) return;
        var firstVisibleRow = _featureStates.FirstDisplayedScrollingRowIndex;
        var selectedRow = _featureStates.CurrentCell?.RowIndex ?? -1;
        _featureStates.Rows.Clear();
        void RestorePosition()
        {
            if (_featureStates.Rows.Count == 0) return;
            if (selectedRow >= 0)
                _featureStates.CurrentCell = _featureStates.Rows[Math.Min(selectedRow, _featureStates.Rows.Count - 1)].Cells[0];
            if (firstVisibleRow >= 0)
                _featureStates.FirstDisplayedScrollingRowIndex = Math.Min(firstVisibleRow, _featureStates.Rows.Count - 1);
        }
        var reported = states?.ToList() ?? new List<FeatureStatus>();
        if (reported.Count == 0)
        {
            _featureStates.Rows.Add("Backend local", "Sem confirmação", "Aguardando um processo e um personagem compatíveis.");
            foreach (var feature in new[] { "Silent Aim", "Reviver", "Noclip / voar", "Invisibilidade", "Freeze Ammo", "Bloquear carro", "Desbloquear carro" })
                _featureStates.Rows.Add(feature, "Não implementada", "Indisponível neste perfil externo.");
            RestorePosition();
            return;
        }
        foreach (var feature in reported)
        {
            var name = feature.Name switch
            {
                "aim" => "Aim",
                "silent" => "Silent Aim",
                "esp" => "ESP",
                "player.godMode" => "GodMode",
                "player.revive" => "Reviver",
                "player.suicide" => "Suicídio",
                "player.noclip" => "Noclip / voar",
                "player.invisible" => "Invisibilidade",
                "player.muteVoice" => "Voz recebida",
                "esp.admins" => "ADMs no ESP",
                "esp.names" => "Nomes no ESP",
                "esp.visibility" => "Visibilidade do ESP",
                "aim.visibility" => "Visibilidade da mira",
                "weapons.noRecoil" => "No Recoil",
                "weapons.noSpread" => "No Spread",
                "weapons.freezeAmmo" => "Freeze Ammo",
                "vehicle.lock" => "Bloquear carro",
                "vehicle.unlock" => "Desbloquear carro",
                _ => feature.Name
            };
            var state = feature.State switch
            {
                "ready" => "Disponível para configurar",
                "waiting" => "Aguardando dados",
                "observed" => "Leitura confirmada",
                "blocked" => "Bloqueada",
                "failed" => "Falha na execução",
                "unsupported" => "Não implementada",
                _ => "Sem confirmação"
            };
            var detail = feature.Message;
            if (feature.State == "observed")
            {
                if (feature.ObservedUtc is null)
                    state = "Leitura sem horário";
                else if (DateTimeOffset.UtcNow - feature.ObservedUtc.Value > TimeSpan.FromSeconds(5))
                {
                    state = "Leitura desatualizada";
                    detail = "Aguardando nova leitura. " + detail;
                }
            }
            _featureStates.Rows.Add(name, state, detail);
        }
        RestorePosition();
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
            _tooltips.SetToolTip(toggle, "Silent Aim externo ainda não implementado.");
        if (capability == "weapons.noRecoil")
            _tooltips.SetToolTip(toggle, "Só será aplicado se o perfil b3258 confirmar os campos de recuo da arma.");
        if (capability == "weapons.freezeAmmo")
            _tooltips.SetToolTip(toggle, "Freeze Ammo externo ainda não implementado.");
        if (capability == "player.godMode")
            _tooltips.SetToolTip(toggle, "Estado local de dano. Scripts da cidade podem alterar saúde e estado de morte separadamente.");
        if (capability == "player.noclip")
            _tooltips.SetToolTip(toggle, "Noclip externo ainda não implementado. O ajuste permanece desativado.");
        if (capability is "aim.visibility" or "esp.visibility")
            _tooltips.SetToolTip(toggle, "Linha de visão não confirmada para o perfil b3258.");
        if (capability == "esp.names")
            _tooltips.SetToolTip(toggle, "Nomes de jogadores não confirmados para o perfil b3258.");
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
        combo.SelectedIndexChanged += (_, _) => { setter(values[combo.SelectedIndex]); SettingsChanged(); };
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
        slider.ValueChanged += (_, _) => { number.Text = slider.Value.ToString(); setter(slider.Value); SettingsChanged(); };
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
            if (!_debounce.Enabled) _debounce.Start();
        }
        else _revisionLabel.Text = "Escolhas locais pendentes; aguardando processo e personagem validados.";
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
        if (_closing || _disconnecting) return;
        _client.DetectionEnabled = true;
        if (_session is null)
        {
            _session = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            ++_connectionGeneration;
        }
        _connectionLabel.Text = "AGUARDANDO FIVEM";
        _connectionLabel.ForeColor = Theme.Warning;
        _connectionDetails.Text = "Procurando FiveM_b3258_GTAProcess.exe e um personagem local válido…";
        _revisionLabel.Text = "Escolhas locais pendentes; nenhum recebimento confirmado pelo backend.";
        _poll.Start();
        UpdateAvailability();
        await PollAsync();
    }

    private async Task SendSettingsAsync()
    {
        if (_sending || _suiciding || _disconnecting || !_dirty || !_healthy || !_client.DetectionEnabled ||
            _lastStatus is not { Ready: true, PlayerOnline: true } || _session is null) return;
        var cancellation = _session.Token;
        var generation = _connectionGeneration;
        var changeVersion = _changeVersion;
        var snapshot = JsonSerializer.Deserialize<MenuSettings>(JsonSerializer.Serialize(_settings))!;
        _sending = true;
        _revisionLabel.Text = "Enviando configuração ao backend local…";
        try
        {
            var request = _client.PushSettingsAsync(snapshot, cancellation);
            _settingsRequest = request;
            await request;
            if (generation != _connectionGeneration || _closing) return;
            _dirty = changeVersion != _changeVersion;
            _revisionLabel.Text = _dirty ? "Novas escolhas locais aguardando envio…" : "Configuração enviada; aguardando recebimento pelo backend…";
            if (_dirty) _debounce.Start();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation != _connectionGeneration || _closing) return;
            ObserveUnavailableBackend();
            _connectionLabel.Text = "APLICAÇÃO NÃO CONFIRMADA";
            _connectionLabel.ForeColor = Theme.Warning;
            _connectionDetails.Text = error.Message;
            _revisionLabel.Text = "Aplicação não confirmada. Verificando o processo local…";
            AppendLog("Aplicação local falhou: " + error.Message);
            UpdateAvailability();
        }
        finally
        {
            if (generation == _connectionGeneration)
            {
                _sending = false;
                _settingsRequest = null;
            }
        }
    }

    private async Task PollAsync()
    {
        if (_polling || !_client.DetectionEnabled || _session is null) return;
        var cancellation = _session.Token;
        var generation = _connectionGeneration;
        _polling = true;
        try
        {
            var status = await _client.GetStatusAsync(cancellation);
            if (generation != _connectionGeneration || _closing) return;
            _healthy = true;
            if (_policy.Observe(true, status.Ready, status.PlayerOnline, status.SessionActive, status.PlayerId))
            {
                ResetActiveEffects();
                AppendLog("A sessão anterior foi perdida ou substituída. Os efeitos locais foram desativados.");
            }
            _lastStatus = status;
            if (status.Ready)
            {
                _knownCapabilities.Clear();
                _knownCapabilities.UnionWith(status.Capabilities ?? Array.Empty<string>());
                _capabilitiesKnown = true;
            }
            RemoveUnsupportedEffects(status);
            ApplyStatus(status);
            if (_dirty && status.Ready && status.PlayerOnline && !_debounce.Enabled && !_sending) _debounce.Start();
            var events = await _client.GetEventsAsync(_eventsCursor, cancellation);
            if (generation != _connectionGeneration || _closing) return;
            foreach (var item in events.Events) AppendLog(item.Type + ": " + item.Message);
            _eventsCursor = events.Cursor;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation != _connectionGeneration || _closing) return;
            if (_healthy) AppendLog("Estado local indisponível: " + error.Message);
            ObserveUnavailableBackend();
            _connectionLabel.Text = "ESTADO INDISPONÍVEL";
            _connectionLabel.ForeColor = Theme.Warning;
            _connectionDetails.Text = error.Message;
            _revisionLabel.Text = "Estado do processo não confirmado. As escolhas locais aguardam validação…";
            UpdateAvailability();
        }
        finally { if (generation == _connectionGeneration) _polling = false; }
    }

    private void ObserveUnavailableBackend()
    {
        _healthy = false;
        UpdateFeatureStates(null);
        if (_policy.Observe(false, false, false, false)) ResetActiveEffects();
    }

    private void RemoveUnsupportedEffects(ServerStatus status)
    {
        if (!status.Ready) return;
        var capabilities = new HashSet<string>(status.Capabilities ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var changed = false;
        bool Keep(bool enabled, string capability)
        {
            if (enabled && !capabilities.Contains(capability)) changed = true;
            return enabled && capabilities.Contains(capability);
        }
        _settings.Aim.Enabled = Keep(_settings.Aim.Enabled, "aim");
        _settings.Aim.DrawFov = Keep(_settings.Aim.DrawFov, "aim");
        _settings.Aim.OnlyVisible = Keep(_settings.Aim.OnlyVisible, "aim.visibility");
        _settings.Aim.OnlyPlayers = Keep(_settings.Aim.OnlyPlayers, "aim.playersOnly");
        _settings.Silent.Enabled = false;
        _settings.Silent.DrawFov = false;
        _settings.Silent.OnlyPlayers = true;
        _settings.Weapons.NoRecoil = Keep(_settings.Weapons.NoRecoil, "weapons.noRecoil");
        _settings.Weapons.NoSpread = Keep(_settings.Weapons.NoSpread, "weapons.noSpread");
        _settings.Weapons.FreezeAmmo = Keep(_settings.Weapons.FreezeAmmo, "weapons.freezeAmmo");
        _settings.Player.GodMode = Keep(_settings.Player.GodMode, "player.godMode");
        _settings.Player.Noclip = Keep(_settings.Player.Noclip, "player.noclip");
        _settings.Player.Invisible = Keep(_settings.Player.Invisible, "player.invisible");
        _settings.Player.MuteVoice = Keep(_settings.Player.MuteVoice, "player.muteVoice");
        _settings.Esp.Admins = Keep(_settings.Esp.Admins, "esp.admins");
        _settings.Esp.Enabled = Keep(_settings.Esp.Enabled, "esp");
        _settings.Esp.Boxes = Keep(_settings.Esp.Boxes, "esp.boxes");
        _settings.Esp.Skeletons = Keep(_settings.Esp.Skeletons, "esp.skeletons");
        _settings.Esp.Names = Keep(_settings.Esp.Names, "esp.names");
        _settings.Esp.Distance = Keep(_settings.Esp.Distance, "esp.distance");
        _settings.Esp.OnlyPlayers = Keep(_settings.Esp.OnlyPlayers, "esp.playersOnly");
        _settings.Esp.OnlyVisible = Keep(_settings.Esp.OnlyVisible, "esp.visibility");
        if (changed)
        {
            _dirty = true;
            _changeVersion++;
            SynchronizeToggles();
            AppendLog("Opções sem suporte confirmado pelo perfil detectado foram desativadas.");
        }
    }

    private void ApplyStatus(ServerStatus status)
    {
        _settings.Silent.Enabled = false;
        UpdateFeatureStates(status.FeatureStates);
        _connectionLabel.Text = status.Ready && status.PlayerOnline && status.SessionActive ? "CONFIGURAÇÃO RECEBIDA"
            : status.Ready && status.PlayerOnline ? "PERSONAGEM VALIDADO"
            : status.Ready ? "AGUARDANDO PERSONAGEM" : "AGUARDANDO FIVEM COMPATÍVEL";
        _connectionLabel.ForeColor = status.Ready && status.PlayerOnline && status.SessionActive ? Theme.Success : Theme.Warning;
        _connectionDetails.Text = status.Message;
        _process.Text = status.PlayerId is not null
            ? $"FiveM_b3258_GTAProcess.exe · PID {status.PlayerId}"
            : "Aguardando FiveM_b3258_GTAProcess.exe…";
        _serverLabel.Text = status.PlayerId is not null
            ? $"{status.PlayerName} · PID {status.PlayerId} · perfil {status.Version}"
            : $"Perfil alvo b3258 · {status.Version}";
        if (!status.Ready || !status.PlayerOnline)
            _revisionLabel.Text = "Escolhas locais pendentes; nenhum recebimento confirmado pelo backend.";
        else if (!status.SessionActive)
            _revisionLabel.Text = "Processo validado. As escolhas locais aguardam recebimento pelo backend…";
        else if (!_dirty && !_sending)
            _revisionLabel.Text = status.AppliedRevision == status.DesiredRevision
                ? $"Configuração {status.AppliedRevision} recebida pelo backend. Consulte Situação das funções."
                : $"Configuração enviada {status.DesiredRevision}; recebida {status.AppliedRevision}.";
        else if (_dirty && !_sending) _revisionLabel.Text = "Novas escolhas locais aguardando envio…";
        UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        UpdateReadinessBanner();
        _connect.Enabled = !_client.DetectionEnabled && !_disconnecting && !_closing;
        _connect.Text = _client.DetectionEnabled ? "Detectando FiveM" : "Detectar FiveM";
        _disconnect.Enabled = _client.DetectionEnabled && !_closing && !_disconnecting;
        var capabilities = _knownCapabilities;
        foreach (var (capability, controls) in _capabilityControls)
        {
            var enabled = !_disconnecting && !_closing && StandaloneUiPolicy.CanEditConfiguration(capability, _capabilitiesKnown, capabilities);
            if (capability == "player.suicide")
                enabled = !_suiciding && CanInvokeSuicide();
            if (capability == "player.godMode" && _suiciding)
                enabled = false;
            if (capability is "weapons.noRecoil" or "weapons.noSpread")
                enabled &= _healthy && _lastStatus is { Ready: true, PlayerOnline: true } &&
                    _lastStatus.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);
            foreach (var control in controls)
            {
                control.Enabled = enabled;
                if (!HasDisabledReason(capability)) continue;
                if (!_configurationTooltips.ContainsKey(control)) _configurationTooltips[control] = _tooltips.GetToolTip(control) ?? "";
                if (enabled)
                {
                    _disabledControlReasons.Remove(control);
                    _tooltips.SetToolTip(control, _configurationTooltips[control]);
                }
                else
                {
                    var reason = ActionBlockReason(capability);
                    _disabledControlReasons[control] = reason;
                    _tooltips.SetToolTip(control, reason);
                    RegisterDisabledControlTooltip(control);
                }
            }
        }
        UpdateActionBlockMessages();
    }

    private static bool HasDisabledReason(string capability) => capability is
        "player.godMode" or "player.noclip" or "player.invisible" or "player.revive" or "player.suicide" or
        "weapons.freezeAmmo" or "silent" or "vehicle.lock" or "vehicle.unlock";

    private void UpdateReadinessBanner()
    {
        _readinessRefresh.Enabled = !_closing && !_disconnecting;
        _saveDiagnostic.Enabled = !_closing;
        var reason = ActionBlockReason();
        _readinessDetails.Text = string.IsNullOrEmpty(reason)
            ? "Processo e personagem confirmados. Consulte a situação de cada função na aba Conexão."
            : reason;
        _readinessDetails.ForeColor = string.IsNullOrEmpty(reason) ? Theme.Success : Theme.Warning;
        _tooltips.SetToolTip(_readinessDetails, _readinessDetails.Text);
    }

    private string ActionBlockReason(string? capability = null)
    {
        if (capability is "silent" or "player.revive" or "player.noclip" or "player.invisible" or
            "weapons.freezeAmmo" or "vehicle.lock" or "vehicle.unlock")
            return "Sem implementação externa nesta versão.";
        if (_closing || _disconnecting) return "A sessão está sendo encerrada. Aguarde para conectar novamente.";
        if (!_client.DetectionEnabled) return "Detecção interrompida. Clique em Verificar novamente para procurar o FiveM.";
        if (!_healthy || _lastStatus is not { Ready: true, PlayerOnline: true })
            return string.IsNullOrWhiteSpace(_lastStatus?.Message) ? "Aguardando FiveM b3258 e um personagem válido." : _lastStatus.Message;
        if (!_lastStatus.SessionActive) return "Personagem validado. Aguardando a configuração local.";
        if (capability is not null && !_lastStatus.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            return "O perfil detectado ainda não confirmou suporte para esta função. Consulte a aba Conexão.";
        if (_suiciding) return "Aguarde a ação atual terminar.";
        return "";
    }

    private void UpdateActionBlockMessages()
    {
        var reason = CanInvokeSuicide() || CanInvokeLocalAction("player.godMode") ? "" : ActionBlockReason();
        if (!_suiciding && DateTimeOffset.UtcNow >= _playerActionFeedbackUntilUtc)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                _lastPlayerBlockReason = reason;
                _playerActionStatus.Text = reason;
                _playerActionStatus.ForeColor = Theme.Warning;
            }
            else if (_playerActionStatus.Text == _lastPlayerBlockReason)
            {
                _playerActionStatus.Text = "Use as funções habilitadas. Suicídio pede confirmação.";
                _playerActionStatus.ForeColor = Theme.Muted;
                _lastPlayerBlockReason = null;
            }
        }
    }

    private void RegisterDisabledControlTooltip(Control control)
    {
        // Disabled WinForms controls do not receive mouse messages, so show the reason from their card.
        if (control.Parent is not { } parent || !_tooltipParents.Add(parent)) return;
        parent.MouseMove += (_, args) =>
        {
            var hovered = parent.Controls.Cast<Control>().FirstOrDefault(child =>
                !child.Enabled && child.Visible && child.Bounds.Contains(args.Location) && _disabledControlReasons.ContainsKey(child));
            if (ReferenceEquals(hovered, _disabledTooltipControl)) return;
            _disabledTooltipControl = hovered;
            _tooltips.Hide(parent);
            if (hovered is not null)
                _tooltips.Show(_disabledControlReasons[hovered], parent, args.X + 12, args.Y + 20, 5000);
        };
        parent.MouseLeave += (_, _) =>
        {
            _disabledTooltipControl = null;
            _tooltips.Hide(parent);
        };
    }

    private bool CanInvokeLocalAction(string capability) =>
        !_closing && !_disconnecting && _client.DetectionEnabled && _session is not null &&
        _lastStatus is { } status && StandaloneUiPolicy.CanInvokeAction(capability, _healthy, status.Ready,
            status.PlayerOnline, status.SessionActive, _knownCapabilities);

    private bool CanInvokeSuicide() => CanInvokeLocalAction("player.suicide");

    private async Task SuicideAsync()
    {
        if (_suiciding || !CanInvokeSuicide()) return;
        var confirmedGeneration = _connectionGeneration;
        var confirmedProcessId = _lastStatus!.PlayerId;
        var confirmedPlayerEpoch = _client.PlayerEpoch;
        if (MessageBox.Show(this,
                "Definir a vida do personagem como zero? GodMode será desativado.",
                "Confirmar suicídio", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (_suiciding || !CanInvokeSuicide()) return;
        if (confirmedGeneration != _connectionGeneration || confirmedProcessId != _lastStatus!.PlayerId ||
            confirmedPlayerEpoch != _client.PlayerEpoch)
        {
            _playerActionStatus.Text = "A sessão local mudou. Confirme novamente a ação no personagem atual.";
            _playerActionStatus.ForeColor = Theme.Warning;
            _playerActionFeedbackUntilUtc = DateTimeOffset.UtcNow.AddSeconds(20);
            return;
        }

        var cancellation = _session!.Token;
        var generation = _connectionGeneration;
        var processId = _lastStatus!.PlayerId;
        var priorSettings = _settingsRequest;
        _suiciding = true;
        _settings.Player.GodMode = false;
        SettingsChanged();
        _debounce.Stop();
        _playerActionStatus.Text = "Desativando GodMode e aguardando a operação local…";
        _playerActionStatus.ForeColor = Theme.Muted;
        UpdateAvailability();
        try
        {
            // An older GodMode=true snapshot must finish before the action's
            // backend reset. New snapshots remain paused until the action ends.
            if (priorSettings is not null) await priorSettings.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (generation != _connectionGeneration || _closing) return;
            if (!CanInvokeSuicide() || _lastStatus?.PlayerId != processId || _client.PlayerEpoch != confirmedPlayerEpoch)
                throw new InvalidOperationException("A sessão local mudou; confirme novamente a ação no personagem atual.");
            await _client.SuicideAsync(confirmedPlayerEpoch, cancellation);
            if (generation != _connectionGeneration || _closing) return;
            const string message = "Vida zero observada; confirme a morte na cidade.";
            _playerActionStatus.Text = message;
            _playerActionStatus.ForeColor = Theme.Text;
            _playerActionFeedbackUntilUtc = DateTimeOffset.UtcNow.AddSeconds(20);
            _revisionLabel.Text = message;
            AppendLog(message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation != _connectionGeneration || _closing) return;
            var message = "Suicídio não confirmado: " + error.Message;
            _playerActionStatus.Text = message;
            _playerActionStatus.ForeColor = Theme.Warning;
            _playerActionFeedbackUntilUtc = DateTimeOffset.UtcNow.AddSeconds(20);
            _revisionLabel.Text = message;
            AppendLog(message);
        }
        finally
        {
            if (generation == _connectionGeneration)
            {
                _suiciding = false;
                if (!_closing)
                {
                    UpdateAvailability();
                    if (_dirty && _healthy && _client.DetectionEnabled && !_debounce.Enabled) _debounce.Start();
                }
            }
        }
    }

    private async Task DisconnectAsync()
    {
        if (_disconnecting) return;
        _disconnecting = true;
        _client.DetectionEnabled = false;
        _poll.Stop();
        _debounce.Stop();
        _session?.Cancel();
        ++_connectionGeneration;
        _connectionLabel.Text = "INTERROMPENDO…";
        UpdateAvailability();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await _client.DisconnectAsync(timeout.Token);
            AppendLog("O backend local encerrou a sessão de aplicação.");
        }
        catch (Exception error)
        {
            AppendLog("Encerramento local não confirmado: " + error.Message);
        }
        finally { if (!_closing) ClearSession(); }
    }

    private void ClearSession()
    {
        _poll.Stop();
        _debounce.Stop();
        _session?.Cancel();
        _session?.Dispose();
        _session = null;
        _lastStatus = null;
        UpdateFeatureStates(null);
        _healthy = false;
        _sending = false;
        _settingsRequest = null;
        _polling = false;
        _disconnecting = false;
        _suiciding = false;
        _playerActionFeedbackUntilUtc = DateTimeOffset.MinValue;
        _lastPlayerBlockReason = null;
        _playerActionStatus.Text = "Ações locais exigem uma sessão ativa. Suicídio pede confirmação.";
        _playerActionStatus.ForeColor = Theme.Muted;
        _vehicleActionStatus.Text = "Sem implementação externa nesta versão.";
        _vehicleActionStatus.ForeColor = Theme.Muted;
        _policy.Disconnect();
        ResetActiveEffects();
        _eventsCursor = 0;
        _connectionLabel.Text = "DETECÇÃO INTERROMPIDA";
        _connectionLabel.ForeColor = Theme.Muted;
        _connectionDetails.Text = "Clique em Detectar FiveM para procurar o processo local novamente.";
        _revisionLabel.Text = "Opções locais desativadas. Você pode preparar novas escolhas antes de retomar a detecção.";
        _process.Text = "Nenhum processo conectado";
        _serverLabel.Text = "";
        UpdateAvailability();
    }

    private void ResetActiveEffects()
    {
        if (!_dirty || _settings.Aim.Enabled || _settings.Aim.DrawFov || _settings.Silent.Enabled || _settings.Silent.DrawFov ||
            _settings.Weapons.NoRecoil || _settings.Weapons.NoSpread || _settings.Weapons.FreezeAmmo ||
            _settings.Player.GodMode || _settings.Player.Noclip || _settings.Player.Invisible ||
            _settings.Player.MuteVoice || _settings.Esp.Admins || _settings.Esp.Enabled)
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
        _settings.Esp.Admins = false;
        _settings.Esp.Enabled = false;
        SynchronizeToggles();
        _dirty = true;
    }

    private void FitToWorkingArea()
    {
        if (IsDisposed || !IsHandleCreated) return;
        var area = Screen.FromHandle(Handle).WorkingArea;
        var scale = DeviceDpi / 96d;
        MinimumSize = new Size(Math.Min(area.Width, (int)Math.Round(950 * scale)),
            Math.Min(area.Height, (int)Math.Round(660 * scale)));
        MaximumSize = area.Size;
        MaximizedBounds = area;
        if (WindowState != FormWindowState.Normal) return;
        var width = Math.Min(Width, area.Width);
        var height = Math.Min(Height, area.Height);
        Bounds = new Rectangle(Math.Clamp(Left, area.Left, area.Right - width),
            Math.Clamp(Top, area.Top, area.Bottom - height), width, height);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitToWorkingArea();
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

    private void SaveDiagnostic()
    {
        if (_closing) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Salvar diagnóstico do cliente",
            Filter = "Diagnóstico JSON (*.json)|*.json",
            FileName = "ZyonMenu-diagnostico.json",
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, _client.CaptureDiagnostics());
            AppendLog("Diagnóstico salvo no arquivo escolhido.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppendLog("Não foi possível salvar o diagnóstico: " + error.Message);
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        Enabled = false;
        _client.DetectionEnabled = false;
        _lifetime.Cancel();
        await DisconnectAsync();
        _session?.Cancel();
        _session?.Dispose();
        _debounce.Dispose();
        _poll.Dispose();
        _tooltips.Dispose();
        _client.Dispose();
        _lifetime.Dispose();
        Close();
    }
}
