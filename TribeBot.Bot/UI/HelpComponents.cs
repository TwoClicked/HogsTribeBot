using Discord;
using Discord.WebSocket;

namespace TribeBot.Bot.UI
{
    public static class HelpComponents
    {
        public static ComponentBuilder Build(string selected)
        {
            var builder = new ComponentBuilder();

            // ============================
            // UPDATED DROPDOWN MENU
            // ============================
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
                .AddOption("Content Creator", "creator", isDefault: selected == "creator")
                .AddOption("Titles", "titles", isDefault: selected == "titles")
                .AddOption("Sworn Vengeance", "sworn", isDefault: selected == "sworn")
                .AddOption("Events", "events", isDefault: selected == "events")
                .AddOption("Farms", "farms", isDefault: selected == "farms")
                .AddOption("Farm Tribes", "farmtribes", isDefault: selected == "farmtribes")
                .AddOption("Raid Signups", "raids", isDefault: selected == "raids");

            builder.WithSelectMenu(menu);
            return builder;
        }
    }
}
