using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using System.Threading.Tasks;
using TribeBot.Core.Interfaces;
using TribeBot.Bot.UI; // EmbedHelper

namespace TribeBot.Bot.Handlers
{
    public class UpdateHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IMemberService _memberService;

        public UpdateHandler(IMemberService memberService)
        {
            _memberService = memberService;
        }

        // ============================================================
        //  Slash Command: /updateprofile
        // ============================================================
        [SlashCommand("updateprofile", "Update your HOGS profile using a clean form.")]
        public async Task UpdateProfile()
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(Context.User.Id.ToString());
            if (member == null)
            {
                await RespondAsync(embed: EmbedHelper.Error("You must register before updating your profile."), ephemeral: true);
                return;
            }

            // Build a modal + pre-fill the user's current values
            var modal = new ModalBuilder()
                .WithTitle("Update Your HOGS Profile")
                .WithCustomId("update_profile_form")
                .AddTextInput("In-Game Name", "ign", TextInputStyle.Short, value: member.IngameName, required: true)
                .AddTextInput("In-Game ID", "id", TextInputStyle.Short, value: member.IngameId, required: true)
                .AddTextInput("Might", "might", TextInputStyle.Short, value: member.Might.ToString(), required: true)
                .AddTextInput("Kill Points", "kills", TextInputStyle.Short, value: member.KillPoints.ToString(), required: true)
                .AddTextInput("Collector Level", "collector", TextInputStyle.Short, value: member.CollectorLevel.ToString(), required: true);

            await RespondWithModalAsync(modal.Build());
        }


        // ============================================================
        //  Modal Response Handling
        // ============================================================
        [ModalInteraction("update_profile_form")]
        public async Task HandleModal(UpdateProfileModal modal)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(Context.User.Id.ToString());
            if (member == null)
            {
                await RespondAsync(embed: EmbedHelper.Error("You must register first."), ephemeral: true);
                return;
            }

            // Validate and parse fields
            if (!int.TryParse(modal.Might, out int might) ||
                might < 0 || might > 3000000000)
            {
                await RespondAsync(embed: EmbedHelper.Error("Invalid Might (0–3,000,000,000)."), ephemeral: true);
                return;
            }

            if (!long.TryParse(modal.Kills, out long kills) ||
                kills < 0 || kills > 500000000000)
            {
                await RespondAsync(embed: EmbedHelper.Error("Invalid Kill Points."), ephemeral: true);
                return;
            }

            if (!int.TryParse(modal.Collector, out int collector) ||
                collector < 0 || collector > 100)
            {
                await RespondAsync(embed: EmbedHelper.Error("Invalid Collector Level (0–100)."), ephemeral: true);
                return;
            }

            // Apply updates
            member.IngameName = modal.IngameName;
            member.IngameId = modal.IngameId;
            member.Might = might;
            member.KillPoints = kills;
            member.CollectorLevel = collector;
            member.LastUpdatedUTC = DateTime.UtcNow;

            await _memberService.RegisterOrUpdateAsync(member);

            await RespondAsync(
                embed: EmbedHelper.Success("Your profile has been successfully updated!"),
                ephemeral: true
            );
        }
    }

    // ============================================================
    //  Strongly-Typed Modal Class
    // ============================================================
    public class UpdateProfileModal : IModal
    {
        public string Title => "Update Your HOGS Profile";

        [InputLabel("In-Game Name")]
        [ModalTextInput("ign", placeholder: "Your character name")]
        public string IngameName { get; set; }

        [InputLabel("In-Game ID")]
        [ModalTextInput("id", placeholder: "Numeric ID")]
        public string IngameId { get; set; }

        [InputLabel("Might")]
        [ModalTextInput("might")]
        public string Might { get; set; }

        [InputLabel("Kill Points")]
        [ModalTextInput("kills")]
        public string Kills { get; set; }

        [InputLabel("Collector Level")]
        [ModalTextInput("collector")]
        public string Collector { get; set; }
    }
}
