using Discord;
using Discord.WebSocket;

namespace TribeBot.Bot.UI
{
    public static class HelpComponents
    {
        public static ComponentBuilder Build(string selected)
        {
            var builder = new ComponentBuilder();

            var menu = new SelectMenuBuilder()
                .WithCustomId("helpMenu")
                .WithPlaceholder("Choose category…")
                .AddOption("General", "general", isDefault: selected == "general")
                .AddOption("Registration", "registration", isDefault: selected == "registration")
                .AddOption("Update", "update", isDefault: selected == "update")
                .AddOption("Reign Event", "reign", isDefault: selected == "reign")
                .AddOption("Bank", "bank", isDefault: selected == "bank")
                .AddOption("Fines", "fines", isDefault: selected == "fines")
                .AddOption("Polls", "polls", isDefault: selected == "polls")
                .AddOption("Content Creator", "creator", isDefault: selected == "creator");

            builder.WithSelectMenu(menu);

            builder.WithButton("General", "general_btn", ButtonStyle.Primary);
            builder.WithButton("Registration", "registration_btn", ButtonStyle.Primary);
            builder.WithButton("Bank", "bank_btn", ButtonStyle.Success);
            builder.WithButton("Fines", "fines_btn", ButtonStyle.Danger);

            builder.WithButton("⬅️ Prev", "help_prev", ButtonStyle.Secondary);
            builder.WithButton("Next ➡️", "help_next", ButtonStyle.Secondary);

            return builder;
        }
    }
}
