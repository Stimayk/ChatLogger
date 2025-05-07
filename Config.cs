using CounterStrikeSharp.API.Core;

namespace ChatLogger
{
    public class ChatLoggerConfig : BasePluginConfig
    {
        public string? DiscordWebhookUrl { get; set; } = string.Empty;
    }
}
