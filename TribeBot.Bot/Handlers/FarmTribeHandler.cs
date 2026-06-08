using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Bot.Handlers;
using TribeBot.Bot.Modals;
using TribeBot.Bot.UI;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Services.Services;


namespace TribeBot.Bot.Handlers
{
    [Group("farmtribe", "Farm tribe management commands")]
    public class FarmTribeHandler : InteractionModuleBase<SocketInteractionContext>
    {

        private readonly IFarmTribeService _farmTribeService;
        private readonly IFarmTribeAssignmentService _assignmentService;
        private readonly IMemberService _memberService;
        private readonly IFarmService _farmService;



        // Officer role 
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong OfficerLogChannelId = 1440211043820507217;
        private const ulong FarmOfficerChannelId = 1453112047716794418;
        private const ulong FarmOfficerTaskRoleId = 1453112186334347418;

        // TEMP: Hardcoded farm tribe > officer mapping 
        private static readonly Dictionary<string, ulong> FarmTribeOfficerRoles =
             new(StringComparer.OrdinalIgnoreCase)
            {
                // FarmTribeName → Officer Role ID
                { "BRF",  1453112186334347418 },
                { "GRV",  1453124084048335063 },
                { "SAGE", 1453124213656387785 },
                { "FLNK", 1453124266106163330 },
                { "DDRG", 1453124266827448330 },
                { "K33F", 1453124267494342952 },
                { "QF33", 1453124270963036244 },
                { "FROG", 1453124424592134357 },
                { "HOPA", 1453124427041738866 },
                { "GRZN", 1453124429906317477 },
                { "BIRD", 1453124432590536774 },
                { "FWOG", 1453124579160621086 },
                { "CAKE", 1453124619027480717 },


                // Add more tribes here
            };

        // Tracks which farm tribe a user is currently editing
        private static readonly Dictionary<ulong, string> _pendingEdits = new();
        // Tracks which farm tribe a user is attempting to delete
        private static readonly Dictionary<ulong, string> _pendingDeletes = new();



        public FarmTribeHandler(
            IFarmTribeService farmTribeService,
            IFarmTribeAssignmentService assignmentService,
            IMemberService memberService,
            IFarmService farmService)
        {
            _farmTribeService = farmTribeService;
            _assignmentService = assignmentService;
            _memberService = memberService;
            _farmService = farmService;
        }


        // =====================================================================
        // REGISTER A TRIBE
        // =====================================================================

        [SlashCommand("register", "Register a new farm tribe")]
        public async Task RegisterFarmTribe()
        {
            if (Context.User is not SocketGuildUser user ||
                !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."));
                return;
            }

            var modal = new ModalBuilder()
                .WithTitle("Register Farm Tribe")
                .WithCustomId("register_farm_tribe")
                .AddTextInput(
                    "Farm Tribe Name",
                    "farmtribe_name",
                    TextInputStyle.Short,
                    placeholder: "BRF",
                    required: true)
                .AddTextInput(
                    "Total Farm Slots",
                    "farmtribe_slots",
                    TextInputStyle.Short,
                    placeholder: "160",
                    required: true);

            await RespondWithModalAsync(modal.Build());
        }

        // =====================================================================
        // SHOW A LIST OF ALL TRIBES
        // =====================================================================

        [SlashCommand("list", "List all farm tribes")]
        public async Task ListFarmTribes()
        {
            if (Context.User is not SocketGuildUser user ||
                !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission."));
                return;
            }

            var tribes = await _farmTribeService.GetAllFarmTribesAsync();

            if (tribes.Count == 0)
            {
                await RespondAsync(
                    embed: EmbedHelper.Info("Farm Tribes", "No farm tribes have been registered yet."));
                return;
            }

            var description = new StringBuilder();

            int index = 1;
            foreach (var tribe in tribes.OrderBy(t => t.FarmTribeName))
            {
                description.AppendLine($"**[{index}] {tribe.FarmTribeName}**");
                description.AppendLine($"• ID: `{tribe.FarmTribeId}`");
                description.AppendLine($"• Slots: {tribe.UsedSlots} / {tribe.TotalSlots}");
                description.AppendLine();

                index++;
            }

            await RespondAsync(
                embed: EmbedHelper.Info("Farm Tribes", description.ToString()));
        }

        // ======================================================
        // /farmtribe assign
        // ======================================================
        [SlashCommand("assign", "Assign a player to a farm tribe")]
        public async Task AssignPlayer(
            [Summary("player", "Player to assign")] SocketGuildUser player,
            [Summary("tribeid", "Farm tribe ID")] string farmTribeId)
        {
            // Permission check
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            try
            {
                await _assignmentService.AssignPlayerAsync(
                    player.Id.ToString(),
                    farmTribeId);

                // Resolve tribe name for messaging
                var tribe = await _farmTribeService.GetFarmTribeByIdAsync(farmTribeId);
                string tribeName = tribe?.FarmTribeName ?? farmTribeId;

                // Fire-and-forget DM (never fail command if DMs are closed)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var dm = await player.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            embed: EmbedHelper.Info(
                                "Farm Tribe Assignment",
                                $"You have been **assigned** to the farm tribe **{tribeName}**.\n\n" +
                                $"You can now enter and move all your farms to the designated farmtribe.")
                        );
                    }
                    catch
                    {
                        await FollowupAsync(
                            $"Failed to dm {player.DisplayName} ");
                    }
                });

                await LogOfficerAsync(
                    "Farm Tribe Player Assigned",
                    $"• Assigned by: {officer.DisplayName}\n" +
                    $"• Player: {player.DisplayName}\n" +
                    $"• Tribe: {tribeName}");

                await FollowupAsync(
                    embed: EmbedHelper.Success(
                        $"**{player.DisplayName}** assigned to farm tribe **{tribeName}**."),
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
        // /farmtribe unassign
        // ======================================================
        [SlashCommand("unassign", "Remove a player from their farm tribe")]
        public async Task UnassignPlayer(
            [Summary("player", "Player to remove")] SocketGuildUser player)
        {
            // Permission check
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            try
            {
                // Get current assignment BEFORE removal (for DM context)
                var assignment = await _assignmentService.GetAssignmentForUserAsync(
                    player.Id.ToString());

                string? tribeName = null;

                if (assignment != null)
                {
                    var tribe = await _farmTribeService.GetFarmTribeByIdAsync(
                        assignment.FarmTribeId);

                    tribeName = tribe?.FarmTribeName ?? assignment.FarmTribeId;
                }

                await _assignmentService.RemovePlayerAsync(player.Id.ToString());

                // Fire-and-forget DM
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var dm = await player.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            embed: EmbedHelper.Info(
                                "Farm Tribe Assignment Removed",
                                tribeName != null
                                    ? $"You have been **removed** from the farm tribe **{tribeName}**.\n\n" +
                                      $"If this is unexpected, please contact an officer."
                                    : $"You have been **removed** from your farm tribe.")
                        );
                    }
                    catch
                    {
                        // DM disabled — ignore
                    }
                });

                await LogOfficerAsync(
                    "Farm Tribe Player Unassigned",
                    $"• Unassigned by: {officer.DisplayName}\n" +
                    $"• Player: {player.DisplayName}\n" +
                    $"• Tribe: {tribeName ?? "Unknown"}");

                await FollowupAsync(
                    embed: EmbedHelper.Success(
                        $"**{player.DisplayName}** removed from their farm tribe."),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(ex.Message),
                    ephemeral: true);
            }
        }


        // =====================================================================
        // EDIT A EXISTING TRIBES DATA
        // =====================================================================
        [SlashCommand("edit", "Edit a farm tribe (by ID)")]
        public async Task EditFarmTribe(
            [Summary("tribeid", "Farm Tribe ID")] string farmTribeId)
        {
            if (Context.User is not SocketGuildUser user ||
                !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission."),
                    ephemeral: true);
                return;
            }

            var tribe = await _farmTribeService.GetFarmTribeByIdAsync(farmTribeId);
            if (tribe == null)
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("Farm tribe not found."),
                    ephemeral: true);
                return;
            }

            _pendingEdits[user.Id] = tribe.FarmTribeId;

            var modal = new ModalBuilder()
                .WithTitle("Edit Farm Tribe")
                .WithCustomId("edit_farm_tribe")
                .AddTextInput(
                    "Farm Tribe Name",
                    "farmtribe_name",
                    TextInputStyle.Short,
                    value: tribe.FarmTribeName,
                    required: true)
                .AddTextInput(
                    "Total Farm Slots",
                    "farmtribe_slots",
                    TextInputStyle.Short,
                    value: tribe.TotalSlots.ToString(),
                    required: true);

            await RespondWithModalAsync(modal.Build());
        }

        // =====================================================================
        // DELETE A TRIBE
        // =====================================================================
        [SlashCommand("delete", "Delete a farm tribe (by ID)")]
        public async Task DeleteFarmTribe(
            [Summary("tribeid", "Farm Tribe ID")] string farmTribeId)
        {
            if (Context.User is not SocketGuildUser user ||
                !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission."),
                    ephemeral: true);
                return;
            }

            var tribe = await _farmTribeService.GetFarmTribeByIdAsync(farmTribeId);
            if (tribe == null)
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("Farm tribe not found."),
                    ephemeral: true);
                return;
            }

            if (tribe.UsedSlots > 0)
            {
                await RespondAsync(
                    embed: EmbedHelper.Error(
                        $"Cannot delete **{tribe.FarmTribeName}**.\n" +
                        $"It still has **{tribe.UsedSlots}** farms assigned."),
                    ephemeral: true);
                return;
            }

            _pendingDeletes[user.Id] = tribe.FarmTribeId;

            var modal = new ModalBuilder()
                .WithTitle("Confirm Farm Tribe Deletion")
                .WithCustomId("delete_farm_tribe")
                .AddTextInput(
                    "Type DELETE to confirm",
                    "confirm_text",
                    TextInputStyle.Short,
                    placeholder: "DELETE",
                    required: true);

            await RespondWithModalAsync(modal.Build());
        }

        [SlashCommand("overview", "Show all players, their farm count, and farm tribe assignment")]
        public async Task FarmOverview()
        {
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var members = await _memberService.GetAllMembersAsync();
            var farms = await _farmService.GetAllFarmsAsync();
            var assignments = await _assignmentService.GetAllAssignmentsAsync();
            var tribes = await _farmTribeService.GetAllFarmTribesAsync();

            // ======================================================
            // LOOKUPS
            // ======================================================

            // DiscordUserId (string) -> Member
            var memberById = members.ToDictionary(m => m.DiscordUserId);

            // DiscordUserId (string) -> farm count
            var farmCountByUser = new Dictionary<string, int>();
            foreach (var farm in farms)
            {
                if (!farmCountByUser.TryAdd(farm.OwnerDiscordId, 1))
                    farmCountByUser[farm.OwnerDiscordId]++;
            }

            // FarmTribeId -> FarmTribeName
            var tribeNameById = tribes.ToDictionary(
                t => t.FarmTribeId,
                t => t.FarmTribeName);

            // DiscordUserId (string) -> FarmTribeName
            var tribeNameByUser = new Dictionary<string, string>();
            foreach (var assignment in assignments)
            {
                if (tribeNameById.TryGetValue(assignment.FarmTribeId, out var tribeName))
                {
                    tribeNameByUser[assignment.DiscordUserId] = tribeName;
                }
            }

            // ======================================================
            // DIAGNOSTIC: LOG MEMBERS WITH ZERO FARMS
            // ======================================================
            foreach (var member in members)
            {
                if (!farmCountByUser.ContainsKey(member.DiscordUserId))
                {
                    Console.WriteLine($"[FarmOverview] 0 farms: {member.IngameName}");
                }
            }


            // ======================================================
            // BUILD RESULT SET
            // ======================================================
            var results = new List<(string Name, int FarmCount, string Tribe)>(farmCountByUser.Count);

            foreach (var (userId, farmCount) in farmCountByUser)
            {
                if (!memberById.TryGetValue(userId, out var member))
                    continue;

                var tribeName = tribeNameByUser.TryGetValue(userId, out var name)
                    ? name
                    : "Unassigned";

                results.Add((member.IngameName, farmCount, tribeName));
            }

            if (results.Count == 0)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Info("Farm Overview", "No registered players found."),
                    ephemeral: true);
                return;
            }

            results.Sort((a, b) =>
            {
                var cmp = b.FarmCount.CompareTo(a.FarmCount);
                return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            var lines = results.Select((r, i) =>
                $"{i + 1}. **{r.Name}** — `{r.FarmCount}` farms — {r.Tribe}");

            foreach (var chunk in ChunkLines(lines))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Info("📊 Farm Overview (High → Low)", chunk),
                    ephemeral: true);
            }
        }


        // MODALS

        // =====================================================================
        // REGISTER MODAL
        // =====================================================================
        [ModalInteraction("register_farm_tribe", ignoreGroupNames: true)]
        public async Task HandleRegisterFarmTribe(RegisterFarmTribeModal modal)
        {
            await DeferAsync(ephemeral: true); // ← REQUIRED

            if (Context.User is not SocketGuildUser user ||
                !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("You do not have permission."),
                    ephemeral: true);
                return;
            }

            if (!int.TryParse(modal.TotalSlots, out var slots) || slots <= 0)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Total slots must be a positive number."),
                    ephemeral: true);
                return;
            }

            var actor = Context.User as SocketGuildUser;

            string actorInfo =
                $"• Action by: {actor.Mention}\n" +
                $"• Username: {actor.Username}#{actor.Discriminator}\n" +
                $"• User ID: {actor.Id}";


            try
            {
                await _farmTribeService.CreateFarmTribeAsync(
                    modal.FarmTribeName,
                    slots);

                await FollowupAsync(
                    embed: EmbedHelper.Success(
                        $"Farm tribe **{modal.FarmTribeName}** registered with **{slots}** slots."),
                    ephemeral: true);

                await LogOfficerAsync(
                    "Farm Tribe Registered",
                    $"• Created by: {actor.DisplayName}\n" +
                    $"• Name: {modal.FarmTribeName}\n" +
                    $"• Slots: {slots}");
            }
            catch (Exception ex)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(ex.Message),
                    ephemeral: true);
            }
        }


        // =====================================================================
        // EDIT MODAL
        // =====================================================================
        [ModalInteraction("edit_farm_tribe", ignoreGroupNames: true)]
        public async Task HandleEditFarmTribe(RegisterFarmTribeModal modalData)
        {
            Console.WriteLine("EDIT MODAL HANDLER HIT");
            await DeferAsync(ephemeral: true);

            if (!_pendingEdits.TryGetValue(Context.User.Id, out var farmTribeId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Edit session expired. Please run /farmtribe edit again."),
                    ephemeral: true);
                return;
            }

            _pendingEdits.Remove(Context.User.Id);

            if (Context.User is not SocketGuildUser user ||
                !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("You do not have permission."),
                    ephemeral: true);
                return;
            }

            if (!int.TryParse(modalData.TotalSlots, out var newSlots) || newSlots <= 0)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Total slots must be a positive number."),
                    ephemeral: true);
                return;
            }

            var actor = Context.User as SocketGuildUser;

            string actorInfo =
             $"• Action by: {actor.DisplayName}";

            try
            {
                var tribe = await _farmTribeService.GetFarmTribeByIdAsync(farmTribeId);
                if (tribe == null)
                    throw new InvalidOperationException("Farm tribe not found.");

                string oldName = tribe.FarmTribeName;
                int oldSlots = tribe.TotalSlots;

                await _farmTribeService.EditFarmTribeAsync(
                    farmTribeId,
                    modalData.FarmTribeName,
                    newSlots);

                await FollowupAsync(
                    embed: EmbedHelper.Success("Farm tribe updated successfully."),
                    ephemeral: true);

                await LogOfficerAsync(
                    "Farm Tribe Edited",
                    $"{actorInfo}\n\n" +
                    $"• Tribe ID: `{farmTribeId}`\n" +
                    $"• Name: {oldName} → {modalData.FarmTribeName}\n" +
                    $"• Slots: {oldSlots} → {newSlots}");

            }
            catch (Exception ex)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(ex.Message),
                    ephemeral: true);
            }
        }

        // =====================================================================
        // DELETE TRIBE
        // =====================================================================
        [ModalInteraction("delete_farm_tribe", ignoreGroupNames: true)]
        public async Task HandleDeleteFarmTribe(DeleteFarmTribeModal modalData)
        {
            Console.WriteLine("DELETE MODAL HANDLER HIT");
            await DeferAsync(ephemeral: true);

            if (!string.Equals(modalData.Confirmation, "DELETE", StringComparison.OrdinalIgnoreCase))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Deletion cancelled. You must type DELETE exactly."),
                    ephemeral: true);
                return;
            }

            if (!_pendingDeletes.TryGetValue(Context.User.Id, out var farmTribeId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Delete session expired. Please retry."),
                    ephemeral: true);
                return;
            }

            _pendingDeletes.Remove(Context.User.Id);

            var tribe = await _farmTribeService.GetFarmTribeByIdAsync(farmTribeId);
            if (tribe == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Farm tribe no longer exists."),
                    ephemeral: true);
                return;
            }

            var actor = Context.User as SocketGuildUser;

            await _farmTribeService.DeleteFarmTribeAsync(farmTribeId);

            await FollowupAsync(
                embed: EmbedHelper.Success(
                    $"Farm tribe **{tribe.FarmTribeName}** has been deleted."),
                ephemeral: true);

            await LogOfficerAsync(
                "Farm Tribe Deleted",
                $"• Deleted by: {actor.DisplayName}\n" +
                $"• Tribe ID: `{farmTribeId}`\n" +
                $"• Name: {tribe.FarmTribeName}\n" +
                $"• Slots: {tribe.TotalSlots}");
        }

        // ======================================================
        // /Farmtribe research command
        // ======================================================

        [SlashCommand("research", "Notify officers that farm tribe research has been completed")]
        public async Task NotifyResearchComplete()
        {
            await DeferAsync(ephemeral: true);

            string userId = Context.User.Id.ToString();

            var assignment = await _assignmentService.GetAssignmentForUserAsync(userId);
            if (assignment == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(
                        "You are not assigned to a farm tribe.\n" +
                        "Please contact an officer to get assigned before using this command."),
                    ephemeral: true);
                return;
            }

            var tribe = await _farmTribeService.GetFarmTribeByIdAsync(assignment.FarmTribeId);
            if (tribe == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Your farm tribe could not be found. Please contact an officer."),
                    ephemeral: true);
                return;
            }

            await SendOfficerTaskAsync(
                tribe,
                "Farm Tribe Research Completed",
                $"📘 **Research Completed**\n\n" +
                $"• Tribe: **{tribe.FarmTribeName}**\n" +
                $"• Reported by: {Context.User.Mention}\n" +
                $"• Action: Research can now be changed\n" +
                $"• Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
            );


            await FollowupAsync(
                embed: EmbedHelper.Success(
                    $"Thank you. Officers have been notified for **{tribe.FarmTribeName}**."),
                ephemeral: true);
        }

        // ======================================================
        // /Farmtribe goldmine
        // ======================================================

        [SlashCommand("goldmine", "Notify officers that the farm tribe gold mine has expired")]
        public async Task NotifyGoldMineExpired()
        {
            await DeferAsync(ephemeral: true);

            string userId = Context.User.Id.ToString();

            var assignment = await _assignmentService.GetAssignmentForUserAsync(userId);
            if (assignment == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(
                        "You are not assigned to a farm tribe.\n" +
                        "Please contact an officer to get assigned before using this command."),
                    ephemeral: true);
                return;
            }

            var tribe = await _farmTribeService.GetFarmTribeByIdAsync(assignment.FarmTribeId);
            if (tribe == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Your farm tribe could not be found. Please contact an officer."),
                    ephemeral: true);
                return;
            }

            await SendOfficerTaskAsync(
                tribe,
                    $"⛏️ **Gold Mine Expired**",
                    $"• Tribe: **{tribe.FarmTribeName}**\n" +
                    $"• Reported by: {Context.User.Mention}\n" +
                    $"• Action: New gold mine required\n" +
                    $"• Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
            );


            await FollowupAsync(
                embed: EmbedHelper.Success(
                    $"Thank you. Officers have been notified for **{tribe.FarmTribeName}**."),
                ephemeral: true);
        }

        // ======================================================
        // /farmtribe check
        // ======================================================
        [SlashCommand("check", "Show farm counts for players in a specific farm tribe")]
        public async Task FarmTribeCheck(
            [Summary("tribeid", "Farm tribe ID")] string farmTribeId)
        {
            // Officer-only
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            // Validate tribe
            var tribe = await _farmTribeService.GetFarmTribeByIdAsync(farmTribeId);
            if (tribe == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Farm tribe not found."),
                    ephemeral: true);
                return;
            }

            // Get all assignments for this tribe
            var assignments = await _assignmentService.GetAssignmentsForTribeAsync(farmTribeId);

            if (assignments.Count == 0)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Info(
                        $"Farm Tribe Check — {tribe.FarmTribeName}",
                        "No players are currently assigned to this farm tribe."),
                    ephemeral: true);
                return;
            }

            var results = new List<(string Name, int FarmCount)>();

            foreach (var assignment in assignments)
            {
                var member = await _memberService
                    .GetMemberByDiscordIdAsync(assignment.DiscordUserId);

                if (member == null)
                    continue;

                var farms = await _farmService
                    .GetFarmsForUserAsync(assignment.DiscordUserId);

                results.Add((member.IngameName, farms.Count));
            }

            if (results.Count == 0)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Info(
                        $"Farm Tribe Check — {tribe.FarmTribeName}",
                        "No registered members with farms found."),
                    ephemeral: true);
                return;
            }

            // Sort high → low
            var ordered = results
                .OrderByDescending(r => r.FarmCount)
                .ThenBy(r => r.Name)
                .ToList();

            var lines = ordered.Select((r, index) =>
                $"{index + 1}. **{r.Name}** — `{r.FarmCount}` farms");

            // Send in chunks
            foreach (var chunk in ChunkLines(lines))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Info(
                        $"📊 Farm Tribe Check — {tribe.FarmTribeName}",
                        chunk),
                    ephemeral: true);
            }
        }


        // HELPERS
        private async Task LogOfficerAsync(string title, string message)
        {
            var channel = Context.Client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (channel == null) return;

            await channel.SendMessageAsync(
                embed: EmbedHelper.Info(title, message));
        }

        private async Task SendOfficerTaskAsync(
            FarmTribe tribe,
            string title,
            string message)
        {
            var channel = Context.Client.GetChannel(FarmOfficerChannelId) as IMessageChannel;
            if (channel == null)
                return;

            // Resolve officer role for this tribe
            if (!FarmTribeOfficerRoles.TryGetValue(tribe.FarmTribeName, out var roleId))
            {
                await channel.SendMessageAsync(
                    embed: EmbedHelper.Error(
                        $"No officer role configured for farm tribe **{tribe.FarmTribeName}**.\n" +
                        $"Please update the hardcoded mapping.")
                );
                return;
            }

            await channel.SendMessageAsync(
                text: $"<@&{roleId}>",
                embed: EmbedHelper.Info(title, message)
            );
        }
        private static IEnumerable<string> ChunkLines(
             IEnumerable<string> lines,
             int maxLength = 1800)
        {
            var buffer = new StringBuilder();

            foreach (var line in lines)
            {
                if (buffer.Length + line.Length + 1 > maxLength)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }

                buffer.AppendLine(line);
            }

            if (buffer.Length > 0)
                yield return buffer.ToString();
        }

    }
}