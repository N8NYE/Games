using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SockerGame.Core.Enums;
using SockerGame.Core.Models;
using SockerGame.Core.Systems;

namespace SockerGame.Server
{
    public class GameServer : IDisposable
    {
        private TcpListener? _listener;
        private readonly Dictionary<string, TcpClient> _clients = new();
        private readonly Dictionary<string, NetworkStream> _streams = new();
        private CancellationTokenSource _cts = new();
        private Task? _acceptTask;

        public GameState GameState { get; private set; } = new();
        public MatchEngine? MatchEngine { get; private set; }
        public bool IsRunning { get; private set; }
        public int Port { get; private set; }
        public string ExternalIp { get; private set; } = "localhost";

        public event Action<string>? OnLog;
        public event Action<NetworkMessage>? OnMessageReceived;

        public void Start(int port = 25565)
        {
            Port = port;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            IsRunning = true;

            // Get external IP
            try
            {
                using var webClient = new HttpClient();
                ExternalIp = webClient.GetStringAsync("https://api.ipify.org").Result;
            }
            catch
            {
                ExternalIp = "localhost";
            }

            _cts = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptClientsAsync(_cts.Token));
            OnLog?.Invoke($"Server started on port {port}. External IP: {ExternalIp}");
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    var clientId = Guid.NewGuid().ToString();
                    _clients[clientId] = client;
                    var stream = client.GetStream();
                    _streams[clientId] = stream;

                    OnLog?.Invoke($"Client connected: {client.Client.RemoteEndPoint} (ID: {clientId})");

                    // Handle client in background
                    _ = Task.Run(() => HandleClientAsync(clientId, stream, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(string clientId, NetworkStream stream, CancellationToken token)
        {
            var buffer = new byte[8192];

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!stream.DataAvailable)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead == 0) break;

                    var messageStr = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var messages = messageStr.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var msg in messages)
                    {
                        try
                        {
                            var networkMessage = JsonSerializer.Deserialize<NetworkMessage>(msg);
                            if (networkMessage != null)
                            {
                                networkMessage.SenderId = clientId;
                                OnMessageReceived?.Invoke(networkMessage);
                                await ProcessMessage(networkMessage, stream);
                            }
                        }
                        catch (JsonException ex)
                        {
                            OnLog?.Invoke($"JSON parse error: {ex.Message}");
                        }
                    }
                }
                catch (IOException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Client handler error: {ex.Message}");
                    break;
                }
            }

            DisconnectClient(clientId);
        }

        private async Task ProcessMessage(NetworkMessage message, NetworkStream senderStream)
        {
            switch (message.Type)
            {
                case NetworkMessageType.JoinLobby:
                    await HandleJoinLobby(message);
                    break;
                case NetworkMessageType.LeaveLobby:
                    await HandleLeaveLobby(message);
                    break;
                case NetworkMessageType.PlayerInput:
                    await HandlePlayerInput(message);
                    break;
                case NetworkMessageType.JerseyUpdate:
                    await HandleJerseyUpdate(message);
                    break;
                case NetworkMessageType.PlayerCustomization:
                    await HandlePlayerCustomization(message);
                    break;
                case NetworkMessageType.PlayerStatsUpdate:
                    await HandlePlayerStatsUpdate(message);
                    break;
                case NetworkMessageType.GameStart:
                    await StartGame();
                    break;
                case NetworkMessageType.Substitution:
                    await HandleSubstitution(message);
                    break;
            }
        }

        private async Task HandleJoinLobby(NetworkMessage message)
        {
            var joinData = JsonSerializer.Deserialize<JoinLobbyData>(message.Payload);
            if (joinData == null) return;

            // Check password
            if (GameState.CurrentLobby != null && !string.IsNullOrEmpty(GameState.CurrentLobby.Password))
            {
                if (joinData.Password != GameState.CurrentLobby.Password)
                {
                    await SendToClient(message.SenderId, new NetworkMessage
                    {
                        Type = NetworkMessageType.LobbyUpdate,
                        Payload = JsonSerializer.Serialize(new { error = "Invalid password" })
                    });
                    return;
                }
            }

            var player = new Player
            {
                Id = message.SenderId,
                Username = joinData.Username,
                IsHuman = true
            };

            GameState.Players.Add(player);
            GameState.CurrentLobby?.ConnectedPlayerIds.Add(message.SenderId);

            OnLog?.Invoke($"Player {joinData.Username} joined lobby");

            await BroadcastLobbyUpdate();
        }

        private async Task HandleLeaveLobby(NetworkMessage message)
        {
            var player = GameState.Players.Find(p => p.Id == message.SenderId);
            if (player != null)
            {
                GameState.Players.Remove(player);
                GameState.CurrentLobby?.ConnectedPlayerIds.Remove(message.SenderId);
                OnLog?.Invoke($"Player {player.Username} left lobby");
            }

            await BroadcastLobbyUpdate();
        }

        private async Task HandlePlayerInput(NetworkMessage message)
        {
            var input = JsonSerializer.Deserialize<InputState>(message.Payload);
            if (input == null) return;

            // Update the player's target position based on input
            var player = GameState.Players.Find(p => p.Id == message.SenderId);
            if (player != null)
            {
                player.TargetX += input.MoveX * 5;
                player.TargetY += input.MoveY * 5;
                player.Direction = MathF.Atan2(input.MouseY - player.Y, input.MouseX - player.X);
            }

            await BroadcastGameState();
        }

        private async Task HandleJerseyUpdate(NetworkMessage message)
        {
            var jerseyData = JsonSerializer.Deserialize<JerseyUpdateData>(message.Payload);
            if (jerseyData == null) return;

            var team = GameState.Teams.Values.FirstOrDefault(t => t.Id == jerseyData.TeamId);
            if (team != null)
            {
                team.Jersey.PrimaryColor = jerseyData.PrimaryColor;
                team.Jersey.SecondaryColor = jerseyData.SecondaryColor;
                team.Jersey.PixelArt = jerseyData.PixelArt;
            }

            await BroadcastLobbyUpdate();
        }

        private async Task HandlePlayerCustomization(NetworkMessage message)
        {
            var appearance = JsonSerializer.Deserialize<PlayerAppearance>(message.Payload);
            if (appearance == null) return;

            var player = GameState.Players.Find(p => p.Id == message.SenderId);
            if (player != null)
            {
                player.Appearance = appearance;
            }

            await BroadcastLobbyUpdate();
        }

        private async Task HandlePlayerStatsUpdate(NetworkMessage message)
        {
            var stats = JsonSerializer.Deserialize<PlayerStats>(message.Payload);
            if (stats == null) return;

            var player = GameState.Players.Find(p => p.Id == message.SenderId);
            if (player != null)
            {
                player.Stats = stats;
            }

            await BroadcastLobbyUpdate();
        }

        private async Task HandleSubstitution(NetworkMessage message)
        {
            var subData = JsonSerializer.Deserialize<SubstitutionData>(message.Payload);
            if (subData == null || MatchEngine == null) return;

            var playerOff = GameState.Players.Find(p => p.Id == subData.PlayerOffId);
            var playerOn = GameState.Players.Find(p => p.Id == subData.PlayerOnId);

            if (playerOff != null && playerOn != null)
            {
                MatchEngine.HandleSubstitution(playerOff, playerOn);
                await Broadcast(new NetworkMessage
                {
                    Type = NetworkMessageType.Substitution,
                    Payload = JsonSerializer.Serialize(subData)
                });
            }
        }

        public void CreateLobby(string name, string password, string hostPlayerId, string hostUsername)
        {
            GameState.CurrentLobby = new LobbyState
            {
                LobbyName = name,
                Password = password,
                HostPlayerId = hostPlayerId,
                MaxPlayers = 30
            };

            var hostPlayer = new Player
            {
                Id = hostPlayerId,
                Username = hostUsername,
                IsHuman = true,
                IsCoach = true
            };

            GameState.Players.Add(hostPlayer);
            GameState.CurrentLobby.ConnectedPlayerIds.Add(hostPlayerId);

            // Create two teams with default opposite colors
            var homeTeam = new Team
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Home Team",
                Side = TeamSide.Home,
                PlayerIds = new List<string>(),
                Jersey = new JerseyDesign { PrimaryColor = "#FF0000", SecondaryColor = "#FFFFFF" }
            };

            var awayTeam = new Team
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Away Team",
                Side = TeamSide.Away,
                PlayerIds = new List<string>(),
                Jersey = new JerseyDesign { PrimaryColor = "#0000FF", SecondaryColor = "#FFFFFF" }
            };

            GameState.Teams["home"] = homeTeam;
            GameState.Teams["away"] = awayTeam;

            OnLog?.Invoke($"Lobby '{name}' created by {hostUsername}");
        }

        public async Task StartGame()
        {
            if (GameState.CurrentLobby == null) return;

            GameState.CurrentPhase = GamePhase.CoinToss;

            // Assign human players to teams (AI players keep their existing team assignments)
            bool homeTurn = true;
            foreach (var playerId in GameState.CurrentLobby.ConnectedPlayerIds)
            {
                var player = GameState.Players.Find(p => p.Id == playerId);
                if (player == null) continue;

                var team = homeTurn ? GameState.Teams["home"] : GameState.Teams["away"];
                player.Team = homeTurn ? TeamSide.Home : TeamSide.Away;
                team.PlayerIds.Add(playerId);
                homeTurn = !homeTurn;
            }

            var homeTeam = GameState.Teams["home"];
            var awayTeam = GameState.Teams["away"];

            // Create match state
            var matchState = new MatchState
            {
                HomeTeamId = homeTeam.Id,
                AwayTeamId = awayTeam.Id,
                TeamPlayers = new Dictionary<string, List<Player>>
                {
                    [homeTeam.Id] = GameState.Players.Where(p => p.Team == TeamSide.Home).ToList(),
                    [awayTeam.Id] = GameState.Players.Where(p => p.Team == TeamSide.Away).ToList()
                }
            };

            GameState.CurrentMatch = matchState;
            // MatchEngine expects teams keyed by GUID, not by "home"/"away"
            var teamsByGuid = GameState.Teams.Values.ToDictionary(t => t.Id, t => t);
            MatchEngine = new MatchEngine(matchState, teamsByGuid);
            MatchEngine.SetupKickoff();

            GameState.CurrentPhase = GamePhase.Kickoff;

            OnLog?.Invoke("Game started!");
            await Broadcast(new NetworkMessage
            {
                Type = NetworkMessageType.GameStart,
                Payload = JsonSerializer.Serialize(new
                {
                    matchState,
                    teams = GameState.Teams
                })
            });
        }

        private void FillAIPlayers(Team team)
        {
            var positions = new[] { PlayerPosition.Goalkeeper, PlayerPosition.Defender, PlayerPosition.Defender,
                PlayerPosition.Defender, PlayerPosition.Defender, PlayerPosition.Midfielder,
                PlayerPosition.Midfielder, PlayerPosition.Midfielder, PlayerPosition.Midfielder,
                PlayerPosition.Forward, PlayerPosition.Forward };

            var substitutes = new[] { PlayerPosition.Goalkeeper, PlayerPosition.Defender, PlayerPosition.Midfielder, PlayerPosition.Forward };

            foreach (var pos in positions)
            {
                var aiPlayer = new Player
                {
                    Username = $"AI_{pos}_{team.PlayerIds.Count}",
                    Team = team.Side,
                    Position = pos,
                    IsHuman = false,
                    Stats = new PlayerStats()
                };
                GameState.Players.Add(aiPlayer);
                team.PlayerIds.Add(aiPlayer.Id);
            }

            AddSubstitutes(team, substitutes);
        }

        private void AddSubstitutes(Team team, PlayerPosition[] positions)
        {
            foreach (var pos in positions)
            {
                var sub = new Player
                {
                    Username = $"SUB_{team.PlayerIds.Count}",
                    Team = team.Side,
                    Position = pos,
                    IsHuman = false,
                    IsSubstitute = true,
                    Stats = new PlayerStats()
                };
                GameState.Players.Add(sub);
                team.PlayerIds.Add(sub.Id);
            }
        }

        public void UpdateGameLoop(float deltaTime)
        {
            if (MatchEngine == null || GameState.CurrentMatch == null) return;

            // Gather inputs from all human players
            var playerInputs = new Dictionary<string, InputState>();
            foreach (var player in GameState.Players.Where(p => p.IsHuman))
            {
                if (player.TargetX != 0 || player.TargetY != 0 || player.Speed != 0)
                {
                    playerInputs[player.Id] = new InputState
                    {
                        MoveX = player.TargetX - player.X,
                        MoveY = player.TargetY - player.Y,
                        MouseX = player.X + 100,
                        MouseY = player.Y,
                        LeftClick = false,
                        RightClick = false,
                        ShiftHeld = false
                    };
                }
            }

            MatchEngine.Update(deltaTime, playerInputs);
        }

        private async Task BroadcastGameState()
        {
            if (GameState.CurrentMatch == null) return;

            var statePayload = new
            {
                GameState.CurrentMatch.HomeScore,
                GameState.CurrentMatch.AwayScore,
                GameState.CurrentMatch.MatchMinute,
                GameState.CurrentMatch.MatchSecond,
                GameState.CurrentMatch.CurrentHalf,
                Ball = new { GameState.CurrentMatch.Ball.X, GameState.CurrentMatch.Ball.Y, GameState.CurrentMatch.Ball.VelocityX, GameState.CurrentMatch.Ball.VelocityY },
                Players = GameState.Players.Select(p => new
                {
                    p.Id, p.Username, p.Team, p.Position, p.X, p.Y,
                    p.Direction, p.HasBall, p.IsKnockedDown, p.IsSubstitute,
                    p.Speed
                }),
                GameState.CurrentPhase
            };

            await Broadcast(new NetworkMessage
            {
                Type = NetworkMessageType.GameStateUpdate,
                Payload = JsonSerializer.Serialize(statePayload)
            });
        }

        private async Task BroadcastLobbyUpdate()
        {
            var lobbyPayload = new
            {
                lobby = GameState.CurrentLobby,
                players = GameState.Players.Select(p => new
                {
                    p.Id, p.Username, p.Position, p.Team,
                    p.IsCoach, p.IsHuman,
                    appearance = new
                    {
                        p.Appearance.HairStyle, p.Appearance.HairColor,
                        p.Appearance.EyeColor, p.Appearance.SkinTone,
                        p.Appearance.FacialHair
                    },
                    stats = new
                    {
                        p.Stats.Speed, p.Stats.ShotStrength, p.Stats.Passing,
                        p.Stats.Dribbling, p.Stats.Defense, p.Stats.Stamina,
                        p.Stats.Aggression, p.Stats.Jumping, p.Stats.Accuracy,
                        p.Stats.Reflexes, p.Stats.StatPointsRemaining
                    }
                }),
                teams = GameState.Teams.Select(t => new
                {
                    t.Value.Id, t.Value.Name, t.Value.Side,
                    jersey = new
                    {
                        t.Value.Jersey.PrimaryColor,
                        t.Value.Jersey.SecondaryColor,
                        t.Value.Jersey.PixelArt
                    },
                    playerIds = t.Value.PlayerIds
                })
            };

            await Broadcast(new NetworkMessage
            {
                Type = NetworkMessageType.LobbyUpdate,
                Payload = JsonSerializer.Serialize(lobbyPayload)
            });
        }

        private async Task Broadcast(NetworkMessage message)
        {
            var json = JsonSerializer.Serialize(message) + "\n";
            var data = Encoding.UTF8.GetBytes(json);

            var deadClients = new List<string>();

            foreach (var (id, client) in _clients)
            {
                try
                {
                    if (_streams.TryGetValue(id, out var stream))
                    {
                        await stream.WriteAsync(data, 0, data.Length);
                        await stream.FlushAsync();
                    }
                }
                catch
                {
                    deadClients.Add(id);
                }
            }

            foreach (var id in deadClients)
            {
                DisconnectClient(id);
            }
        }

        private async Task SendToClient(string clientId, NetworkMessage message)
        {
            if (!_streams.TryGetValue(clientId, out var stream)) return;

            var json = JsonSerializer.Serialize(message) + "\n";
            var data = Encoding.UTF8.GetBytes(json);

            try
            {
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
            }
            catch
            {
                DisconnectClient(clientId);
            }
        }

        private void DisconnectClient(string clientId)
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                client.Close();
                _clients.Remove(clientId);
                _streams.Remove(clientId);

                var player = GameState.Players.Find(p => p.Id == clientId);
                if (player != null)
                {
                    GameState.Players.Remove(player);
                    GameState.CurrentLobby?.ConnectedPlayerIds.Remove(clientId);

                    foreach (var team in GameState.Teams.Values)
                    {
                        team.PlayerIds.Remove(clientId);
                    }
                }

                OnLog?.Invoke($"Client {clientId} disconnected");
            }
        }

        public void RemoveAIPlayer(string playerId)
        {
            var player = GameState.Players.Find(p => p.Id == playerId && !p.IsHuman);
            if (player == null) return;

            foreach (var team in GameState.Teams.Values)
            {
                team.PlayerIds.Remove(playerId);
            }
            GameState.Players.Remove(player);
            OnLog?.Invoke($"AI player {player.Username} removed");
        }

        public void SetPlayerTeam(string playerId, TeamSide team)
        {
            var player = GameState.Players.Find(p => p.Id == playerId);
            if (player == null) return;

            // Remove from old team
            foreach (var t in GameState.Teams.Values)
            {
                t.PlayerIds.Remove(playerId);
            }
            
            player.Team = team;
            var teamKey = team == TeamSide.Home ? "home" : "away";
            if (GameState.Teams.TryGetValue(teamKey, out var targetTeam))
            {
                targetTeam.PlayerIds.Add(playerId);
            }
        }

        public void AddAIPlayer(TeamSide side, PlayerPosition position, bool isSubstitute = false)
        {
            var team = side == TeamSide.Home ? GameState.Teams.GetValueOrDefault("home") : GameState.Teams.GetValueOrDefault("away");
            if (team == null) return;

            var aiPlayer = new Player
            {
                Username = $"AI_{position}_{team.PlayerIds.Count}",
                Team = side,
                Position = position,
                IsHuman = false,
                IsSubstitute = isSubstitute,
                Stats = new PlayerStats()
            };
            GameState.Players.Add(aiPlayer);
            team.PlayerIds.Add(aiPlayer.Id);
            OnLog?.Invoke($"AI player {aiPlayer.Username} added to {side} team");
        }

        public void FillTeamWithAI(TeamSide side)
        {
            var team = side == TeamSide.Home ? GameState.Teams.GetValueOrDefault("home") : GameState.Teams.GetValueOrDefault("away");
            if (team == null) return;

            // Count current non-substitute players on this team
            var currentPlayers = GameState.Players.Where(p => team.PlayerIds.Contains(p.Id) && !p.IsSubstitute).Count();
            int needed = 11 - currentPlayers;
            
            if (needed <= 0) return;

            // Positions to fill in order
            var positions = new Queue<PlayerPosition>(new[]
            {
                PlayerPosition.Goalkeeper,
                PlayerPosition.Defender, PlayerPosition.Defender, PlayerPosition.Defender, PlayerPosition.Defender,
                PlayerPosition.Midfielder, PlayerPosition.Midfielder, PlayerPosition.Midfielder, PlayerPosition.Midfielder,
                PlayerPosition.Forward, PlayerPosition.Forward
            });

            // Skip positions already filled
            var existingPositions = GameState.Players
                .Where(p => team.PlayerIds.Contains(p.Id) && !p.IsSubstitute)
                .Select(p => p.Position)
                .ToList();
            foreach (var existing in existingPositions)
            {
                if (positions.Contains(existing) && existing != PlayerPosition.Goalkeeper)
                {
                    // Only remove one of each position that exists
                    var tempList = positions.ToList();
                    int idx = tempList.IndexOf(existing);
                    if (idx >= 0 && idx > 0) // don't remove goalkeeper
                        tempList.RemoveAt(idx);
                    positions = new Queue<PlayerPosition>(tempList);
                }
            }

            for (int i = 0; i < needed && positions.Count > 0; i++)
            {
                var pos = positions.Dequeue();
                AddAIPlayer(side, pos, false);
            }

            // Add substitutes (4 subs)
            int currentSubs = GameState.Players.Where(p => team.PlayerIds.Contains(p.Id) && p.IsSubstitute).Count();
            int subsNeeded = 4 - currentSubs;
            var subPositions = new[] { PlayerPosition.Goalkeeper, PlayerPosition.Defender, PlayerPosition.Midfielder, PlayerPosition.Forward };
            for (int i = 0; i < subsNeeded && i < subPositions.Length; i++)
            {
                AddAIPlayer(side, subPositions[i], true);
            }

            OnLog?.Invoke($"Filled {side} team with AI players");
        }

        public void Stop()
        {
            _cts.Cancel();
            IsRunning = false;

            foreach (var client in _clients.Values)
            {
                client.Close();
            }

            _clients.Clear();
            _streams.Clear();

            _listener?.Stop();
            OnLog?.Invoke("Server stopped");
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }
    }

    // Data classes for serialization
    public class JoinLobbyData
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class JerseyUpdateData
    {
        public string TeamId { get; set; } = "";
        public string PrimaryColor { get; set; } = "#FF0000";
        public string SecondaryColor { get; set; } = "#FFFFFF";
        public int[,]? PixelArt { get; set; }
    }

    public class SubstitutionData
    {
        public string PlayerOffId { get; set; } = "";
        public string PlayerOnId { get; set; } = "";
    }
}