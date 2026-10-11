using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TribeBot.Bot.UI;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class BankPrepayButtonHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IMemberService _memberService;
        private readonly IDonationService _donationService;

        private const ulong OfficerRoleId = 1222665812775534592;

        // Stops two officers handling the same request at the same moment
        private static readonly HashSet<ulong> _handledRequests = new();
        private static readonly object _handledLock = new();

        public BankPrepayButtonHandler(IMemberService memberService, IDonationService donationService)
        {
            _memberService = memberService;
            _donationService = donationService;
        }

        // ======================================================
        // APPROVE
        // ======================================================
        [ComponentInteraction(BankPrepayIds.Approve + ":*:*:*", ignoreGroupNames: true)]
        public async Task ApproveAsync(string memberId, string weeksText, string uploadMessageId)
            => await HandleDecisionAsync(memberId, weeksText, uploadMessageId, approved: true);

        // ======================================================
        // DENY
        // ======================================================
        [ComponentInteraction(BankPrepayIds.Deny + ":*:*:*", ignoreGroupNames: true)]
        public async Task DenyAsync(string memberId, string weeksText, string uploadMessageId)
            => await HandleDecisionAsync(memberId, weeksText, uploadMessageId, approved: false);

        // ======================================================
        // CORE HANDLER
        // ======================================================
        private async Task HandleDecisionAsync(
            string memberId, string weeksText, string uploadMessageId, bool approved)
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(embed:
                    EmbedHelper.Error("Only officers can approve or deny prepayments."), ephemeral: true);
                return;
            }

            bool alreadyHandled;
            lock (_handledLock)
            {
                alreadyHandled = !_handledRequests.Add(component.Message.Id);
            }

            if (alreadyHandled)
            {
                await RespondAsync(embed:
                    EmbedHelper.Warning("This request is already being handled."), ephemeral: true);
                return;
            }

            await DeferAsync();

            try
            {
                if (!int.TryParse(weeksText, out int weeks) ||
                    !ulong.TryParse(uploadMessageId, out ulong uploadMsgId) ||
                    !ulong.TryParse(memberId, out ulong memberUid))
                {
                    throw new InvalidOperationException($"Bad prepay button data: {component.Data.CustomId}");
                }

                var member = await _memberService.GetMemberByDiscordIdAsync(memberId);

                // Keep the original request fields and add the outcome
                var fields = component.Message.Embeds.FirstOrDefault()?.Fields
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
                }

                // Update the request and remove the buttons
                await component.ModifyOriginalResponseAsync(m =>
                {
                    m.Embed = EmbedHelper.Log("Bank Prepay Request", fields);
                    m.Components = new ComponentBuilder().Build();
                });

                if (member == null)
                    return;

                await ReactOnUploadAsync(uploadMsgId, approved ? "✅" : "❌");
                await DmMemberAsync(memberUid, approved
                    ? $"🏦 **Bank Prepay Approved**\n\n" +
                      $"Your prepayment of **{weeks}** week(s) was approved.\n" +
                      $"Covered: **{range}**\n\n" +
                      $"You don't need to do the weekly bank for these weeks and you won't be fined."
                    : $"🏦 **Bank Prepay Denied**\n\n" +
                      $"Your prepayment request of **{weeks}** week(s) was denied.\n" +
                      $"Please contact an officer.");
            }
            catch (Exception ex)
            {
                // Allow a retry if something failed
                lock (_handledLock)
                {
                    _handledRequests.Remove(component.Message.Id);
                }

                Console.WriteLine($"[BankPrepay Error] {ex}");
                await FollowupAsync(embed:
                    EmbedHelper.Error("Something went wrong handling this request. Try again."), ephemeral: true);
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

        private async Task DmMemberAsync(ulong memberUid, string text)
        {
            try
            {
                // REST lookup — works even when the member isn't in the cache
                var user = await Context.Client.Rest.GetUserAsync(memberUid);
                if (user == null)
                    return;

                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BankPrepay DM Error] {ex.Message}");
            }
        }
    }
}