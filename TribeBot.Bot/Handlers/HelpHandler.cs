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

        // UPDATED ORDER — now includes titles, sworn, events
        private readonly string[] Order =
        {
            "general",
            "registration",
            "update",
            "reign",
            "bank",
            "fines",
            "polls",
            "creator",
            "titles",
            "sworn",
            "events"
        };

        public HelpHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.InteractionCreated += HandleInteractionAsync;
        }

        // ============================================================
        // MAIN COMMAND — !help
        // ============================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim().ToLower();

            if (!content.StartsWith("!help"))
                return false;

            if (content == "!help")
            {
                await message.Channel.SendMessageAsync(
                    embed: HelpEmbeds.General(),
                    components: HelpComponents.Build("general").Build()
                );
                return true;
            }

            await message.Channel.SendMessageAsync(
                embed: EmbedHelper.Error(
                    "Unknown help option.\nUse **`!help`** to open the help menu."
                )
            );

            return true;
        }

        // ============================================================
        // INTERACTION HANDLING — button & dropdown navigation
        // ============================================================
        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            if (interaction is not SocketMessageComponent component)
                return;

            // Filter out unrelated components
            if (!IsHelpComponent(component.Data.CustomId))
                return;

            string id = component.Data.CustomId;
            string selected = GetSelectedCategory(component);

            if (id == "help_prev")
                selected = Prev(selected);

            if (id == "help_next")
                selected = Next(selected);

            if (id.EndsWith("_btn"))
                selected = id.Replace("_btn", "");

            // Build selected page embed
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
                "titles" => HelpEmbeds.Titles(),
                "sworn" => HelpEmbeds.Sworn(),
                "events" => HelpEmbeds.Events(),
                _ => HelpEmbeds.General()
            };

            await component.UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Components = HelpComponents.Build(selected).Build();
            });
        }

        private bool IsHelpComponent(string customId)
        {
            return customId.StartsWith("help_")
                || customId.EndsWith("_btn")
                || customId == "helpMenu";
        }

        // Identify current help category safely
        private string GetSelectedCategory(SocketMessageComponent component)
        {
            if (component.Data.CustomId == "helpMenu")
                return component.Data.Values.First();

            if (component.Data.CustomId.EndsWith("_btn"))
                return component.Data.CustomId.Replace("_btn", "");

            // Match category by embed title text
            string title = component.Message.Embeds.First().Title.ToLower();

            foreach (string cat in Order)
                if (title.Contains(cat))
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
