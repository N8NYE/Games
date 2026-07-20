using Raylib_cs;
using SockerGame.Core.Enums;
using SockerGame.Core.Models;
using SockerGame.Core.Systems;
using SockerGame.Server;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const int ScreenWidth = 1280;
const int ScreenHeight = 720;
const string GameTitle = "Socker - Multiplayer Soccer";

GameState _gameState = new();
GameServer? _server = null;
TcpClient? _client = null;
NetworkStream? _stream = null;
string _localPlayerId = "";
string _localUsername = "";
InputState _input = new();
bool _isHost = false;
string _serverAddress = "localhost";
int _serverPort = 25565;
string _lobbyPassword = "";
string _lobbyName = "Socker Lobby";
string _statusMessage = "";
float _statusTimer = 0;
bool _showPasswordField = false;
string _passwordInput = "";
string _usernameInput = "";
bool _showJerseyEditor = false;
bool _showPlayerCustomization = false;
bool _showStatsEditor = false;
bool _showSubstitutionMenu = false;
bool _showPauseMenu = false;
bool _showQuitConfirm = false;
int _selectedPixelColor = 0;
int[,] _jerseyPixels = new int[32, 32];
Color[] _pixelPalette = new[] {
    Color.Red, Color.Blue, Color.Green, Color.Yellow,
    Color.White, Color.Black, Color.Orange, Color.Purple,
    Color.Pink, Color.Brown, Color.Gray, Color.Lime
};
int _selectedHairStyle = 0;
int _selectedHairColor = 0;
int _selectedEyeColor = 0;
int _selectedSkinTone = 0;
int _selectedFacialHair = 0;
int _cameraOffsetX = 0;
int _cameraOffsetY = 0;
float _voiceVolumeTeam = 1.0f;
float _voiceVolumeOpponent = 0.5f;
string _chatInput = "";
List<string> _chatMessages = new();
bool _showChat = false;
bool _isFullscreen = true;
float _clickCooldown = 0;
int _selectedPosition = 2;
bool _showHostDialog = false;
string _hostPasswordInput = "";
string _hostLobbyNameInput = "Socker Lobby";
bool _hostFocusName = true;
bool _joinFocusAddress = true;
int _selectedTeam = 0;
int _subPlayerOffIndex = -1;
int _subPlayerOnIndex = -1;
float _statRepeatTimer = 0;
int _statRepeatIndex = -1;
int _statRepeatDirection = 0; // -1 for decrease, 1 for increase
float _statRepeatDelay = 0.3f; // initial delay before repeat starts
float _statRepeatRate = 0.08f; // repeat rate after initial delay
float _kickInputDelay = 0f; // prevents click-through on game start
int _pendingAiTeam = 0; // which team to add AI to (0=home, 1=away)

Raylib.InitWindow(ScreenWidth, ScreenHeight, GameTitle);
Raylib.SetTargetFPS(60);
Raylib.SetExitKey(KeyboardKey.Null);
{
    int monitor = Raylib.GetCurrentMonitor();
    Raylib.SetWindowSize(Raylib.GetMonitorWidth(monitor), Raylib.GetMonitorHeight(monitor));
    Raylib.ToggleFullscreen();
}
_usernameInput = $"Player{Random.Shared.Next(1000, 9999)}";

while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();
    _clickCooldown = Math.Max(0, _clickCooldown - dt);
    Update(dt);
    Draw();
}

_server?.Stop();
_client?.Close();
Raylib.CloseWindow();

void Update(float dt)
{
    _statusTimer -= dt;

    if (Raylib.IsKeyPressed(KeyboardKey.F11))
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            int monitor = Raylib.GetCurrentMonitor();
            Raylib.SetWindowSize(Raylib.GetMonitorWidth(monitor), Raylib.GetMonitorHeight(monitor));
            Raylib.ToggleFullscreen();
        }
        else
        {
            Raylib.ToggleFullscreen();
            Raylib.SetWindowSize(ScreenWidth, ScreenHeight);
        }
    }

    if (Raylib.IsKeyPressed(KeyboardKey.Escape))
    {
        if (_gameState.CurrentPhase == GamePhase.Lobby)
        {
            _client?.Close();
            _server?.Stop();
            _gameState.CurrentPhase = GamePhase.MainMenu;
            _showPauseMenu = false;
            _showQuitConfirm = false;
        }
        else if (_gameState.CurrentPhase >= GamePhase.Kickoff && _gameState.CurrentPhase < GamePhase.FullTime)
        {
            if (_showQuitConfirm)
            {
                _showQuitConfirm = false;
                _showPauseMenu = true;
            }
            else if (_showPauseMenu)
            {
                _showPauseMenu = false;
            }
            else
            {
                _showPauseMenu = true;
            }
        }
    }

    switch (_gameState.CurrentPhase)
    {
        case GamePhase.MainMenu: UpdateMainMenu(); break;
        case GamePhase.Lobby: UpdateLobby(dt); break;
        default: UpdateMatch(dt); break;
    }

    // CRITICAL: Always send input to server if connected
    if (!_isHost && _stream != null && _gameState.CurrentPhase >= GamePhase.Kickoff)
    {
        SendMessage(new NetworkMessage
        {
            Type = NetworkMessageType.PlayerInput,
            SenderId = _localPlayerId,
            Payload = JsonSerializer.Serialize(_input)
        });
    }

    // CRITICAL: Update host match engine with input
    if (_isHost && _server != null && _server.IsRunning && _server.MatchEngine != null)
    {
        var playerInputs = new Dictionary<string, InputState>();
        playerInputs[_localPlayerId] = _input;
        _server.MatchEngine.Update(dt, playerInputs);
    }

    // CRITICAL: Sync match state from server (for clients) or local server (for host)
    if (_isHost && _server != null && _server.GameState.CurrentMatch != null)
    {
        _gameState.CurrentMatch = _server.GameState.CurrentMatch;
    }
}

void UpdateMainMenu()
{
    var mousePos = Raylib.GetMousePosition();

    if (_showHostDialog)
    {
        int ox = ScreenWidth / 2, oy = 200;
        Rectangle nameField = new(ox - 200, oy + 95, 400, 35);
        Rectangle passField = new(ox - 200, oy + 175, 400, 35);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, nameField))
            _hostFocusName = true;
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, passField))
            _hostFocusName = false;

        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if (_hostFocusName && _hostLobbyNameInput.Length < 30 && key >= 32 && key <= 126)
                _hostLobbyNameInput += (char)key;
            else if (!_hostFocusName && _hostPasswordInput.Length < 20 && key >= 32 && key <= 126)
                _hostPasswordInput += (char)key;
            key = Raylib.GetCharPressed();
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace))
        {
            if (_hostFocusName && _hostLobbyNameInput.Length > 0) _hostLobbyNameInput = _hostLobbyNameInput[..^1];
            else if (!_hostFocusName && _hostPasswordInput.Length > 0) _hostPasswordInput = _hostPasswordInput[..^1];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) _hostFocusName = !_hostFocusName;

        Rectangle createBtn = new(ox - 100, oy + 240, 200, 45);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, createBtn))
        {
            _lobbyName = _hostLobbyNameInput;
            _lobbyPassword = _hostPasswordInput;
            StartHosting();
        }
        Rectangle cancelBtn = new(ox - 100, oy + 295, 200, 40);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, cancelBtn))
            _showHostDialog = false;
        return;
    }

    if (_showPasswordField)
    {
        int ox = ScreenWidth / 2, oy = 200;
        Rectangle addrField = new(ox - 200, oy + 95, 400, 35);
        Rectangle passField = new(ox - 200, oy + 175, 400, 35);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, addrField))
            _joinFocusAddress = true;
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, passField))
            _joinFocusAddress = false;

        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if (_joinFocusAddress && _serverAddress.Length < 50 && key >= 32 && key <= 126)
                _serverAddress += (char)key;
            else if (!_joinFocusAddress && _passwordInput.Length < 20 && key >= 32 && key <= 126)
                _passwordInput += (char)key;
            key = Raylib.GetCharPressed();
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace))
        {
            if (_joinFocusAddress && _serverAddress.Length > 0) _serverAddress = _serverAddress[..^1];
            else if (!_joinFocusAddress && _passwordInput.Length > 0) _passwordInput = _passwordInput[..^1];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) _joinFocusAddress = !_joinFocusAddress;

        Rectangle connectBtn = new(ox - 100, oy + 240, 200, 45);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, connectBtn))
            ConnectToServer();
        Rectangle cancelBtn = new(ox - 100, oy + 295, 200, 40);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, cancelBtn))
            _showPasswordField = false;
        return;
    }

    int key3 = Raylib.GetCharPressed();
    while (key3 > 0)
    {
        if (_usernameInput.Length < 20 && key3 >= 32 && key3 <= 126) _usernameInput += (char)key3;
        key3 = Raylib.GetCharPressed();
    }
    if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _usernameInput.Length > 0)
        _usernameInput = _usernameInput[..^1];

    Rectangle hostBtn = new(ScreenWidth / 2 - 150, 340, 300, 55);
    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, hostBtn))
        _showHostDialog = true;
    Rectangle joinBtn = new(ScreenWidth / 2 - 150, 410, 300, 55);
    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, joinBtn))
        _showPasswordField = true;
}

void StartHosting()
{
    _isHost = true;
    _localPlayerId = Guid.NewGuid().ToString();
    _server = new GameServer();
    _server.OnLog += (msg) => Console.WriteLine($"[Server] {msg}");
    _server.Start(_serverPort);
    _server.CreateLobby(_lobbyName, _lobbyPassword, _localPlayerId, _usernameInput);
    _gameState = _server.GameState;
    _gameState.LocalPlayer = _gameState.Players.Find(p => p.Id == _localPlayerId);
    if (_gameState.LocalPlayer != null)
        _gameState.LocalPlayer.Position = (PlayerPosition)_selectedPosition;
    _gameState.CurrentPhase = GamePhase.Lobby;
    _statusMessage = $"Server started! IP: {_server.ExternalIp}:{_serverPort}";
    _statusTimer = 5.0f;
    _showHostDialog = false;
}

void ConnectToServer()
{
    try
    {
        _client = new TcpClient();
        _client.Connect(_serverAddress, _serverPort);
        _stream = _client.GetStream();
        _localPlayerId = Guid.NewGuid().ToString();
        _isHost = false;
        SendMessage(new NetworkMessage
        {
            Type = NetworkMessageType.JoinLobby,
            SenderId = _localPlayerId,
            Payload = JsonSerializer.Serialize(new JoinLobbyData { Username = _usernameInput, Password = _passwordInput })
        });
        _gameState.CurrentPhase = GamePhase.Lobby;
        _statusMessage = "Connected to server!";
        _statusTimer = 3.0f;
        _showPasswordField = false;
        _ = Task.Run(() => ListenForMessages());
    }
    catch (Exception ex)
    {
        _statusMessage = $"Connection failed: {ex.Message}";
        _statusTimer = 3.0f;
    }
}

async Task ListenForMessages()
{
    var buffer = new byte[65536];
    while (_client?.Connected == true && _stream != null)
    {
        try
        {
            if (!_stream.DataAvailable) { await Task.Delay(16); continue; }
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;
            foreach (var msg in Encoding.UTF8.GetString(buffer, 0, bytesRead).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try { var networkMsg = JsonSerializer.Deserialize<NetworkMessage>(msg); if (networkMsg != null) HandleNetworkMessage(networkMsg); }
                catch { }
            }
        }
        catch { break; }
    }
}

void HandleNetworkMessage(NetworkMessage message)
{
    switch (message.Type)
    {
        case NetworkMessageType.LobbyUpdate: HandleLobbyUpdate(message.Payload); break;
        case NetworkMessageType.GameStart: HandleGameStart(message.Payload); break;
        case NetworkMessageType.GameStateUpdate: HandleGameStateUpdate(message.Payload); break;
    }
}

void HandleLobbyUpdate(string payload)
{
    try { _statusMessage = "Lobby updated"; _statusTimer = 2.0f; }
    catch { }
}

void HandleGameStart(string payload)
{
    try { _gameState.CurrentPhase = GamePhase.Kickoff; _statusMessage = "Game starting!"; _statusTimer = 3.0f; }
    catch { }
}

void HandleGameStateUpdate(string payload)
{
    try
    {
        var data = JsonSerializer.Deserialize<JsonElement>(payload);
        if (_gameState.CurrentMatch == null) return;
        if (data.TryGetProperty("HomeScore", out var hs)) _gameState.CurrentMatch.HomeScore = hs.GetInt32();
        if (data.TryGetProperty("AwayScore", out var aws)) _gameState.CurrentMatch.AwayScore = aws.GetInt32();
        if (data.TryGetProperty("MatchMinute", out var mm)) _gameState.CurrentMatch.MatchMinute = mm.GetInt32();
        if (data.TryGetProperty("MatchSecond", out var ms)) _gameState.CurrentMatch.MatchSecond = ms.GetInt32();
        if (data.TryGetProperty("CurrentHalf", out var ch)) _gameState.CurrentMatch.CurrentHalf = (GamePhase)ch.GetInt32();
        if (data.TryGetProperty("Ball", out var ball))
        {
            _gameState.CurrentMatch.Ball.X = ball.GetProperty("X").GetSingle();
            _gameState.CurrentMatch.Ball.Y = ball.GetProperty("Y").GetSingle();
        }
        if (data.TryGetProperty("Players", out var players))
        {
            foreach (var p in players.EnumerateArray())
            {
                string id = p.GetProperty("Id").GetString() ?? "";
                var player = _gameState.Players.Find(pl => pl.Id == id);
                if (player != null)
                {
                    player.X = p.GetProperty("X").GetSingle();
                    player.Y = p.GetProperty("Y").GetSingle();
                    player.HasBall = p.GetProperty("HasBall").GetBoolean();
                    player.Direction = p.GetProperty("Direction").GetSingle();
                    player.Speed = p.GetProperty("Speed").GetSingle();
                }
            }
        }
    }
    catch { }
}

void SendMessage(NetworkMessage message)
{
    if (_stream == null) return;
    try
    {
        var json = JsonSerializer.Serialize(message) + "\n";
        var data = Encoding.UTF8.GetBytes(json);
        _stream.Write(data, 0, data.Length);
        _stream.Flush();
    }
    catch { }
}

void LoadTeamJerseyForEditing()
{
    _jerseyPixels = new int[32, 32];
    if (_gameState.LocalPlayer != null)
    {
        var team = _gameState.LocalPlayer.Team == TeamSide.Home ? _gameState.Teams.GetValueOrDefault("home") : _gameState.Teams.GetValueOrDefault("away");
        if (team?.Jersey?.PixelArt != null)
        {
            Array.Copy(team.Jersey.PixelArt, _jerseyPixels, team.Jersey.PixelArt.Length);
        }
        else
        {
            string primaryColor = team?.Jersey?.PrimaryColor ?? "#FF0000";
            int colorIndex = GetColorIndexFromHex(primaryColor);
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 32; y++)
                {
                    _jerseyPixels[x, y] = Array.IndexOf(_pixelPalette, Color.White);
                    if (x < 8 || x > 23) _jerseyPixels[x, y] = colorIndex;
                }
            }
        }
    }
}

int GetColorIndexFromHex(string hex)
{
    return hex.ToUpper() switch
    {
        "#FF0000" => Array.IndexOf(_pixelPalette, Color.Red),
        "#0000FF" => Array.IndexOf(_pixelPalette, Color.Blue),
        "#00FF00" => Array.IndexOf(_pixelPalette, Color.Lime),
        "#FFFF00" => Array.IndexOf(_pixelPalette, Color.Yellow),
        "#FFFFFF" => Array.IndexOf(_pixelPalette, Color.White),
        "#000000" => Array.IndexOf(_pixelPalette, Color.Black),
        "#FFA500" => Array.IndexOf(_pixelPalette, Color.Orange),
        "#800080" => Array.IndexOf(_pixelPalette, Color.Purple),
        "#FFC0CB" => Array.IndexOf(_pixelPalette, Color.Pink),
        "#A52A2A" => Array.IndexOf(_pixelPalette, Color.Brown),
        "#808080" => Array.IndexOf(_pixelPalette, Color.Gray),
        _ => Array.IndexOf(_pixelPalette, Color.Red)
    };
}

void UpdateLobby(float dt)
{
    var mousePos = Raylib.GetMousePosition();
    
    if (_showJerseyEditor || _showPlayerCustomization || _showStatsEditor)
    {
        if (_showJerseyEditor) UpdateJerseyEditor();
        if (_showPlayerCustomization) UpdatePlayerCustomization();
        if (_showStatsEditor) UpdateStatsEditor();
        return;
    }
    
    if (_gameState.CurrentPhase != GamePhase.Lobby) _showStatsEditor = false;

    // Handle team/position selection for local player (controls at rightX=580)
    string[] teamNames = { "Home", "Away" };
    int rightX = 580; // Controls section starts after player list elements + padding
    
    if (Raylib.IsMouseButtonPressed(MouseButton.Left))
    {
        Rectangle teamLeft = new(rightX, 185, 30, 25);
        Rectangle teamRight = new(rightX + 110, 185, 30, 25);
        if (Raylib.CheckCollisionPointRec(mousePos, teamLeft)) _selectedTeam = (_selectedTeam - 1 + 2) % 2;
        if (Raylib.CheckCollisionPointRec(mousePos, teamRight)) _selectedTeam = (_selectedTeam + 1) % 2;
        
        Rectangle posLeft = new(rightX, 250, 30, 25);
        Rectangle posRight = new(rightX + 110, 250, 30, 25);
        if (Raylib.CheckCollisionPointRec(mousePos, posLeft)) _selectedPosition = (_selectedPosition - 1 + 5) % 5;
        if (Raylib.CheckCollisionPointRec(mousePos, posRight)) _selectedPosition = (_selectedPosition + 1) % 5;
    }
    
    if (_gameState.LocalPlayer != null)
    {
        _gameState.LocalPlayer.Team = _selectedTeam == 0 ? TeamSide.Home : TeamSide.Away;
        _gameState.LocalPlayer.Position = (PlayerPosition)_selectedPosition;
    }
}

void UpdateJerseyEditor()
{
    var mousePos = Raylib.GetMousePosition();
    int pixelSize = 10, startX = ScreenWidth / 2 - 160, startY = ScreenHeight / 2 - 160;
    if (Raylib.IsMouseButtonDown(MouseButton.Left))
    {
        int px = (int)((mousePos.X - startX) / pixelSize), py = (int)((mousePos.Y - startY) / pixelSize);
        if (px >= 0 && px < 32 && py >= 0 && py < 32) _jerseyPixels[px, py] = _selectedPixelColor;
    }
    if (Raylib.IsMouseButtonDown(MouseButton.Right))
    {
        int px = (int)((mousePos.X - startX) / pixelSize), py = (int)((mousePos.Y - startY) / pixelSize);
        if (px >= 0 && px < 32 && py >= 0 && py < 32) _jerseyPixels[px, py] = -1;
    }
    for (int i = 0; i < _pixelPalette.Length; i++)
    {
        int paletteX = 50 + i * 30, paletteY = ScreenHeight - 100;
        var rect = new Rectangle(paletteX, paletteY, 25, 25);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, rect))
            _selectedPixelColor = i;
    }
    Rectangle closeBtn = new(ScreenWidth / 2 - 50, ScreenHeight - 40, 100, 30);
    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, closeBtn))
    {
        if (_gameState.LocalPlayer != null && _gameState.CurrentPhase == GamePhase.Lobby)
        {
            var team = _gameState.LocalPlayer.Team == TeamSide.Home ? _gameState.Teams.GetValueOrDefault("home") : _gameState.Teams.GetValueOrDefault("away");
            if (team != null)
            {
                team.Jersey.PixelArt = new int[32, 32];
                Array.Copy(_jerseyPixels, team.Jersey.PixelArt, _jerseyPixels.Length);
            }
        }
        _showJerseyEditor = false;
    }
}

void UpdatePlayerCustomization()
{
    var mousePos = Raylib.GetMousePosition();
    int cx = ScreenWidth / 2, startY = 160;
    int[] values = new[] { _selectedHairStyle, _selectedHairColor, _selectedEyeColor, _selectedSkinTone, _selectedFacialHair };
    int[] maxValues = new[] { 5, 8, 5, 5, 5 };
    for (int row = 0; row < 5; row++)
    {
        int btnY = startY + row * 32;
        Rectangle leftArrow = new(cx - 210, btnY, 30, 25);
        Rectangle rightArrow = new(cx + 170, btnY, 30, 25);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (Raylib.CheckCollisionPointRec(mousePos, leftArrow)) values[row] = (values[row] - 1 + maxValues[row]) % maxValues[row];
            if (Raylib.CheckCollisionPointRec(mousePos, rightArrow)) values[row] = (values[row] + 1) % maxValues[row];
        }
    }
    _selectedHairStyle = values[0]; _selectedHairColor = values[1]; _selectedEyeColor = values[2]; _selectedSkinTone = values[3]; _selectedFacialHair = values[4];
    Rectangle closeBtn = new(ScreenWidth / 2 - 50, ScreenHeight - 40, 100, 30);
    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, closeBtn))
        _showPlayerCustomization = false;
}

void UpdateStatsEditor()
{
    if (_gameState.CurrentPhase != GamePhase.Lobby) return;
    var mousePos = Raylib.GetMousePosition();
    var statNames = PlayerStats.StatNames;
    
    int baseY = 150;
    int statSpacing = 25;
    
    if (_statRepeatIndex >= 0 && _statRepeatIndex < statNames.Length)
    {
        _statRepeatTimer -= Raylib.GetFrameTime();
        if (_statRepeatTimer <= 0)
        {
            if (_statRepeatDirection == -1)
            {
                int currentVal = _gameState.LocalPlayer.Stats.GetStat(statNames[_statRepeatIndex]);
                if (currentVal > 0)
                    _gameState.LocalPlayer.Stats.TrySetStat(statNames[_statRepeatIndex], currentVal - 1);
            }
            else if (_statRepeatDirection == 1)
            {
                int currentVal = _gameState.LocalPlayer.Stats.GetStat(statNames[_statRepeatIndex]);
                if (currentVal < 100 && _gameState.LocalPlayer.Stats.StatPointsRemaining > 0)
                    _gameState.LocalPlayer.Stats.TrySetStat(statNames[_statRepeatIndex], currentVal + 1);
            }
            _statRepeatTimer = _statRepeatRate;
        }
        
        bool stillHeld = Raylib.IsMouseButtonDown(MouseButton.Left);
        if (!stillHeld)
        {
            _statRepeatIndex = -1;
            _statRepeatDirection = 0;
        }
    }
    
    if (Raylib.IsMouseButtonPressed(MouseButton.Left))
    {
        for (int i = 0; i < statNames.Length; i++)
        {
            int btnY = baseY + i * statSpacing;
            Rectangle decBtn = new(ScreenWidth / 2 + 255, btnY, 25, 24);
            if (Raylib.CheckCollisionPointRec(mousePos, decBtn))
            {
                if (_gameState.LocalPlayer != null)
                {
                    int currentVal = _gameState.LocalPlayer.Stats.GetStat(statNames[i]);
                    if (currentVal > 0)
                    {
                        _gameState.LocalPlayer.Stats.TrySetStat(statNames[i], currentVal - 1);
                        _statRepeatIndex = i;
                        _statRepeatDirection = -1;
                        _statRepeatTimer = _statRepeatDelay;
                    }
                }
                break;
            }
            Rectangle incBtn = new(ScreenWidth / 2 + 285, btnY, 25, 24);
            if (Raylib.CheckCollisionPointRec(mousePos, incBtn))
            {
                if (_gameState.LocalPlayer != null)
                {
                    int currentVal = _gameState.LocalPlayer.Stats.GetStat(statNames[i]);
                    if (currentVal < 100 && _gameState.LocalPlayer.Stats.StatPointsRemaining > 0)
                    {
                        _gameState.LocalPlayer.Stats.TrySetStat(statNames[i], currentVal + 1);
                        _statRepeatIndex = i;
                        _statRepeatDirection = 1;
                        _statRepeatTimer = _statRepeatDelay;
                    }
                }
                break;
            }
        }
    }
    
    Rectangle closeBtn = new(ScreenWidth / 2 - 50, ScreenHeight - 40, 100, 30);
    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, closeBtn))
        _showStatsEditor = false;
}

void StartGame()
{
    if (_isHost && _server != null)
    {
        _ = _server.StartGame();
        _gameState = _server.GameState;
        _gameState.LocalPlayer = _gameState.Players.Find(p => p.Id == _localPlayerId);
        _gameState.CurrentPhase = GamePhase.Kickoff;
        _kickInputDelay = 0.5f; // 0.5 second delay before any kick input is accepted
        
        if (_gameState.CurrentMatch != null)
        {
            foreach (var (teamId, players) in _gameState.CurrentMatch.TeamPlayers)
            {
                foreach (var player in players)
                {
                    player.TargetX = player.X;
                    player.TargetY = player.Y;
                    player.Speed = 0;
                }
            }
        }
    }
}

void UpdateMatch(float dt)
{
    // CRITICAL: Calculate camera offset FIRST before gathering input (so mouse position is correct)
    if (_gameState.CurrentMatch != null)
    {
        int actualScreenWidth = Raylib.GetScreenWidth();
        int actualScreenHeight = Raylib.GetScreenHeight();
        _cameraOffsetX = (int)(PitchDimensions.CenterX - actualScreenWidth / 2);
        _cameraOffsetY = (int)(PitchDimensions.CenterY - actualScreenHeight / 2);
    }

    // Countdown kick delay after game starts to prevent click-through
    if (_kickInputDelay > 0)
    {
        _kickInputDelay -= dt;
        if (_kickInputDelay < 0) _kickInputDelay = 0;
    }

    if (_gameState.LocalPlayer != null)
    {
        _input.MoveX = 0; _input.MoveY = 0;
        if (Raylib.IsKeyDown(KeyboardKey.W)) _input.MoveY = -1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) _input.MoveY = 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) _input.MoveX = -1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) _input.MoveX = 1;
        var mousePos = Raylib.GetMousePosition();
        _input.MouseX = mousePos.X + _cameraOffsetX;
        _input.MouseY = mousePos.Y + _cameraOffsetY;
        // Block kick inputs during initial delay to prevent click-through from start button
        _input.LeftClick = _kickInputDelay <= 0 && Raylib.IsMouseButtonPressed(MouseButton.Left);
        _input.RightClick = _kickInputDelay <= 0 && Raylib.IsMouseButtonPressed(MouseButton.Right);
        _input.LeftClickHeld = _kickInputDelay <= 0 && Raylib.IsMouseButtonDown(MouseButton.Left);
        _input.RightClickHeld = _kickInputDelay <= 0 && Raylib.IsMouseButtonDown(MouseButton.Right);
        _input.ShiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift);
        _input.SpaceHeld = Raylib.IsKeyDown(KeyboardKey.Space);
        
        if (_input.ShiftHeld && (_input.MoveX != 0 || _input.MoveY != 0))
        {
            float sprintMultiplier = 1.5f + (_gameState.LocalPlayer.Stats.Speed / 100f) * 1.0f;
            _input.MoveX *= sprintMultiplier;
            _input.MoveY *= sprintMultiplier;
        }
    }
    
    if (_showQuitConfirm)
    {
        var mp = Raylib.GetMousePosition();
        Rectangle yesBtn = new(ScreenWidth / 2 - 110, ScreenHeight / 2 + 20, 100, 40);
        Rectangle noBtn = new(ScreenWidth / 2 + 10, ScreenHeight / 2 + 20, 100, 40);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (Raylib.CheckCollisionPointRec(mp, yesBtn))
            {
                _showQuitConfirm = false;
                _showPauseMenu = false;
                _gameState.CurrentPhase = GamePhase.Lobby;
                _showSubstitutionMenu = false;
            }
            if (Raylib.CheckCollisionPointRec(mp, noBtn))
            {
                _showQuitConfirm = false;
                _showPauseMenu = true;
            }
        }
        return;
    }

    if (_showPauseMenu)
    {
        var mp = Raylib.GetMousePosition();
        Rectangle resumeBtn = new(ScreenWidth / 2 - 100, ScreenHeight / 2 - 60, 200, 40);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, resumeBtn))
            _showPauseMenu = false;
        Rectangle subBtn = new(ScreenWidth / 2 - 100, ScreenHeight / 2, 200, 40);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, subBtn))
        {
            if (_gameState.LocalPlayer?.IsCoach == true) _showSubstitutionMenu = !_showSubstitutionMenu;
            _showPauseMenu = false;
        }
        Rectangle quitBtn = new(ScreenWidth / 2 - 100, ScreenHeight / 2 + 60, 200, 40);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, quitBtn))
        {
            _showPauseMenu = false;
            _showQuitConfirm = true;
        }
    }

    if (Raylib.IsKeyPressed(KeyboardKey.U) && _gameState.LocalPlayer?.IsCoach == true && !_showPauseMenu && !_showQuitConfirm)
    {
        _showSubstitutionMenu = !_showSubstitutionMenu;
        if (_showSubstitutionMenu) { _subPlayerOffIndex = -1; _subPlayerOnIndex = -1; }
    }

    if (Raylib.IsKeyPressed(KeyboardKey.Enter))
    {
        _showChat = !_showChat;
        if (!_showChat && !string.IsNullOrEmpty(_chatInput)) { _chatMessages.Add($"{_localUsername}: {_chatInput}"); _chatInput = ""; }
    }
    if (_showChat)
    {
        int key = Raylib.GetCharPressed();
        while (key > 0) { if (_chatInput.Length < 100 && key >= 32 && key <= 126) _chatInput += (char)key; key = Raylib.GetCharPressed(); }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _chatInput.Length > 0) _chatInput = _chatInput[..^1];
    }
    if (Raylib.IsKeyPressed(KeyboardKey.PageUp)) _voiceVolumeTeam = Math.Min(1.0f, _voiceVolumeTeam + 0.1f);
    if (Raylib.IsKeyPressed(KeyboardKey.PageDown)) _voiceVolumeTeam = Math.Max(0, _voiceVolumeTeam - 0.1f);
    if (Raylib.IsKeyPressed(KeyboardKey.Home)) _voiceVolumeOpponent = Math.Min(1.0f, _voiceVolumeOpponent + 0.1f);
    if (Raylib.IsKeyPressed(KeyboardKey.End)) _voiceVolumeOpponent = Math.Max(0, _voiceVolumeOpponent - 0.1f);
}

void Draw()
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DarkGreen);
    switch (_gameState.CurrentPhase)
    {
        case GamePhase.MainMenu: DrawMainMenu(); break;
        case GamePhase.Lobby: DrawLobby(); break;
        default: DrawMatch(); break;
    }
    if (_statusTimer > 0)
    {
        int alpha = Math.Clamp((int)(_statusTimer / 3.0f * 255), 0, 255);
        Raylib.DrawText(_statusMessage, ScreenWidth / 2 - Raylib.MeasureText(_statusMessage, 20) / 2, ScreenHeight - 50, 20, new Color(255, 255, 255, alpha));
    }
    Raylib.DrawFPS(10, 10);
    Raylib.EndDrawing();
}

void DrawMainMenu()
{
    Color fieldGreen = new Color(34, 139, 34, 255);
    Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, fieldGreen);
    for (int i = 0; i < 10; i++) Raylib.DrawRectangle(0, i * 80, ScreenWidth, 40, new Color(30, 125, 30, 100));
    Raylib.DrawCircleLines(ScreenWidth / 2, ScreenHeight / 2, 120, Color.White);
    Raylib.DrawCircle(ScreenWidth / 2, ScreenHeight / 2, 3, Color.White);
    Raylib.DrawLine(ScreenWidth / 2, 0, ScreenWidth / 2, ScreenHeight, Color.White);
    Raylib.DrawRectangleLines(ScreenWidth / 2 - 200, ScreenHeight / 2 - 150, 200, 300, Color.White);
    Raylib.DrawRectangleLines(ScreenWidth / 2, ScreenHeight / 2 - 150, 200, 300, Color.White);
    Raylib.DrawRectangleLines(ScreenWidth / 2 - 80, ScreenHeight / 2 - 100, 80, 200, Color.White);
    Raylib.DrawRectangleLines(ScreenWidth / 2, ScreenHeight / 2 - 100, 80, 200, Color.White);
    int cornerR = 20;
    Raylib.DrawCircleSector(new System.Numerics.Vector2(0, 0), cornerR, 0, 90, 20, Color.White);
    Raylib.DrawCircleSector(new System.Numerics.Vector2(ScreenWidth, 0), cornerR, 90, 180, 20, Color.White);
    Raylib.DrawCircleSector(new System.Numerics.Vector2(0, ScreenHeight), cornerR, 270, 360, 20, Color.White);
    Raylib.DrawCircleSector(new System.Numerics.Vector2(ScreenWidth, ScreenHeight), cornerR, 180, 270, 20, Color.White);
    Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 120));
    int titleSize = 72;
    string title = "SOCKER";
    int titleX = ScreenWidth / 2 - Raylib.MeasureText(title, titleSize) / 2;
    Raylib.DrawText(title, titleX + 3, 103, titleSize, Color.Black);
    Raylib.DrawText(title, titleX, 100, titleSize, Color.White);
    Raylib.DrawText("MULTIPLAYER SOCCER", ScreenWidth / 2 - Raylib.MeasureText("MULTIPLAYER SOCCER", 24) / 2, 175, 24, new Color(255, 255, 200, 255));
    Raylib.DrawText("PLAYER NAME", ScreenWidth / 2 - 200, 240, 18, new Color(200, 200, 200, 255));
    Raylib.DrawRectangle(ScreenWidth / 2 - 200, 265, 400, 40, new Color(0, 0, 0, 200));
    Raylib.DrawRectangleLines(ScreenWidth / 2 - 200, 265, 400, 40, Color.White);
    Raylib.DrawText(_usernameInput, ScreenWidth / 2 - 195, 273, 22, Color.White);
    var mousePos = Raylib.GetMousePosition();
    Rectangle hostBtn = new(ScreenWidth / 2 - 150, 340, 300, 55);
    Color hostColor = Raylib.CheckCollisionPointRec(mousePos, hostBtn) ? new Color(0, 130, 230, 255) : new Color(0, 100, 200, 255);
    Raylib.DrawRectangleRounded(hostBtn, 0.15f, 10, hostColor);
    Raylib.DrawRectangleRoundedLines(hostBtn, 0.15f, 10, Color.White);
    Raylib.DrawText("HOST GAME", ScreenWidth / 2 - Raylib.MeasureText("HOST GAME", 24) / 2, 355, 24, Color.White);
    Rectangle joinBtn = new(ScreenWidth / 2 - 150, 410, 300, 55);
    Color joinColor = Raylib.CheckCollisionPointRec(mousePos, joinBtn) ? new Color(0, 180, 70, 255) : new Color(0, 150, 50, 255);
    Raylib.DrawRectangleRounded(joinBtn, 0.15f, 10, joinColor);
    Raylib.DrawRectangleRoundedLines(joinBtn, 0.15f, 10, Color.White);
    Raylib.DrawText("JOIN GAME", ScreenWidth / 2 - Raylib.MeasureText("JOIN GAME", 24) / 2, 425, 24, Color.White);

    if (_showHostDialog)
    {
        Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 200));
        int ox = ScreenWidth / 2, oy = 200;
        Raylib.DrawRectangle(ox - 250, oy, 500, 350, new Color(30, 30, 30, 240));
        Raylib.DrawRectangleLines(ox - 250, oy, 500, 350, Color.White);
        Raylib.DrawText("HOST GAME", ox - Raylib.MeasureText("HOST GAME", 28) / 2, oy + 20, 28, Color.White);
        Raylib.DrawText("Lobby Name:", ox - 200, oy + 70, 18, Color.LightGray);
        Raylib.DrawRectangle(ox - 200, oy + 95, 400, 35, Color.Black);
        Raylib.DrawRectangleLines(ox - 200, oy + 95, 400, 35, _hostFocusName ? Color.Yellow : Color.White);
        Raylib.DrawText(_hostLobbyNameInput, ox - 195, oy + 102, 20, Color.White);
        Raylib.DrawText("Password (optional):", ox - 200, oy + 150, 18, Color.LightGray);
        Raylib.DrawRectangle(ox - 200, oy + 175, 400, 35, Color.Black);
        Raylib.DrawRectangleLines(ox - 200, oy + 175, 400, 35, !_hostFocusName ? Color.Yellow : Color.White);
        Raylib.DrawText(new string('*', _hostPasswordInput.Length), ox - 195, oy + 182, 20, Color.White);
        Rectangle createBtn = new(ox - 100, oy + 240, 200, 45);
        Color createColor = Raylib.CheckCollisionPointRec(mousePos, createBtn) ? new Color(0, 180, 70, 255) : new Color(0, 150, 50, 255);
        Raylib.DrawRectangleRounded(createBtn, 0.15f, 10, createColor);
        Raylib.DrawRectangleRoundedLines(createBtn, 0.15f, 10, Color.White);
        Raylib.DrawText("CREATE", ox - Raylib.MeasureText("CREATE", 22) / 2, oy + 252, 22, Color.White);
        Rectangle cancelBtn = new(ox - 100, oy + 295, 200, 40);
        Color cancelColor = Raylib.CheckCollisionPointRec(mousePos, cancelBtn) ? new Color(180, 70, 70, 255) : new Color(150, 50, 50, 255);
        Raylib.DrawRectangleRounded(cancelBtn, 0.15f, 10, cancelColor);
        Raylib.DrawRectangleRoundedLines(cancelBtn, 0.15f, 10, Color.White);
        Raylib.DrawText("CANCEL", ox - Raylib.MeasureText("CANCEL", 20) / 2, oy + 305, 20, Color.White);
    }
    if (_showPasswordField)
    {
        Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 200));
        int ox = ScreenWidth / 2, oy = 200;
        Raylib.DrawRectangle(ox - 250, oy, 500, 350, new Color(30, 30, 30, 240));
        Raylib.DrawRectangleLines(ox - 250, oy, 500, 350, Color.White);
        Raylib.DrawText("CONNECT TO SERVER", ox - Raylib.MeasureText("CONNECT TO SERVER", 28) / 2, oy + 20, 28, Color.White);
        Raylib.DrawText("Server Address:", ox - 200, oy + 70, 18, Color.LightGray);
        Raylib.DrawRectangle(ox - 200, oy + 95, 400, 35, Color.Black);
        Raylib.DrawRectangleLines(ox - 200, oy + 95, 400, 35, _joinFocusAddress ? Color.Yellow : Color.White);
        Raylib.DrawText(_serverAddress, ox - 195, oy + 102, 20, Color.White);
        Raylib.DrawText("Password (optional):", ox - 200, oy + 150, 18, Color.LightGray);
        Raylib.DrawRectangle(ox - 200, oy + 175, 400, 35, Color.Black);
        Raylib.DrawRectangleLines(ox - 200, oy + 175, 400, 35, !_joinFocusAddress ? Color.Yellow : Color.White);
        Raylib.DrawText(new string('*', _passwordInput.Length), ox - 195, oy + 182, 20, Color.White);
        Rectangle connectBtn = new(ox - 100, oy + 240, 200, 45);
        Color connColor = Raylib.CheckCollisionPointRec(mousePos, connectBtn) ? new Color(0, 180, 70, 255) : new Color(0, 150, 50, 255);
        Raylib.DrawRectangleRounded(connectBtn, 0.15f, 10, connColor);
        Raylib.DrawRectangleRoundedLines(connectBtn, 0.15f, 10, Color.White);
        Raylib.DrawText("CONNECT", ox - Raylib.MeasureText("CONNECT", 22) / 2, oy + 252, 22, Color.White);
        Rectangle cancelBtn = new(ox - 100, oy + 295, 200, 40);
        Color cancelColor = Raylib.CheckCollisionPointRec(mousePos, cancelBtn) ? new Color(180, 70, 70, 255) : new Color(150, 50, 50, 255);
        Raylib.DrawRectangleRounded(cancelBtn, 0.15f, 10, cancelColor);
        Raylib.DrawRectangleRoundedLines(cancelBtn, 0.15f, 10, Color.White);
        Raylib.DrawText("CANCEL", ox - Raylib.MeasureText("CANCEL", 20) / 2, oy + 305, 20, Color.White);
    }
    Raylib.DrawRectangle(0, ScreenHeight - 80, ScreenWidth, 80, new Color(0, 0, 0, 150));
    Raylib.DrawText("CONTROLS", 30, ScreenHeight - 75, 16, Color.White);
    Raylib.DrawText("WASD: Move | Mouse: Aim | L/R Click: Kick | Shift: Sprint | Space: Tackle", 30, ScreenHeight - 50, 14, Color.LightGray);
    Raylib.DrawText("F11: Fullscreen | ESC: Back | Enter: Chat", 30, ScreenHeight - 30, 14, Color.LightGray);
}

void DrawLobby()
{
    Raylib.ClearBackground(Color.DarkGray);
    
    string[] teamNames = { "Home", "Away" };
    var mp = Raylib.GetMousePosition();
    
    Raylib.DrawText("Players", 20, 20, 24, Color.White);
    Raylib.DrawText($"{_gameState.Players.Count}/30", 20, 50, 16, Color.LightGray);
    
    // Process click actions FIRST (before drawing) - using SAME coordinates as drawing
    if (_isHost && Raylib.IsMouseButtonPressed(MouseButton.Left))
    {
        int yPos = 80;
        foreach (var player in _gameState.Players.ToList())
        {
            int btnX = 400; // Same padding as drawing section
            Rectangle teamLeft = new(btnX, yPos, 20, 16);
            Rectangle teamRight = new(btnX + 70, yPos, 20, 16);
            
            // Team toggle buttons
            if (Raylib.CheckCollisionPointRec(mp, teamLeft))
            {
                var newTeam = player.Team == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
                SetPlayerTeam(player.Id, newTeam);
            }
            else if (Raylib.CheckCollisionPointRec(mp, teamRight))
            {
                var newTeam = player.Team == TeamSide.Away ? TeamSide.Home : TeamSide.Away;
                SetPlayerTeam(player.Id, newTeam);
            }
            // Delete button - only for AI players
            else if (!player.IsHuman)
            {
                Rectangle delBtn = new(btnX + 110, yPos, 20, 16);
                if (Raylib.CheckCollisionPointRec(mp, delBtn) && _server != null)
                {
                    _server.RemoveAIPlayer(player.Id);
                }
            }
            
            yPos += 22;
            if (yPos > ScreenHeight - 100) break;
        }
    }
    
    // Now draw the player list
    int drawYPos = 80;
    foreach (var player in _gameState.Players)
    {
        string posStr = player.Position.ToString();
        string teamStr = teamNames[(int)player.Team];
        string coachStr = player.IsCoach ? " [COACH]" : "";
        string humanStr = player.IsHuman ? " [HUMAN]" : " [AI]";
        
        Raylib.DrawText($"{player.Username} - {posStr}{coachStr}{humanStr}", 20, drawYPos, 13, Color.White);
        
        // Team toggle buttons (only for host) - with team name in between
        if (_isHost)
        {
            int btnX = 400; // Position for team buttons
            Rectangle teamLeft = new(btnX, drawYPos, 20, 16);
            Rectangle teamRight = new(btnX + 70, drawYPos, 20, 16);
            
            Color leftColor = Raylib.CheckCollisionPointRec(mp, teamLeft) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
            Color rightColor = Raylib.CheckCollisionPointRec(mp, teamRight) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
            
            Raylib.DrawRectangleRec(teamLeft, leftColor);
            Raylib.DrawRectangleLinesEx(teamLeft, 1, Color.White);
            Raylib.DrawText("<", btnX + 6, drawYPos + 2, 13, Color.White);
            
            // Team name displayed between the arrow buttons
            Raylib.DrawText(teamStr, btnX + 28, drawYPos + 2, 13, Color.Yellow);
            
            Raylib.DrawRectangleRec(teamRight, rightColor);
            Raylib.DrawRectangleLinesEx(teamRight, 1, Color.White);
            Raylib.DrawText(">", btnX + 76, drawYPos + 2, 13, Color.White);
            
            // Delete button - only for AI players
            if (!player.IsHuman)
            {
                Rectangle delBtn = new(btnX + 110, drawYPos, 20, 16);
                Color delColor = Raylib.CheckCollisionPointRec(mp, delBtn) ? new Color(200, 50, 50, 255) : new Color(150, 30, 30, 255);
                Raylib.DrawRectangleRec(delBtn, delColor);
                Raylib.DrawRectangleLinesEx(delBtn, 1, Color.White);
                Raylib.DrawText("X", btnX + 118, drawYPos + 2, 13, Color.White);
            }
        }
        
        drawYPos += 22;
        if (drawYPos > ScreenHeight - 100) break;
    }

    // Calculate rightX based on rightmost player list element + padding
    // Delete button ends at btnX + 110 + 20 = 530, add 50px padding = 580
    int rightX = 580; // Controls section starts after player list elements + padding
    Raylib.DrawText("Controls", rightX, 20, 24, Color.White);
    
    if (_isHost)
    {
        string ipText = $"IP: {_server?.ExternalIp ?? "localhost"}:{_serverPort}";
        Raylib.DrawText(ipText, rightX, 60, 14, Color.Yellow);
        int ipTextWidth = Raylib.MeasureText(ipText, 14);
        Rectangle copyBtn = new(rightX + ipTextWidth + 10, 55, 70, 25);
        Color copyColor = Raylib.CheckCollisionPointRec(mp, copyBtn) ? new Color(60, 60, 60, 255) : new Color(40, 40, 40, 255);
        Raylib.DrawRectangleRec(copyBtn, copyColor);
        Raylib.DrawRectangleLinesEx(copyBtn, 1, Color.White);
        Raylib.DrawText("COPY", rightX + ipTextWidth + 15, 60, 12, Color.White);
        
        Rectangle startBtn = new(rightX, 100, 160, 35);
        Color startColor = Raylib.CheckCollisionPointRec(mp, startBtn) ? new Color(0, 200, 0, 255) : Color.Green;
        Raylib.DrawRectangleRec(startBtn, startColor);
        Raylib.DrawRectangleLinesEx(startBtn, 2, Color.White);
        Raylib.DrawText("START", rightX + 50, 110, 18, Color.White);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, startBtn))
            StartGame();
    }
    else
    {
        Raylib.DrawText($"Connected: {_serverAddress}", rightX, 60, 14, Color.Yellow);
    }

    int teamY = 160;
    Raylib.DrawText("My Team:", rightX, teamY, 16, Color.White);
    Rectangle teamLeftCtrl = new(rightX, teamY + 25, 30, 25);
    Rectangle teamRightCtrl = new(rightX + 110, teamY + 25, 30, 25);
    Color tlc = Raylib.CheckCollisionPointRec(mp, teamLeftCtrl) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
    Color trc = Raylib.CheckCollisionPointRec(mp, teamRightCtrl) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
    Raylib.DrawRectangleRec(teamLeftCtrl, tlc); Raylib.DrawRectangleLinesEx(teamLeftCtrl, 1, Color.White); Raylib.DrawText("<", rightX + 8, teamY + 28, 18, Color.White);
    Raylib.DrawText(teamNames[_selectedTeam], rightX + 40, teamY + 28, 16, Color.White);
    Raylib.DrawRectangleRec(teamRightCtrl, trc); Raylib.DrawRectangleLinesEx(teamRightCtrl, 1, Color.White); Raylib.DrawText(">", rightX + 118, teamY + 28, 18, Color.White);
    if (Raylib.IsMouseButtonPressed(MouseButton.Left))
    {
        if (Raylib.CheckCollisionPointRec(mp, teamLeftCtrl)) _selectedTeam = (_selectedTeam - 1 + 2) % 2;
        if (Raylib.CheckCollisionPointRec(mp, teamRightCtrl)) _selectedTeam = (_selectedTeam + 1) % 2;
    }
    if (_gameState.LocalPlayer != null)
        _gameState.LocalPlayer.Team = _selectedTeam == 0 ? TeamSide.Home : TeamSide.Away;

    string[] posNames = { "GK", "DEF", "MID", "FWD", "SUB" };
    int posY = teamY + 65;
    Raylib.DrawText("Position:", rightX, posY, 16, Color.White);
    Rectangle posLeft = new(rightX, posY + 25, 30, 25);
    Rectangle posRight = new(rightX + 110, posY + 25, 30, 25);
    Color plc = Raylib.CheckCollisionPointRec(mp, posLeft) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
    Color prc = Raylib.CheckCollisionPointRec(mp, posRight) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
    Raylib.DrawRectangleRec(posLeft, plc); Raylib.DrawRectangleLinesEx(posLeft, 1, Color.White); Raylib.DrawText("<", rightX + 8, posY + 28, 18, Color.White);
    Raylib.DrawText(posNames[_selectedPosition], rightX + 40, posY + 28, 16, Color.White);
    Raylib.DrawRectangleRec(posRight, prc); Raylib.DrawRectangleLinesEx(posRight, 1, Color.White); Raylib.DrawText(">", rightX + 118, posY + 28, 18, Color.White);

    bool editorsOpen = _showJerseyEditor || _showPlayerCustomization || _showStatsEditor;
    
    Rectangle jerseyBtn = new(rightX, posY + 70, 200, 30);
    Color jc = Raylib.CheckCollisionPointRec(mp, jerseyBtn) ? new Color((byte)60, (byte)60, (byte)60, (byte)255) : new Color((byte)40, (byte)40, (byte)40, (byte)255);
    Raylib.DrawRectangleRec(jerseyBtn, editorsOpen ? new Color((byte)(jc.R / 2), (byte)(jc.G / 2), (byte)(jc.B / 2), (byte)128) : jc); 
    Raylib.DrawRectangleLinesEx(jerseyBtn, 1, editorsOpen ? new Color((byte)127, (byte)127, (byte)127, (byte)128) : Color.White); 
    Raylib.DrawText("Jersey", rightX + 70, posY + 82, 14, editorsOpen ? new Color((byte)127, (byte)127, (byte)127, (byte)128) : Color.White);
    if (!editorsOpen && Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, jerseyBtn))
    {
        LoadTeamJerseyForEditing();
        _showJerseyEditor = true;
    }
    
    Rectangle custBtn = new(rightX, posY + 110, 200, 30);
    Color cc = Raylib.CheckCollisionPointRec(mp, custBtn) ? new Color((byte)60, (byte)60, (byte)60, (byte)255) : new Color((byte)40, (byte)40, (byte)40, (byte)255);
    Raylib.DrawRectangleRec(custBtn, editorsOpen ? new Color((byte)(cc.R / 2), (byte)(cc.G / 2), (byte)(cc.B / 2), (byte)128) : cc); 
    Raylib.DrawRectangleLinesEx(custBtn, 1, editorsOpen ? new Color((byte)127, (byte)127, (byte)127, (byte)128) : Color.White); 
    Raylib.DrawText("Appearance", rightX + 55, posY + 122, 14, editorsOpen ? new Color((byte)127, (byte)127, (byte)127, (byte)128) : Color.White);
    if (!editorsOpen && Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, custBtn))
        _showPlayerCustomization = true;
    
    Rectangle statsBtn = new(rightX, posY + 150, 200, 30);
    Color sc = Raylib.CheckCollisionPointRec(mp, statsBtn) ? new Color((byte)60, (byte)60, (byte)60, (byte)255) : new Color((byte)40, (byte)40, (byte)40, (byte)255);
    Raylib.DrawRectangleRec(statsBtn, editorsOpen ? new Color((byte)(sc.R / 2), (byte)(sc.G / 2), (byte)(sc.B / 2), (byte)128) : sc); 
    Raylib.DrawRectangleLinesEx(statsBtn, 1, editorsOpen ? new Color((byte)127, (byte)127, (byte)127, (byte)128) : Color.White); 
    Raylib.DrawText("Stats", rightX + 80, posY + 162, 14, editorsOpen ? new Color((byte)127, (byte)127, (byte)127, (byte)128) : Color.White);
    if (!editorsOpen && Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, statsBtn) && _gameState.CurrentPhase == GamePhase.Lobby)
        _showStatsEditor = true;

    // AI Player management buttons (host only, below team controls)
    if (_isHost && !editorsOpen)
    {
        int aiY = posY + 195;
        Raylib.DrawText("AI Players:", rightX, aiY, 16, Color.White);
        
        // Team selector for AI placement
        Rectangle aiTeamLeft = new(rightX, aiY + 25, 30, 25);
        Rectangle aiTeamRight = new(rightX + 110, aiY + 25, 30, 25);
        Color atlc = Raylib.CheckCollisionPointRec(mp, aiTeamLeft) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
        Color atrc = Raylib.CheckCollisionPointRec(mp, aiTeamRight) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
        Raylib.DrawRectangleRec(aiTeamLeft, atlc); Raylib.DrawRectangleLinesEx(aiTeamLeft, 1, Color.White); Raylib.DrawText("<", rightX + 8, aiY + 28, 18, Color.White);
        Raylib.DrawText(teamNames[_pendingAiTeam], rightX + 40, aiY + 28, 16, Color.White);
        Raylib.DrawRectangleRec(aiTeamRight, atrc); Raylib.DrawRectangleLinesEx(aiTeamRight, 1, Color.White); Raylib.DrawText(">", rightX + 118, aiY + 28, 18, Color.White);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (Raylib.CheckCollisionPointRec(mp, aiTeamLeft)) _pendingAiTeam = (_pendingAiTeam - 1 + 2) % 2;
            if (Raylib.CheckCollisionPointRec(mp, aiTeamRight)) _pendingAiTeam = (_pendingAiTeam + 1) % 2;
        }
        
        // Fill team with 11 AI players button
        Rectangle fillAiBtn = new(rightX, aiY + 60, 200, 30);
        Color fillAiColor = Raylib.CheckCollisionPointRec(mp, fillAiBtn) ? new Color(0, 180, 70, 255) : new Color(0, 130, 50, 255);
        Raylib.DrawRectangleRec(fillAiBtn, fillAiColor);
        Raylib.DrawRectangleLinesEx(fillAiBtn, 1, Color.White);
        Raylib.DrawText("Fill Team (11 players)", rightX + 20, aiY + 70, 13, Color.White);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, fillAiBtn) && _server != null)
        {
            _server.FillTeamWithAI(_pendingAiTeam == 0 ? TeamSide.Home : TeamSide.Away);
        }
        
        // Add single AI player buttons
        string[] aiPos = { "GK", "DEF", "MID", "FWD" };
        PlayerPosition[] aiPositions = { PlayerPosition.Goalkeeper, PlayerPosition.Defender, PlayerPosition.Midfielder, PlayerPosition.Forward };
        for (int i = 0; i < 4; i++)
        {
            Rectangle aiPosBtn = new(rightX + i * 50, aiY + 95, 45, 25);
            Color aiPosColor = Raylib.CheckCollisionPointRec(mp, aiPosBtn) ? new Color(60, 60, 60, 255) : new Color(40, 40, 40, 255);
            Raylib.DrawRectangleRec(aiPosBtn, aiPosColor);
            Raylib.DrawRectangleLinesEx(aiPosBtn, 1, Color.White);
            Raylib.DrawText(aiPos[i], rightX + i * 50 + 12, aiY + 101, 12, Color.White);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, aiPosBtn) && _server != null)
            {
                _server.AddAIPlayer(_pendingAiTeam == 0 ? TeamSide.Home : TeamSide.Away, aiPositions[i], false);
            }
        }
        // Add sub button
        Rectangle subBtn = new(rightX + 200, aiY + 95, 60, 25);
        Color subBtnColor = Raylib.CheckCollisionPointRec(mp, subBtn) ? new Color(60, 60, 60, 255) : new Color(40, 40, 40, 255);
        Raylib.DrawRectangleRec(subBtn, subBtnColor);
        Raylib.DrawRectangleLinesEx(subBtn, 1, Color.White);
        Raylib.DrawText("SUB", rightX + 215, aiY + 101, 12, Color.White);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, subBtn) && _server != null)
        {
            _server.AddAIPlayer(_pendingAiTeam == 0 ? TeamSide.Home : TeamSide.Away, PlayerPosition.Midfielder, true);
        }
    }

    if (_showJerseyEditor) DrawJerseyEditor();
    if (_showPlayerCustomization) DrawPlayerCustomization();
    if (_showStatsEditor) DrawStatsEditor();
    if (!_showJerseyEditor && !_showPlayerCustomization && !_showStatsEditor) DrawTeamJerseys();
}

void SetPlayerTeam(string playerId, TeamSide team)
{
    var player = _gameState.Players.Find(p => p.Id == playerId);
    if (player == null) return;
    
    // Remove from old team
    foreach (var t in _gameState.Teams.Values)
    {
        t.PlayerIds.Remove(playerId);
    }
    
    player.Team = team;
    var teamKey = team == TeamSide.Home ? "home" : "away";
    if (_gameState.Teams.TryGetValue(teamKey, out var targetTeam))
    {
        targetTeam.PlayerIds.Add(playerId);
    }
}

void DrawTeamJerseys()
{
    int jerseyX = ScreenWidth - 200, jerseyY = 160;
    Raylib.DrawText("Team Jerseys:", jerseyX - 50, jerseyY, 20, Color.White);
    foreach (var team in _gameState.Teams.Values)
    {
        jerseyY += 80;
        if (team.Jersey.PixelArt != null)
        {
            for (int px = 0; px < 32; px++)
                for (int py = 0; py < 32; py++)
                {
                    int ci = team.Jersey.PixelArt[px, py];
                    if (ci >= 0 && ci < _pixelPalette.Length)
                        Raylib.DrawRectangle(jerseyX + px * 2, jerseyY + py * 2, 2, 2, _pixelPalette[ci]);
                }
        }
        else
        {
            Color primary = team.Jersey.PrimaryColor == "#0000FF" ? Color.Blue : team.Jersey.PrimaryColor == "#FF0000" ? Color.Red : Color.White;
            Color secondary = team.Jersey.SecondaryColor == "#FFFFFF" ? Color.White : Color.LightGray;
            Raylib.DrawRectangle(jerseyX, jerseyY, 40, 50, primary);
            Raylib.DrawRectangle(jerseyX + 5, jerseyY + 5, 30, 20, secondary);
            Raylib.DrawRectangle(jerseyX + 5, jerseyY + 30, 10, 15, secondary);
            Raylib.DrawRectangle(jerseyX + 25, jerseyY + 30, 10, 15, secondary);
        }
        Raylib.DrawText($"{team.Name} ({team.Side})", jerseyX + 50, jerseyY + 15, 15, Color.White);
    }
}

void DrawJerseyEditor()
{
    Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 220));
    int pixelSize = 10, startX = ScreenWidth / 2 - 160, startY = ScreenHeight / 2 - 160;
    for (int x = 0; x < 32; x++)
        for (int y = 0; y < 32; y++)
        {
            int ci = _jerseyPixels[x, y];
            Color c = ci >= 0 && ci < _pixelPalette.Length ? _pixelPalette[ci] : Color.White;
            Raylib.DrawRectangle(startX + x * pixelSize, startY + y * pixelSize, pixelSize, pixelSize, c);
            Raylib.DrawRectangleLines(startX + x * pixelSize, startY + y * pixelSize, pixelSize, pixelSize, Color.Gray);
        }
    Raylib.DrawText("Color Palette:", 50, ScreenHeight - 130, 20, Color.White);
    for (int i = 0; i < _pixelPalette.Length; i++)
    {
        int px = 50 + i * 30, py = ScreenHeight - 100;
        Raylib.DrawRectangle(px, py, 25, 25, _pixelPalette[i]);
        Raylib.DrawRectangleLines(px, py, 25, 25, i == _selectedPixelColor ? Color.Yellow : Color.Gray);
    }
    Rectangle closeBtn = new(ScreenWidth / 2 - 50, ScreenHeight - 40, 100, 30);
    Color closeColor = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), closeBtn) ? new Color(200, 50, 50, 255) : new Color(150, 30, 30, 255);
    Raylib.DrawRectangleRec(closeBtn, closeColor); Raylib.DrawRectangleLinesEx(closeBtn, 2, Color.White); Raylib.DrawText("CLOSE", ScreenWidth / 2 - 25, ScreenHeight - 37, 16, Color.White);
}

void DrawPlayerCustomization()
{
    Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 220));
    int cx = ScreenWidth / 2, yPos = 100;
    Raylib.DrawText("Player Customization", cx - 150, yPos, 30, Color.White);
    yPos += 50;
    string[] hairStyles = { "Short", "Medium", "Long", "Curly", "Bald" };
    string[] hairColors = { "Black", "Brown", "Blonde", "Red", "White", "Blue", "Green", "Pink" };
    string[] eyeColors = { "Brown", "Blue", "Green", "Gray", "Hazel" };
    string[] skinTones = { "Light", "Medium", "Tan", "Dark", "V.Dark" };
    string[] facialHair = { "None", "Stubble", "Goatee", "Beard", "Mustache" };
    string[][] labels = new[] { hairStyles, hairColors, eyeColors, skinTones, facialHair };
    int[] values = new[] { _selectedHairStyle, _selectedHairColor, _selectedEyeColor, _selectedSkinTone, _selectedFacialHair };
    string[] names = new[] { "Hair Style", "Hair Color", "Eye Color", "Skin Tone", "Facial Hair" };
    var mp = Raylib.GetMousePosition();
    for (int i = 0; i < 5; i++)
    {
        int btnY = yPos + i * 32;
        Rectangle leftBtn = new(cx - 210, btnY, 30, 25);
        Color lc = Raylib.CheckCollisionPointRec(mp, leftBtn) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
        Raylib.DrawRectangleRec(leftBtn, lc); Raylib.DrawRectangleLinesEx(leftBtn, 1, Color.White); Raylib.DrawText("<", cx - 203, btnY + 3, 18, Color.White);
        Raylib.DrawText($"{names[i]}: {labels[i][values[i]]}", cx - 170, btnY + 3, 16, Color.White);
        Rectangle rightBtn = new(cx + 150, btnY, 30, 25);
        Color rc = Raylib.CheckCollisionPointRec(mp, rightBtn) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
        Raylib.DrawRectangleRec(rightBtn, rc); Raylib.DrawRectangleLinesEx(rightBtn, 1, Color.White); Raylib.DrawText(">", cx + 160, btnY + 3, 18, Color.White);
    }
    DrawPlayerPreview(cx, 370);
    Rectangle closeBtn = new(ScreenWidth / 2 - 50, ScreenHeight - 40, 100, 30);
    Color closeColor = Raylib.CheckCollisionPointRec(mp, closeBtn) ? new Color(200, 50, 50, 255) : new Color(150, 30, 30, 255);
    Raylib.DrawRectangleRec(closeBtn, closeColor); Raylib.DrawRectangleLinesEx(closeBtn, 2, Color.White); Raylib.DrawText("CLOSE", ScreenWidth / 2 - 25, ScreenHeight - 37, 16, Color.White);
}

void DrawPlayerPreview(int centerX, int centerY)
{
    Color skinColor = _selectedSkinTone switch { 0 => new Color(255, 205, 148, 255), 1 => new Color(224, 172, 105, 255), 2 => new Color(198, 134, 66, 255), 3 => new Color(141, 85, 36, 255), 4 => new Color(90, 50, 20, 255), _ => new Color(255, 205, 148, 255) };
    Color hairColor = _selectedHairColor switch { 0 => Color.Black, 1 => new Color(139, 69, 19, 255), 2 => Color.Yellow, 3 => Color.Red, 4 => Color.White, 5 => Color.Blue, 6 => Color.Green, 7 => Color.Pink, _ => Color.Black };
    Color eyeColor = _selectedEyeColor switch { 0 => new Color(139, 69, 19, 255), 1 => Color.Blue, 2 => Color.Green, 3 => Color.Gray, 4 => new Color(205, 133, 63, 255), _ => Color.Brown };
    Raylib.DrawCircle(centerX, centerY - 20, 20, skinColor);
    if (_selectedHairStyle < 4) Raylib.DrawCircle(centerX, centerY - 30, 15, hairColor);
    Raylib.DrawCircle(centerX - 7, centerY - 22, 3, eyeColor);
    Raylib.DrawCircle(centerX + 7, centerY - 22, 3, eyeColor);
    Raylib.DrawRectangle(centerX - 15, centerY, 30, 40, Color.Red);
    Raylib.DrawRectangle(centerX - 12, centerY + 40, 10, 30, Color.White); Raylib.DrawRectangle(centerX + 2, centerY + 40, 10, 30, Color.White);
}

void DrawStatsEditor()
{
    Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 220));
    int cx = ScreenWidth / 2, yPos = 80;
    Raylib.DrawText("Player Stats", cx - 100, yPos, 30, Color.White);
    yPos += 40;
    if (_gameState.LocalPlayer != null)
    {
        var stats = _gameState.LocalPlayer.Stats;
        Raylib.DrawText($"Points Remaining: {stats.StatPointsRemaining}", cx - Raylib.MeasureText($"Points Remaining: {stats.StatPointsRemaining}", 20) / 2, yPos, 20, Color.Yellow);
        yPos += 30;
        var statNames = PlayerStats.StatNames;
        for (int i = 0; i < statNames.Length; i++)
        {
            int value = stats.GetStat(statNames[i]);
            Raylib.DrawText(statNames[i], cx - 200, yPos, 16, Color.White);
            Raylib.DrawText($"{value}", cx + 10, yPos, 16, Color.White);
            Rectangle sliderTrack = new(cx + 50, yPos + 2, 200, 20);
            Raylib.DrawRectangleRec(sliderTrack, new Color(40, 40, 40, 255));
            Raylib.DrawRectangleLinesEx(sliderTrack, 1, Color.Gray);
            float fillWidth = (value / 100f) * 200f;
            Color fillColor = value > 80 ? Color.Green : value > 50 ? Color.Yellow : Color.Red;
            Raylib.DrawRectangle((int)sliderTrack.X, (int)sliderTrack.Y, (int)fillWidth, (int)sliderTrack.Height, fillColor);
            int handleX = (int)(sliderTrack.X + fillWidth - 4);
            Raylib.DrawRectangle(handleX, (int)sliderTrack.Y - 2, 8, 18, Color.White);
            Rectangle decBtn = new(cx + 255, yPos, 25, 24);
            Color decColor = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), decBtn) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
            Raylib.DrawRectangleRec(decBtn, decColor); Raylib.DrawRectangleLinesEx(decBtn, 1, Color.White); Raylib.DrawText("-", cx + 263, yPos + 3, 18, Color.White);
            Rectangle incBtn = new(cx + 285, yPos, 25, 24);
            Color incColor = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), incBtn) ? new Color(80, 80, 80, 255) : new Color(50, 50, 50, 255);
            Raylib.DrawRectangleRec(incBtn, incColor); Raylib.DrawRectangleLinesEx(incBtn, 1, Color.White); Raylib.DrawText("+", cx + 293, yPos + 3, 18, Color.White);
            yPos += 25;
        }
    }
    Rectangle closeBtn = new(ScreenWidth / 2 - 50, ScreenHeight - 40, 100, 30);
    Color closeColor = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), closeBtn) ? new Color(200, 50, 50, 255) : new Color(150, 30, 30, 255);
    Raylib.DrawRectangleRec(closeBtn, closeColor); Raylib.DrawRectangleLinesEx(closeBtn, 2, Color.White); Raylib.DrawText("CLOSE", ScreenWidth / 2 - 25, ScreenHeight - 37, 16, Color.White);
}

void DrawMatch()
{
    DrawPitch();
    DrawPlayers();
    DrawBall();
    DrawHUD();
    if (_showQuitConfirm) DrawQuitConfirm();
    else if (_showPauseMenu) DrawPauseMenu();
    if (_showSubstitutionMenu) DrawSubstitutionMenu();
    if (_showChat) DrawChat();
    DrawVoiceControls();
}

void DrawPitch()
{
    int actualScreenWidth = Raylib.GetScreenWidth();
    int actualScreenHeight = Raylib.GetScreenHeight();
    
    Raylib.DrawRectangle(0, 0, actualScreenWidth, actualScreenHeight, new Color(34, 139, 34, 255));
    int fieldX = actualScreenWidth / 2;
    int fieldY = actualScreenHeight / 2;
    Raylib.DrawRectangleLines(fieldX - (int)PitchDimensions.Width / 2, fieldY - (int)PitchDimensions.Height / 2, (int)PitchDimensions.Width, (int)PitchDimensions.Height, Color.White);
    Raylib.DrawLine(fieldX, fieldY - (int)PitchDimensions.Height / 2, fieldX, fieldY + (int)PitchDimensions.Height / 2, Color.White);
    Raylib.DrawCircleLines(fieldX, fieldY, PitchDimensions.CenterCircleRadius, Color.White);
    Raylib.DrawCircle(fieldX, fieldY, 3, Color.White);
    int leftPenX = fieldX - (int)PitchDimensions.Width / 2;
    int leftPenY = fieldY - (int)PitchDimensions.PenaltyAreaHeight / 2;
    Raylib.DrawRectangleLines(leftPenX, leftPenY, (int)PitchDimensions.PenaltyAreaWidth, (int)PitchDimensions.PenaltyAreaHeight, Color.White);
    int rightPenX = fieldX + (int)PitchDimensions.Width / 2 - (int)PitchDimensions.PenaltyAreaWidth;
    Raylib.DrawRectangleLines(rightPenX, leftPenY, (int)PitchDimensions.PenaltyAreaWidth, (int)PitchDimensions.PenaltyAreaHeight, Color.White);
    int goalAreaY = fieldY - (int)PitchDimensions.GoalAreaHeight / 2;
    Raylib.DrawRectangleLines(leftPenX, goalAreaY, (int)PitchDimensions.GoalAreaWidth, (int)PitchDimensions.GoalAreaHeight, Color.White);
    int rightGoalX = fieldX + (int)PitchDimensions.Width / 2 - (int)PitchDimensions.GoalAreaWidth;
    Raylib.DrawRectangleLines(rightGoalX, goalAreaY, (int)PitchDimensions.GoalAreaWidth, (int)PitchDimensions.GoalAreaHeight, Color.White);
    int goalY = fieldY - (int)PitchDimensions.GoalWidth / 2;
    Raylib.DrawRectangle(leftPenX - 5, goalY, 5, (int)PitchDimensions.GoalWidth, Color.White);
    Raylib.DrawRectangle(rightPenX + (int)PitchDimensions.PenaltyAreaWidth, goalY, 5, (int)PitchDimensions.GoalWidth, Color.White);
    int cr = (int)PitchDimensions.CornerArcRadius;
    Raylib.DrawCircleSector(new System.Numerics.Vector2(leftPenX, fieldY - (int)PitchDimensions.Height / 2), cr, 0, 90, 20, Color.White);
    Raylib.DrawCircleSector(new System.Numerics.Vector2(leftPenX + (int)PitchDimensions.Width, fieldY - (int)PitchDimensions.Height / 2), cr, 90, 180, 20, Color.White);
    Raylib.DrawCircleSector(new System.Numerics.Vector2(leftPenX + (int)PitchDimensions.Width, fieldY + (int)PitchDimensions.Height / 2), cr, 180, 270, 20, Color.White);
    Raylib.DrawCircleSector(new System.Numerics.Vector2(leftPenX, fieldY + (int)PitchDimensions.Height / 2), cr, 270, 360, 20, Color.White);
    DrawBench(fieldX - (int)PitchDimensions.Width / 2, fieldY + (int)PitchDimensions.Height / 2 + 20, TeamSide.Home);
    DrawBench(fieldX + (int)PitchDimensions.Width / 2 - 100, fieldY + (int)PitchDimensions.Height / 2 + 20, TeamSide.Away);
}

void DrawBench(int benchX, int benchY, TeamSide side)
{
    Raylib.DrawRectangle(benchX, benchY, 100, 60, new Color(139, 69, 19, 150));
    Raylib.DrawRectangleLines(benchX, benchY, 100, 60, Color.White);
    Raylib.DrawText($"{side} Bench", benchX + 10, benchY + 5, 10, Color.White);
    var subs = _gameState.Players.Where(p => p.Team == side && p.IsSubstitute).ToList();
    int subX = benchX + 15, subY = benchY + 25;
    foreach (var sub in subs)
    {
        Raylib.DrawCircle(subX, subY, 6, side == TeamSide.Home ? Color.Red : Color.Blue);
        subX += 22;
        if (subX > benchX + 80) { subX = benchX + 15; subY += 18; }
    }
}

void DrawPlayers()
{
    var players = _gameState.CurrentMatch?.TeamPlayers.SelectMany(kvp => kvp.Value).ToList();
    if (players == null || players.Count == 0) players = _gameState.Players;
    foreach (var player in players)
    {
        if (player.IsSubstitute && !player.IsOnField) continue;
        int screenX = (int)(player.X - _cameraOffsetX);
        int screenY = (int)(player.Y - _cameraOffsetY);
        Color teamColor = Color.Red;
        var teamKey = player.Team == TeamSide.Home ? "home" : "away";
        if (_gameState.Teams.TryGetValue(teamKey, out var team))
        {
            string primary = team.Jersey.PrimaryColor ?? "#FF0000";
            teamColor = primary.ToUpper() switch
            {
                "#FF0000" => Color.Red, "#0000FF" => Color.Blue, "#00FF00" => Color.Lime, "#FFFF00" => Color.Yellow, "#FFFFFF" => Color.White, "#000000" => Color.Black, "#FFA500" => Color.Orange, "#800080" => Color.Purple, "#FFC0CB" => Color.Pink, "#A52A2A" => Color.Brown, "#808080" => Color.Gray, _ => Color.Red
            };
        }
        if (player.IsKnockedDown) Raylib.DrawRectangle(screenX - 10, screenY - 5, 20, 10, teamColor);
        else
        {
            Raylib.DrawCircle(screenX, screenY - 15, 6, player.Appearance.SkinTone switch { 0 => new Color(255, 205, 148, 255), 1 => new Color(224, 172, 105, 255), 2 => new Color(198, 134, 66, 255), 3 => new Color(141, 85, 36, 255), 4 => new Color(90, 50, 20, 255), _ => new Color(255, 205, 148, 255) });
            Raylib.DrawCircle(screenX, screenY - 5, 8, teamColor);
            Color secondary = player.Team == TeamSide.Home ? new Color(255, 200, 200, 255) : new Color(200, 200, 255, 255);
            Raylib.DrawRectangle(screenX - 6, screenY - 8, 12, 4, secondary);
            Raylib.DrawLine(screenX - 8, screenY - 2, screenX + 8, screenY - 2, secondary);
            float dirX = MathF.Cos(player.Direction) * 12, dirY = MathF.Sin(player.Direction) * 12;
            Raylib.DrawLine(screenX, screenY, (int)(screenX + dirX), (int)(screenY + dirY), Color.White);
        }
        if (player.HasBall) Raylib.DrawCircle(screenX, screenY - 20, 4, Color.White);
        if (player.Id == _localPlayerId) Raylib.DrawCircleLines(screenX, screenY - 5, 12, Color.Yellow);
        Raylib.DrawText(player.Username, screenX - 20, screenY - 30, 10, Color.White);
    }
}

void DrawBall()
{
    if (_gameState.CurrentMatch?.Ball == null) return;
    var ball = _gameState.CurrentMatch.Ball;
    int sx = (int)(ball.X - _cameraOffsetX), sy = (int)(ball.Y - _cameraOffsetY);
    float radius = 5 * ball.VisualScale;
    Raylib.DrawCircle(sx, sy, radius, Color.White);
    Raylib.DrawCircleLines(sx, sy, radius, Color.Black);
    Raylib.DrawCircle(sx, sy, radius * 0.3f, Color.Black);
}

void DrawHUD()
{
    if (_gameState.CurrentMatch == null) return;
    var match = _gameState.CurrentMatch;
    int sw = 300, sh = 95, sx = ScreenWidth - sw - 20, sy = 20;
    Raylib.DrawRectangleRounded(new Rectangle(sx, sy, sw, sh), 0.1f, 10, new Color(0, 0, 0, 200));
    Raylib.DrawRectangle(sx + 5, sy + 5, 140, 30, Color.Red); Raylib.DrawText("HOME", sx + 40, sy + 10, 15, Color.White);
    Raylib.DrawRectangle(sx + 155, sy + 5, 140, 30, Color.Blue); Raylib.DrawText("AWAY", sx + 190, sy + 10, 15, Color.White);
    string score = $"{match.HomeScore} - {match.AwayScore}";
    Raylib.DrawText(score, sx + sw / 2 - Raylib.MeasureText(score, 30) / 2, sy + 40, 30, Color.White);
    string timeStr = $"{match.MatchMinute:D2}:{match.MatchSecond:D2}";
    string halfStr = match.CurrentHalf switch { GamePhase.FirstHalf => "1st Half", GamePhase.SecondHalf => "2nd Half", GamePhase.Halftime => "Half Time", GamePhase.FullTime => "Full Time", _ => "" };
    Raylib.DrawText($"{timeStr} {halfStr}", sx + 5, sy + 78, 12, Color.LightGray);
    Raylib.DrawText("WASD: Move | Mouse: Aim | L/R: Kick | Shift: Sprint | Space: Tackle", 20, ScreenHeight - 30, 12, Color.LightGray);
    
    // Draw referee call just above the field (top center area)
    if (_gameState.CurrentMatch != null && _gameState.CurrentMatch.RefereeCallTimer > 0)
    {
        string refCall = _gameState.CurrentMatch.RefereeCall;
        if (!string.IsNullOrEmpty(refCall))
        {
            int actualScreenHeight = Raylib.GetScreenHeight();
            int fieldTopY = actualScreenHeight / 2 - (int)PitchDimensions.Height / 2;
            int refCallX = ScreenWidth / 2 - Raylib.MeasureText(refCall, 28) / 2;
            int refCallY = fieldTopY - 40;
            float alpha = Math.Clamp(_gameState.CurrentMatch.RefereeCallTimer / 2.0f, 0.0f, 1.0f);
            Color refColor = new Color(255, 255, 100, (int)(alpha * 255));
            Raylib.DrawText(refCall, refCallX + 2, refCallY + 2, 28, Color.Black);
            Raylib.DrawText(refCall, refCallX, refCallY, 28, refColor);
        }
    }
}

void DrawQuitConfirm()
{
    Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 180));
    int cx = ScreenWidth / 2, cy = ScreenHeight / 2;
    Raylib.DrawText("Are you sure you want to cancel match?", cx - Raylib.MeasureText("Are you sure you want to cancel match?", 24) / 2, cy - 40, 24, Color.White);
    var mp = Raylib.GetMousePosition();
    Rectangle yesBtn = new(cx - 110, cy + 20, 100, 40);
    Color yesColor = Raylib.CheckCollisionPointRec(mp, yesBtn) ? new Color(200, 50, 50, 255) : new Color(150, 30, 30, 255);
    Raylib.DrawRectangleRec(yesBtn, yesColor); Raylib.DrawRectangleLinesEx(yesBtn, 2, Color.White); Raylib.DrawText("YES", cx - 80, cy + 32, 20, Color.White);
    Rectangle noBtn = new(cx + 10, cy + 20, 100, 40);
    Color noColor = Raylib.CheckCollisionPointRec(mp, noBtn) ? new Color(0, 180, 70, 255) : new Color(0, 150, 50, 255);
    Raylib.DrawRectangleRec(noBtn, noColor); Raylib.DrawRectangleLinesEx(noBtn, 2, Color.White); Raylib.DrawText("NO", cx + 45, cy + 32, 20, Color.White);
}

void DrawPauseMenu()
{
    Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color(0, 0, 0, 180));
    int cx = ScreenWidth / 2, cy = ScreenHeight / 2;
    Raylib.DrawText("PAUSED", cx - Raylib.MeasureText("PAUSED", 50) / 2, cy - 120, 50, Color.White);
    var mp = Raylib.GetMousePosition();
    Rectangle resumeBtn = new(cx - 100, cy - 60, 200, 40);
    Color resumeColor = Raylib.CheckCollisionPointRec(mp, resumeBtn) ? new Color(0, 180, 70, 255) : new Color(0, 150, 50, 255);
    Raylib.DrawRectangleRec(resumeBtn, resumeColor); Raylib.DrawRectangleLinesEx(resumeBtn, 2, Color.White); Raylib.DrawText("RESUME", cx - Raylib.MeasureText("RESUME", 20) / 2, cy - 50, 20, Color.White);
    Rectangle subBtn = new(cx - 100, cy, 200, 40);
    Color subColor = Raylib.CheckCollisionPointRec(mp, subBtn) ? new Color(0, 130, 230, 255) : new Color(0, 100, 200, 255);
    Raylib.DrawRectangleRec(subBtn, subColor); Raylib.DrawRectangleLinesEx(subBtn, 2, Color.White); Raylib.DrawText("SUBSTITUTION", cx - Raylib.MeasureText("SUBSTITUTION", 20) / 2, cy + 10, 20, Color.White);
    Rectangle quitBtn = new(cx - 100, cy + 60, 200, 40);
    Color quitColor = Raylib.CheckCollisionPointRec(mp, quitBtn) ? new Color(180, 70, 70, 255) : new Color(150, 50, 50, 255);
    Raylib.DrawRectangleRec(quitBtn, quitColor); Raylib.DrawRectangleLinesEx(quitBtn, 2, Color.White); Raylib.DrawText("QUIT TO LOBBY", cx - Raylib.MeasureText("QUIT TO LOBBY", 20) / 2, cy + 70, 20, Color.White);
}

void DrawSubstitutionMenu()
{
    Raylib.DrawRectangle(ScreenWidth / 2 - 200, ScreenHeight / 2 - 200, 400, 400, new Color(0, 0, 0, 220));
    Raylib.DrawRectangleLines(ScreenWidth / 2 - 200, ScreenHeight / 2 - 200, 400, 400, Color.White);
    Raylib.DrawText("Substitution", ScreenWidth / 2 - 80, ScreenHeight / 2 - 190, 25, Color.White);
    int yPos = ScreenHeight / 2 - 150;
    Raylib.DrawText("Field Players (click to swap):", ScreenWidth / 2 - 180, yPos, 15, Color.Yellow);
    yPos += 25;
    var mp = Raylib.GetMousePosition();
    var fieldPlayers = _gameState.Players.Where(p => !p.IsSubstitute && p.Team == _gameState.LocalPlayer?.Team).ToList();
    var subs = _gameState.Players.Where(p => p.IsSubstitute && p.Team == _gameState.LocalPlayer?.Team).ToList();
    for (int i = 0; i < fieldPlayers.Count; i++)
    {
        var p = fieldPlayers[i];
        Color c = (i == _subPlayerOffIndex) ? Color.Green : Color.White;
        Raylib.DrawText($"{p.Username} ({p.Position})", ScreenWidth / 2 - 170, yPos, 12, c);
        Rectangle playerRect = new(ScreenWidth / 2 - 170, yPos - 3, 200, 18);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, playerRect))
        { _subPlayerOffIndex = i; _subPlayerOnIndex = -1; }
        yPos += 20;
    }
    yPos += 10;
    Raylib.DrawText("Substitutes (click to swap):", ScreenWidth / 2 - 180, yPos, 15, Color.Yellow);
    yPos += 25;
    for (int i = 0; i < subs.Count; i++)
    {
        var p = subs[i];
        Color c = (i == _subPlayerOnIndex) ? Color.Green : Color.White;
        Raylib.DrawText($"{p.Username} ({p.Position})", ScreenWidth / 2 - 170, yPos, 12, c);
        Rectangle subRect = new(ScreenWidth / 2 - 170, yPos - 3, 200, 18);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, subRect)) { _subPlayerOnIndex = i; }
        yPos += 20;
    }
    yPos = ScreenHeight / 2 + 120;
    Rectangle confirmBtn = new(ScreenWidth / 2 - 80, yPos, 160, 30);
    if (_subPlayerOffIndex >= 0 && _subPlayerOnIndex >= 0)
    {
        Color confirmColor = Raylib.CheckCollisionPointRec(mp, confirmBtn) ? new Color(0, 200, 0, 255) : Color.Green;
        Raylib.DrawRectangleRec(confirmBtn, confirmColor); Raylib.DrawRectangleLinesEx(confirmBtn, 2, Color.White); Raylib.DrawText("CONFIRM", ScreenWidth / 2 - 40, yPos + 8, 16, Color.White);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mp, confirmBtn))
        {
            if (_isHost && _server != null && _server.MatchEngine != null) { var playerOff = fieldPlayers[_subPlayerOffIndex]; var playerOn = subs[_subPlayerOnIndex]; _server.MatchEngine.HandleSubstitution(playerOff, playerOn); }
            _showSubstitutionMenu = false;
            _subPlayerOffIndex = -1; _subPlayerOnIndex = -1;
        }
    }
    Raylib.DrawText("Press U to close", ScreenWidth / 2 - 60, ScreenHeight / 2 + 170, 15, Color.LightGray);
}

void DrawChat()
{
    int cw = 300, ch = 200, cx = ScreenWidth - cw - 20, cy = ScreenHeight - ch - 60;
    Raylib.DrawRectangle(cx, cy, cw, ch, new Color(0, 0, 0, 180));
    Raylib.DrawRectangleLines(cx, cy, cw, ch, Color.White);
    int yPos = cy + 5;
    foreach (var msg in _chatMessages.TakeLast(8)) { Raylib.DrawText(msg, cx + 5, yPos, 12, Color.White); yPos += 20; }
    Raylib.DrawRectangle(cx, cy + ch, cw, 25, Color.Black);
    Raylib.DrawRectangleLines(cx, cy + ch, cw, 25, Color.White);
    Raylib.DrawText(_chatInput, cx + 5, cy + ch + 5, 12, Color.White);
}

void DrawVoiceControls()
{
    // Place voice chat in top right corner, above and to the right of scoreboard area
    // Scoreboard is at ScreenWidth - 320 to ScreenWidth - 20
    // Voice chat at ScreenWidth - 180 with 20px padding from right edge
    int vx = ScreenWidth - 180, vy = 20;
    Raylib.DrawText("Voice Chat", vx, vy, 15, Color.White);
    Raylib.DrawText($"Team Vol: {_voiceVolumeTeam:P0}", vx, vy + 20, 12, Color.LightGray);
    Raylib.DrawText($"Opp Vol: {_voiceVolumeOpponent:P0}", vx, vy + 35, 12, Color.LightGray);
    Raylib.DrawText("PgUp/Dn: Team | Home/End: Opp", vx, vy + 55, 10, Color.Gray);
}
