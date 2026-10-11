using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TribeBot.Bot.Modals;
using TribeBot.Bot.UI;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class BankPrepayButtonHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IMemberService _memberService;
        private readonly IDonationService _donationService;

        private const ulong OfficerRoleId = 1222665812775534592;
        private const int MaxReasonLength = 500;

        // Stops two officers handling the same request at the same moment
        private static readonly HashSet<ulong> _handledRequests = new();
        private static readonly object _handledLock = new();

        public BankPrepayButtonHandler(IMemberService memberService, IDonationService donationService)
        {
            _memberService = memberService;
            _donationService = donationService;
        }

        // ======================================================
        // APPROVE (button)
        // ======================================================
        [ComponentInteraction(BankPrepayIds.Approve + ":*:*:*", ignoreGroupNames: true)]
        public async Task ApproveAsync(string memberId, string weeksText, string uploadMessageId)
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (!await EnsureOfficerAsync())
                return;

            if (!TryClaim(component.Message.Id))
            {
                await RespondAsync(embed:
                    EmbedHelper.Warning("This request is already being handled."), ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            await FinalizeAsync(
                component.Message, memberId, weeksText, uploadMessageId,
                approved: true, reason: null);
        }

        // ======================================================
        // DENY (button) → opens the reason modal
        // ======================================================
        [ComponentInteraction(BankPrepayIds.Deny + ":*:*:*", ignoreGroupNames: true)]
        public async Task DenyAsync(string memberId, string weeksText, string uploadMessageId)
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (!await EnsureOfficerAsync())
                return;

            // Not claimed yet — the officer can still close the modal without denying
            bool alreadyHandled;
            lock (_handledLock)
            {
                alreadyHandled = _handledRequests.Contains(component.Message.Id);
            }

            if (alreadyHandled)
            {
                await RespondAsync(embed:
                    EmbedHelper.Warning("This request is already being handled."), ephemeral: true);
                return;
            }

            // Carry the same data + the request message ID into the modal
            var modal = new ModalBuilder()
                .WithTitle("Deny Bank Prepay")
                .WithCustomId($"{BankPrepayIds.DenyModal}:{memberId}:{weeksText}:{uploadMessageId}:{component.Message.Id}")
                .AddTextInput("Reason (sent to the member)", "deny_reason", TextInputStyle.Paragraph,
                    placeholder: "e.g. Gold screenshot is missing, please upload all 4 resources",
                    minLength: 3, maxLength: MaxReasonLength, required: true);

            await RespondWithModalAsync(modal.Build());
        }

        // ======================================================
        // DENY (modal submit)
        // ======================================================
        [ModalInteraction(BankPrepayIds.DenyModal + ":*:*:*:*", ignoreGroupNames: true)]
        public async Task DenyModalAsync(
            string memberId, string weeksText, string uploadMessageId, string requestMessageId,
            BankPrepayDenyModal modal)
        {
            if (!await EnsureOfficerAsync())
                return;

            if (!ulong.TryParse(requestMessageId, out ulong requestMsgId))
            {
                await RespondAsync(embed:
                    EmbedHelper.Error("Could not find the original request."), ephemeral: true);
                return;
            }

            if (!TryClaim(requestMsgId))
            {
                await RespondAsync(embed:
                    EmbedHelper.Warning("This request was already handled by another officer."), ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            // The modal is submitted in the same channel as the request (officer log)
            if (await Context.Channel.GetMessageAsync(requestMsgId) is not IUserMessage requestMessage)
            {
                Release(requestMsgId);
                await FollowupAsync(embed:
                    EmbedHelper.Error("Could not find the original request message."), ephemeral: true);
                return;
            }

            string reason = (modal.Reason ?? "").Trim();
            if (reason.Length > MaxReasonLength)
                reason = reason[..MaxReasonLength];

            await FinalizeAsync(
                requestMessage, memberId, weeksText, uploadMessageId,
                approved: false, reason: reason);
        }

        // ======================================================
        // CORE: record (on approve), update request, react, DM
        // ======================================================
        private async Task FinalizeAsync(
            IUserMessage requestMessage,
            string memberId, string weeksText, string uploadMessageId,
            bool approved, string? reason)
        {
            var officer = (SocketGuildUser)Context.User;

            try
            {
                if (!int.TryParse(weeksText, out int weeks) ||
                    !ulong.TryParse(uploadMessageId, out ulong uploadMsgId) ||
                    !ulong.TryParse(memberId, out ulong memberUid))
                {
                    throw new InvalidOperationException(
                        $"Bad prepay data: {memberId}:{weeksText}:{uploadMessageId}");
                }

                var member = await _memberService.GetMemberByDiscordIdAsync(memberId);

                // Keep the original request fields and add the outcome
                var fields = requestMessage.Embeds.FirstOrDefault()?.Fields
                    .ToDictionary(f => f.Name, f => f.Value)
                    ?? new Dictionary<string, string>();

                string range = "—";

                if (member == null)
                {
                    fields["Status"] = $"⚠️ Member no longer registered — nothing recorded ({officer.Username})";
                }
                else if (approved)
                {
                    var covered = await _donationService.AddPrepaidWeeksAsync(
                        member, weeks, BankPrepayIds.PrepaidWeekValue, DateTime.UtcNow);

                    range = covered.Count == 1
                        ? $"week of {covered[0]:yyyy-MM-dd}"
                        : $"week of {covered.First():yyyy-MM-dd} → week of {covered.Last():yyyy-MM-dd}";

                    fields["Weeks Covered"] = range;
                    fields["Status"] = $"✅ Approved by {officer.Username}";
                }
                else
                {
                    fields["Status"] = $"❌ Denied by {officer.Username}";
                    fields["Reason"] = reason ?? "—";
                }

                // Update the request and remove the buttons
                await requestMessage.ModifyAsync(m =>
                {
                    m.Embed = EmbedHelper.Log("Bank Prepay Request", fields);
                    m.Components = new ComponentBuilder().Build();
                });

                if (member == null)
                {
                    await FollowupAsync(embed:
                        EmbedHelper.Warning("Member is no longer registered — nothing was recorded."), ephemeral: true);
                    return;
                }

                await ReactOnUploadAsync(uploadMsgId, approved ? "✅" : "❌");

                bool dmSent = await DmMemberAsync(memberUid, approved
                    ? $"🏦 **Bank Prepay Approved**\n\n" +
                      $"Your prepayment of **{weeks}** week(s) was approved.\n" +
                      $"Covered: **{range}**\n\n" +
                      $"You don't need to do the weekly bank for these weeks and you won't be fined."
                    : $"🏦 **Bank Prepay Denied**\n\n" +
                      $"Your prepayment request of **{weeks}** week(s) was denied.\n\n" +
                      $"**Reason:** {reason}\n\n" +
                      $"Fix this and submit a new request in <#{BankPrepayIds.ChannelId}>, " +
                      $"or contact an officer if you have questions.");

                string result = approved ? "Prepay approved." : "Prepay denied.";
                await FollowupAsync(embed: dmSent
                    ? EmbedHelper.Success($"{result} The member was notified by DM.")
                    : EmbedHelper.Warning($"{result} ⚠️ Could not DM the member (DMs closed?) — please let them know."),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                // Allow a retry if something failed
                Release(requestMessage.Id);

                Console.WriteLine($"[BankPrepay Error] {ex}");
                await FollowupAsync(embed:
                    EmbedHelper.Error("Something went wrong handling this request. Try again."), ephemeral: true);
            }
        }

        // ======================================================
        // HELPERS
        // ======================================================
        private async Task<bool> EnsureOfficerAsync()
        {
            if (Context.User is SocketGuildUser user &&
                user.Roles.Any(r => r.Id == OfficerRoleId))
                return true;

            await RespondAsync(embed:
                EmbedHelper.Error("Only officers can approve or deny prepayments."), ephemeral: true);
            return false;
        }

        private static bool TryClaim(ulong requestMessageId)
        {
            lock (_handledLock)
            {
                return _handledRequests.Add(requestMessageId);
            }
        }

        private static void Release(ulong requestMessageId)
        {
            lock (_handledLock)
            {
                _handledRequests.Remove(requestMessageId);
            }
        }

        private async Task ReactOnUploadAsync(ulong uploadMsgId, string emoji)
        {
            try
            {
                if (Context.Client.GetChannel(BankPrepayIds.ChannelId) is not IMessageChannel channel)
                    return;

                if (await channel.GetMessageAsync(uploadMsgId) is not IUserMessage upload)
                    return;

                await upload.RemoveReactionAsync(new Emoji("⏳"), Context.Client.CurrentUser);
                await upload.AddReactionAsync(new Emoji(emoji));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BankPrepay React Error] {ex.Message}");
            }
        }

        private async Task<bool> DmMemberAsync(ulong memberUid, string text)
        {
            try
            {
                // REST lookup — works even when the member isn't in the cache
                var user = await Context.Client.Rest.GetUserAsync(memberUid);
                if (user == null)
                    return false;

                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(text);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BankPrepay DM Error] {ex.Message}");
                return false;
            }
        }
    }
}