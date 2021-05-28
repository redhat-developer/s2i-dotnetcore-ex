using System;
using System.Collections.Generic;
using System.Text;
using Discord;
using Discord.Webhook;

namespace MarketPriceGap
{
    public class Discord
    {
        public ulong WebHookId { get; private set; }
        public string WebHookToken { get; private set; }

        public async void Send(string title, string message, Color color)
        {
            using (var client = new DiscordWebhookClient(WebHookId, WebHookToken))
            {
                var embed = new EmbedBuilder
                {
                    Title = title,
                    Description = message,
                    Color = color,
                    Timestamp = DateTime.Now,
                };

                await client.SendMessageAsync(text: "", embeds: new[] { embed.Build() });
            }
        }

        public Discord(ulong webhookId, string webhookToken)
        {
            WebHookId = webhookId;
            WebHookToken = webhookToken;
        }
    }
}
