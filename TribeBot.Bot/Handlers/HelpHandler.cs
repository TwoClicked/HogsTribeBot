using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.UI;

namespace TribeBot.Bot.Handlers
{
    public class HelpHandler
    {
        private readonly DiscordSocketClient _client;

        private readonly string[] Order =
            { "general", "registration", "update", "reign", "bank", "fines", "polls", "creator" };

        public HelpHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.InteractionCreated += HandleInteractionAsync;
        }

        // ============================================================
        // LOCAL EMBED HELPERS (Style-2)
        // ============================================================
        private Embed BuildEmbed(string title, string desc, Color color)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(desc)
                .WithColor(color)
                .WithFooter($"{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                .Build();
        }

        private Task SendError(SocketMessage msg, string text)
            => msg.Channel.SendMessageAsync(embed:
                BuildEmbed("❌ Error", text, Color.Red));


        // ============================================================
        // MAIN COMMAND
        // ============================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim().ToLower();

            if (!content.StartsWith("!help"))
                return false;

            // EXACT command: !help → show menu
            if (content == "!help")
            {
                await message.Channel.SendMessageAsync(
                    embed: HelpEmbeds.General(),
                    components: HelpComponents.Build("general").Build()
                );
                return true;
            }

            // Anything ELSE → invalid help command
            await SendError(message,
                "Unknown help option.\nUse **`!help`** to open the help menu.");

            return true;
        }


        // ============================================================
        // INTERACTIONS (unchanged)
        // ============================================================
        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            if (interaction is not SocketMessageComponent component)
                return;

            string id = component.Data.CustomId;
            string selected = GetSelectedCategory(component);

            if (id == "help_prev")
                selected = Prev(selected);

            if (id == "help_next")
                selected = Next(selected);

            if (id.EndsWith("_btn"))
                selected = id.Replace("_btn", "");

            Embed embed = selected switch
            {
                "general" => HelpEmbeds.General(),
                "registration" => HelpEmbeds.Registration(),
                "update" => HelpEmbeds.Update(),
                "reign" => HelpEmbeds.Reign(),
                "bank" => HelpEmbeds.Bank(),
                "fines" => HelpEmbeds.Fines(),
                "polls" => HelpEmbeds.Polls(),
                "creator" => HelpEmbeds.Creator(),
                _ => HelpEmbeds.General()
            };

            await component.UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = HelpComponents.Build(selected).Build();
            });
        }

        private string GetSelectedCategory(SocketMessageComponent component)
        {
            if (component.Data.CustomId == "helpMenu")
                return component.Data.Values.First();

            if (component.Data.CustomId.EndsWith("_btn"))
                return component.Data.CustomId.Replace("_btn", "");

            foreach (string cat in Order)
                if (component.Message.Embeds.First().Title.ToLower().Contains(cat))
                    return cat;

            return "general";
        }

        private string Prev(string current)
        {
            int index = System.Array.IndexOf(Order, current);
            return Order[(index - 1 + Order.Length) % Order.Length];
        }

        private string Next(string current)
        {
            int index = System.Array.IndexOf(Order, current);
            return Order[(index + 1) % Order.Length];
        }
    }
}
