using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Google.Apis.Drive.v3.Data;
using System.Text;
using TribeBot.Bot.Modals;
using TribeBot.Bot.UI;
using TribeBot.Core.Interfaces;
using TribeBot.Services.Services;

namespace TribeBot.Bot.Handlers
{
    [Group("farm", "Farm registration commands")]
    public class FarmHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IFarmService _farmService;
        private readonly IFarmTribeService _farmTribeService;
        private readonly IFarmTribeAssignmentService _assignmentService;
        private readonly IMemberService _memberService;

        // officer role id
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong FarmManagerRoleId = 1458892024588669134;

        // Farm registration role
        private const ulong HogsRole = 1222668156271591485;

        // Tracks the farm being edited by the user (In memory)
        private static readonly Dictionary<ulong, string> _pendingFarmEdits = new();
        private static readonly Dictionary<ulong, ulong> _pendingBulkFarmTargets = new();

        public FarmHandler(
            IFarmService farmService,
            IFarmTribeService farmTribeService,
            IFarmTribeAssignmentService assignmentService,
            IMemberService memberService)
        {
            _farmService = farmService;
            _farmTribeService = farmTribeService;
            _assignmentService = assignmentService;
            _memberService = memberService;
        }

        // ======================================================
        // /farm add
        // ======================================================
        [RequireRole(HogsRole)]
        [SlashCommand("add", "Register a single farm")]
        public async Task AddFarm()
        {
            var modal = new ModalBuilder()
                .WithTitle("Register Farm")
                .WithCustomId("register")
                .AddTextInput(
                    label: "Farm Name",
                    customId: "farm_name",
                    style: TextInputStyle.Short,
                    required: true)
                .AddTextInput(
                    label: "Farm Ingame ID",
                    customId: "farm_id",
                    style: TextInputStyle.Short,
                    required: true);

            await RespondWithModalAsync(modal.Build());
        }

        // ======================================================
        // /farm bulk
        // ======================================================
        [RequireRole(HogsRole)]
        [SlashCommand("bulk", "Register multiple farms at once")]
        public async Task BulkAddFarms()
        {
            var modal = new ModalBuilder()
                .WithTitle("Register Farms (Max 15 PER REQUEST)")
                .WithCustomId("register_farm_bulk")
                .AddTextInput(
                    label: "Farm list (One per line: Name | ID)",
                    customId: "farm_list",
                    style: TextInputStyle.Paragraph,
                    placeholder:
@"FarmAlpha | 123456
FarmBravo | 654321
FarmCharlie | 987654",
                    required: true,
                    maxLength: 2000
                );

            await RespondWithModalAsync(modal.Build());
        }

        // =========================
        // FARM LIST (Sent to user in dm)
        // =========================
        [SlashCommand("list", "List your registered farms (sent via DM)")]
        public async Task ListMyFarms()
        {
            await DeferAsync(ephemeral: true);

            var userId = Context.User.Id.ToString();
            var farms = await _farmService.GetFarmsForUserAsync(userId);

            try
            {
                var dm = await Context.User.CreateDMChannelAsync();

                if (farms.Count == 0)
                {
                    await dm.SendMessageAsync("📭 You have **no registered farms**.");
                }
                else
                {
                    await dm.SendMessageAsync("🌾 **Your Registered Farms**");

                    var lines = farms.Select(f =>
                        $"• **{f.FarmName}** | ID: `{f.FarmId}`"
                    );

                    foreach (var chunk in ChunkLines(lines))
                        await dm.SendMessageAsync(chunk);
                }

                await FollowupAsync(
                    embed: EmbedHelper.Success("I've sent you a DM with your farms."),
                    ephemeral: true);
            }
            catch
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("I couldn’t DM you. Please enable DMs."),
                    ephemeral: true);
            }
        }

        // ======================================================
        // /farm remove <id>
        // ======================================================
        [SlashCommand("remove", "Remove one of your farms")]
        public async Task RemoveFarm(
            [Summary("farmid", "Farm ID")] string farmId)
        {
            await DeferAsync(ephemeral: true);

            var user = (SocketGuildUser)Context.User;

            try
            {
                await _farmService.RemoveFarmAsync(farmId, user.Id.ToString());

                await FollowupAsync(
                    embed: EmbedHelper.Success($"Farm `{farmId}` removed successfully."),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(ex.Message),
                    ephemeral: true);
            }
        }

        // ======================================================
        // /farm edit <id>
        // ======================================================
        [SlashCommand("edit", "Edit a farm you own")]
        public async Task EditFarm(
            [Summary("farmid", "Existing Farm ID")] string farmId)
        {
            var farm = await _farmService.GetFarmByIdAsync(farmId);
            if (farm == null)
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("Farm not found."),
                    ephemeral: true);
                return;
            }

            if (farm.OwnerDiscordId != Context.User.Id.ToString())
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not own this farm."),
                    ephemeral: true);
                return;
            }

            _pendingFarmEdits[Context.User.Id] = farm.FarmId;

            var modal = new ModalBuilder()
                .WithTitle("Edit Farm")
                .WithCustomId("edit_farm")
                .AddTextInput("Farm Name", "farm_name", value: farm.FarmName, required: true)
                .AddTextInput("Farm ID", "farm_id", value: farm.FarmId, required: true);

            await RespondWithModalAsync(modal.Build());
        }

        // ======================================================
        // /farm status
        // ======================================================
        [SlashCommand("status", "View your farm and farm tribe status")]
        public async Task FarmStatus()
        {
            await DeferAsync(ephemeral: true);

            string userId = Context.User.Id.ToString();

            var farms = await _farmService.GetFarmsForUserAsync(userId);
            int farmCount = farms.Count;

            var assignment = await _assignmentService.GetAssignmentForUserAsync(userId);

            string tribeName = "No tribe assigned";
            string tribeSlots = "";
            string footerNote = "";

            if (assignment != null)
            {
                var tribe = await _farmTribeService.GetFarmTribeByIdAsync(assignment.FarmTribeId);
                if (tribe != null)
                {
                    tribeName = tribe.FarmTribeName;
                    tribeSlots = $"Slots: {tribe.UsedSlots} / {tribe.TotalSlots}";
                }
            }
            else
            {
                footerNote = "Contact an officer if you need a farm tribe assignment.";
            }

            var desc = new StringBuilder();

            desc.AppendLine($"**Farm Tribe:** {tribeName}");
            if (!string.IsNullOrEmpty(tribeSlots))
                desc.AppendLine($"**{tribeSlots}**");

            desc.AppendLine($"**Your Farms:** {farmCount}");
            desc.AppendLine();

            if (farmCount == 0)
            {
                desc.AppendLine("You have no registered farms.");
            }
            else
            {
                desc.AppendLine("**Farms:**");
                foreach (var farm in farms)
                    desc.AppendLine($"• **{farm.FarmName}** — `{farm.FarmId}`");
            }

            if (!string.IsNullOrEmpty(footerNote))
            {
                desc.AppendLine();
                desc.AppendLine(footerNote);
            }

            await FollowupAsync(
                embed: EmbedHelper.Info("🌾 Farm Status", desc.ToString()),
                ephemeral: true);
        }

        // ======================================================
        // /farm track
        // ======================================================
        [SlashCommand("track", "Track a farm by ID and see who owns it")]
        public async Task TrackFarm(
            [Summary("farmid", "Farm ingame ID")] string farmId)
        {
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId || r.Id == FarmManagerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var farm = await _farmService.GetFarmByIdAsync(farmId);
            if (farm == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Info("Farm Tracking",
                        $"No registered farm found with ID `{farmId}`."),
                    ephemeral: true);
                return;
            }

            SocketGuildUser? ownerUser = null;
            if (ulong.TryParse(farm.OwnerDiscordId, out var ownerId))
                ownerUser = Context.Guild.GetUser(ownerId);

            string ownerDisplay = ownerUser != null
                ? ownerUser.Mention
                : farm.OwnerIngameName;

            string tribeDisplay = "❌ Not assigned";

            var assignment = await _assignmentService.GetAssignmentForUserAsync(farm.OwnerDiscordId);

            if (assignment != null)
            {
                var tribe = await _farmTribeService.GetFarmTribeByIdAsync(assignment.FarmTribeId);
                if (tribe != null)
                    tribeDisplay = $"**{tribe.FarmTribeName}** (`{tribe.FarmTribeId}`)";
            }

            await FollowupAsync(
                embed: EmbedHelper.Info(
                    "Farm Tracking Result",
                    $"**Farm ID:** `{farm.FarmId}`\n" +
                    $"**Farm Name:** `{farm.FarmName}`\n" +
                    $"**Owner:** {ownerDisplay}\n" +
                    $"**Farm Tribe:** {tribeDisplay}"
                ),
                ephemeral: true);
        }

        // ======================================================
        // /farm addfor
        // ======================================================
        [SlashCommand("addfor", "Register a farm for another player (Officer/Farm manager only)")]
        public async Task AddFarmFor(
            SocketGuildUser targetUser,
            string farmName,
            string farmId)
        {
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId || r.Id == FarmManagerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            try
            {
                await _farmService.RegisterFarmAsync(
                    farmId.Trim(),
                    farmName.Trim(),
                    targetUser.Id.ToString(),
                    targetUser.Nickname ?? targetUser.Username
                );

                try
                {
                    var dm = await targetUser.CreateDMChannelAsync();

                    await dm.SendMessageAsync(
                        embed: EmbedHelper.Info(
                            "🌾 Farm Registered",
                            $"An officer has registered a farm for you.\n\n" +
                            $"**Farm Name:** {farmName}\n" +
                            $"**Farm ID:** `{farmId}`\n\n" +
                            $"You can view all your farms using `/farm list`."
                        ));
                }
                catch { }

                await FollowupAsync(
                    embed: EmbedHelper.Success(
                        $"Farm **{farmName}** (`{farmId}`) has been registered for {targetUser.Mention}."),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(ex.Message),
                    ephemeral: true);
            }
        }

        // ======================================================
        // /farm bulkfor
        // ======================================================
        [SlashCommand("bulkfor", "Register multiple farms for a player (Officer/Farm manager only)")]
        public async Task BulkRegisterForUser(SocketGuildUser targetUser)
        {
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId || r.Id == FarmManagerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            _pendingBulkFarmTargets[Context.User.Id] = targetUser.Id;

            var modal = new ModalBuilder()
                .WithTitle($"Register Farms for {targetUser.DisplayName}")
                .WithCustomId("register_farm_bulk_for")
                .AddTextInput(
                    "Farm list (One per line: Name | ID)",
                    "farm_list",
                    TextInputStyle.Paragraph,
                    placeholder:
@"FarmAlpha | 123456
FarmBravo | 654321
FarmCharlie | 987654",
                    required: true,
                    maxLength: 2000
                );

            await RespondWithModalAsync(modal.Build());
        }

        [ModalInteraction("register_farm_bulk_for", ignoreGroupNames: true)]
        public async Task HandleBulkRegisterForUser(RegisterFarmBulkModal modal)
        {
            await DeferAsync(ephemeral: true);

            if (!_pendingBulkFarmTargets.TryGetValue(Context.User.Id, out var targetUserId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Bulk registration session expired. Please run `/farm bulkfor` again."),
                    ephemeral: true);
                return;
            }

            _pendingBulkFarmTargets.Remove(Context.User.Id);

            var targetUser = Context.Guild.GetUser(targetUserId);

            if (targetUser == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Target user not found."),
                    ephemeral: true);
                return;
            }

            var lines = modal.FarmList
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (lines.Count > 15)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(
                        $"You submitted **{lines.Count} farms**. Maximum **15 farms per bulk submission**."),
                    ephemeral: true);
                return;
            }

            int successCount = 0;
            var failures = new List<string>();

            foreach (var line in lines)
            {
                var partsLine = line.Split('|', StringSplitOptions.RemoveEmptyEntries);

                if (partsLine.Length != 2)
                {
                    failures.Add($"{line} — Invalid format (expected: Name | ID)");
                    continue;
                }

                string farmName = partsLine[0].Trim();
                string farmId = partsLine[1].Trim();

                if (!long.TryParse(farmId, out _))
                {
                    failures.Add($"{farmName} | {farmId} — Farm ID must be numeric");
                    continue;
                }

                try
                {
                    await _farmService.RegisterFarmAsync(
                        farmId,
                        farmName,
                        targetUser.Id.ToString(),
                        targetUser.Nickname ?? targetUser.Username
                    );

                    successCount++;
                    await Task.Delay(250);
                }
                catch (Exception ex)
                {
                    failures.Add($"{farmName} | {farmId} — {ex.Message}");
                }
            }

            try
            {
                var dm = await targetUser.CreateDMChannelAsync();

                await dm.SendMessageAsync(
                    embed: EmbedHelper.Info(
                        "🌾 Farms Registered",
                        $"An officer has registered **{successCount} farms** for you.\n\n" +
                        $"You can check them using `/farm list`."
                    ));
            }
            catch { }

            var response = new StringBuilder();

            response.AppendLine($"✅ **{successCount} farms registered** for {targetUser.Mention}.");

            if (failures.Count > 0)
            {
                response.AppendLine();
                response.AppendLine($"⚠️ **{failures.Count} farms failed:**");

                foreach (var f in failures.Take(5))
                    response.AppendLine($"• {f}");

                if (failures.Count > 5)
                    response.AppendLine($"...and {failures.Count - 5} more.");
            }

            await FollowupAsync(
                embed: EmbedHelper.Info("Officer Bulk Farm Registration", response.ToString()),
                ephemeral: true);
        }

        // ======================================================
        // Farm inactive
        // ======================================================

        [SlashCommand("inactive", "Notify a player that a farm appears inactive")]
        public async Task MarkFarmInactive(
            [Summary("farmid", "Farm ingame ID")] string farmId)
        {
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r =>
                    r.Id == OfficerRoleId ||
                    r.Id == FarmManagerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var farm = await _farmService.GetFarmByIdAsync(farmId);

            if (farm == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error($"No registered farm found with ID `{farmId}`."),
                    ephemeral: true);
                return;
            }

            if (!ulong.TryParse(farm.OwnerDiscordId, out var ownerId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Farm owner Discord ID is invalid."),
                    ephemeral: true);
                return;
            }

            var owner = Context.Guild.GetUser(ownerId);

            if (owner == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Warning(
                        "Farm found, but the owner is no longer in the server."),
                    ephemeral: true);
                return;
            }

            try
            {
                var dm = await owner.CreateDMChannelAsync();

                await dm.SendMessageAsync(
                    embed: EmbedHelper.Info(
                        "Inactive Farm Notice",
                        $"One of your farms may be inactive and should be checked.\n\n" +
                        $"**Farm Name:** {farm.FarmName}\n" +
                        $"**Farm ID:** `{farm.FarmId}`")
                );
            }
            catch
            {
                await FollowupAsync(
                    $"Failed to send DM to **{farm.OwnerIngameName}** (privacy settings or bot blocked).",
                    ephemeral: true);
                return;
            }

            await FollowupAsync(
                embed: EmbedHelper.Success(
                    $"The owner ({farm.OwnerIngameName}) of farm `{farm.FarmId}` has been notified."),
                ephemeral: true);
        }


        // ======================================================
        // Farm inactive bulk
        // ======================================================

        [SlashCommand("inactivebulk", "Notify a farm owner that multiple farms may be offline")]
        public async Task MarkFarmsInactiveBulkByFarmId(
            [Summary("farmid", "Any farm ID owned by the player")] string farmId)
        {
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r =>
                    r.Id == OfficerRoleId ||
                    r.Id == FarmManagerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var farm = await _farmService.GetFarmByIdAsync(farmId);

            if (farm == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error($"No registered farm found with ID `{farmId}`."),
                    ephemeral: true);
                return;
            }

            if (!ulong.TryParse(farm.OwnerDiscordId, out var ownerId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Farm owner has an invalid Discord ID."),
                    ephemeral: true);
                return;
            }

            var owner = Context.Guild.GetUser(ownerId);

            if (owner == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Warning(
                        "Farm found, but the owner is no longer in this server."),
                    ephemeral: true);
                return;
            }

            try
            {
                var dm = await owner.CreateDMChannelAsync();

                await dm.SendMessageAsync(
                    embed: EmbedHelper.Info(
                        "Farm Activity Check",
                        "An officer has noticed that several of your farms may have been " +
                        "offline for an extended period of time. Please review them when " +
                        "you have a moment."
                    )
                );
            }
            catch
            {
                await FollowupAsync(
                    $"Failed to send DM to **{farm.OwnerIngameName}** (privacy settings or bot blocked).",
                    ephemeral: true);
                return;
            }

            await FollowupAsync(
                embed: EmbedHelper.Success(
                    $"Owner **{farm.OwnerIngameName}** has been notified about potential offline farms."),
                ephemeral: true);
        }

        // ======================================================
        // HELPERS
        // ======================================================
        private static List<string> ChunkLines(IEnumerable<string> lines, int maxChars = 1800)
        {
            var chunks = new List<string>();
            var sb = new StringBuilder();

            foreach (var line in lines)
            {
                if (sb.Length + line.Length + 2 > maxChars)
                {
                    chunks.Add(sb.ToString());
                    sb.Clear();
                }

                sb.AppendLine(line);
            }

            if (sb.Length > 0)
                chunks.Add(sb.ToString());

            return chunks;
        }
    }
}