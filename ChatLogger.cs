using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace ChatLogger
{
    public class ChatLogger : BasePlugin, IPluginConfig<ChatLoggerConfig>
    {
        public override string ModuleName => "Chat Logger";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        public ChatLoggerConfig Config { get; set; } = new();
        private readonly HttpClient _httpClient;
        private readonly CancellationTokenSource _cts = new();

        public ChatLogger()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public void OnConfigParsed(ChatLoggerConfig config)
        {
            Config = config;
        }

        public override void Load(bool hotReload)
        {
            AddCommandListener("say", OnPlayerChat);
            AddCommandListener("say_team", OnPlayerChat);
        }

        public override void Unload(bool hotReload)
        {
            _cts.Cancel();
            _cts.Dispose();

            RemoveCommandListener("say", OnPlayerChat, HookMode.Pre);
            RemoveCommandListener("say_team", OnPlayerChat, HookMode.Pre);

            _httpClient.Dispose();
            base.Unload(hotReload);
        }

        private HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info)
        {
            try
            {
                if (!ShouldProcessMessage(player, info))
                {
                    return HookResult.Continue;
                }

                ChatMessageData? messageData = PrepareMessageData(player!, info);
                if (messageData == null)
                {
                    return HookResult.Continue;
                }

                _ = Task.Run(() => ProcessMessageAsync(messageData, _cts.Token))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            Logger.LogError($"Ошибка обработки сообщения: {t.Exception?.Flatten().InnerException}");
                        }
                    });

                return HookResult.Continue;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Critical error: {ex}");
                return HookResult.Continue;
            }
        }

        private bool ShouldProcessMessage(CCSPlayerController? player, CommandInfo info)
        {
            return player != null
                && player.IsValid
                && !player.IsBot
                && !player.IsHLTV
                && player.Connected == PlayerConnectedState.PlayerConnected
                && !IsCommand(info.GetArg(1));
        }

        private bool IsCommand(string message)
        {
            List<string> commandPrefixes = Config.CommandPrefixes;
            return commandPrefixes.Any(message.StartsWith)
                || message.Equals("rtv", StringComparison.OrdinalIgnoreCase);
        }

        private static ChatMessageData? PrepareMessageData(CCSPlayerController player, CommandInfo info)
        {
            try
            {
                ulong? steamId = player.AuthorizedSteamID?.SteamId64;
                return steamId == null || string.IsNullOrWhiteSpace(info.GetArg(1))
                    ? null
                    : new ChatMessageData
                    {
                        PlayerName = player.PlayerName,
                        Message = info.GetArg(1),
                        SteamId = steamId.Value,
                        IsTeamChat = info.GetArg(0) == "say_team",
                        Timestamp = DateTime.UtcNow,
                        Hostname = ConVar.Find("hostname")?.StringValue ?? "Unknown"
                    };
            }
            catch
            {
                return null;
            }
        }

        private async Task ProcessMessageAsync(ChatMessageData data, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(Config.DiscordWebhookUrl))
                {
                    return;
                }

                string payload = BuildDiscordPayload(data);
                await SendDiscordMessageAsync(payload, ct);
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка обработки сообщения: {ex.Message}");
            }
        }

        private string BuildDiscordPayload(ChatMessageData data)
        {
            List<object> fields =
            [
        new
        {
            name = $"{Localizer["cl.Message"].Value} ({(data.IsTeamChat ? "Командный чат" : "Общий чат")})",
            value = data.Message,
            inline = false
        },
        new
        {
            name = Localizer["cl.SteamID"].Value,
            value = $"https://steamcommunity.com/id/{data.SteamId}/",
            inline = false
        },
        new
        {
            name = Localizer["cl.Date"].Value,
            value = data.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            inline = false
        }
    ];

            var embed = new
            {
                title = $"{Localizer["cl.Title"].Value} {data.PlayerName}",
                color = 2826045,
                description = $"Сервер: {data.Hostname}",
                fields
            };

            return JsonConvert.SerializeObject(new { embeds = new[] { embed } });
        }

        private async Task SendDiscordMessageAsync(string payload, CancellationToken ct)
        {
            using StringContent content = new(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _httpClient.PostAsync(Config.DiscordWebhookUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync(ct);
                Logger.LogError($"Discord API error: {response.StatusCode} - {responseBody}");
            }
        }
    }

    public class ChatMessageData
    {
        public string PlayerName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ulong SteamId { get; set; }
        public bool IsTeamChat { get; set; }
        public DateTime Timestamp { get; set; }
        public string Hostname { get; set; } = string.Empty;
    }
}