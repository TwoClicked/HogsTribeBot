using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;

public class EventNotifyRoleHandler : InteractionModuleBase<SocketInteractionContext>
{
    private const ulong HogsEventsRoleId = 1448513656542199880; //HogEvent role Hogs event id : 1448513656542199880 Dev test role id 1439972286877794314
    private const ulong OfficerRoleId = 1222665812775534592;

    [SlashCommand("eventnotifications", "Post the opt-in button for DM event notifications.")]
    public async Task PostOptInMessage()
    {

        var user = Context.User as SocketGuildUser;

        if (!user.Roles.Any(r => r.Id == OfficerRoleId))
        {
            await RespondAsync("You do not have permission to use this command.", ephemeral: true);
            return;
        }

        var button = new ComponentBuilder()
            .WithButton("Toggle Event Notifications", "hogsevt:toggle", ButtonStyle.Primary);


        await RespondAsync(
            embed: new EmbedBuilder()
                .WithTitle("Event Notifications")
                .WithDescription(
                    "Click the button below to **opt-in** or **opt-out** of DM notifications for scheduled events.\n" +
                    "Only members with this role will receive event reminders.")
                .WithColor(Color.Gold)
                .Build(),
            components: button.Build()
        );
    }

    [ComponentInteraction("hogsevt:toggle")]
    public async Task ToggleEventRole()
    {
        var user = Context.User as SocketGuildUser;
        var role = user.Guild.Roles.FirstOrDefault(r => r.Id == HogsEventsRoleId);

        if (role == null)
        {
            await RespondAsync("Error: HogsEvents role not found.", ephemeral: true);
            return;
        }

        if (user.Roles.Any(r => r.Id == HogsEventsRoleId))
        {
            await user.RemoveRoleAsync(role);
            await RespondAsync("You have **opted out** of event notifications.", ephemeral: true);
        }
        else
        {
            await user.AddRoleAsync(role);
            await RespondAsync("You are now **subscribed** to event notifications.", ephemeral: true);
        }
    }
}
