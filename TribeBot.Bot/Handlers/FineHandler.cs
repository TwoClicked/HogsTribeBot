using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Services.Services;
using TribeBot.Bot.UI; // IMPORTANT: For EmbedHelper

namespace TribeBot.Bot.Handlers
{
    public class FineHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IFineService _fineService;
        private readonly IMemberService _memberService;
        private readonly PaddleOcrServerService _ocrService;

        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong FinePaymentChannelId = 1440431172160061450;
        private const ulong OfficerLogChannelId = 1440211043820507217;

        public FineHandler(
            DiscordSocketClient client,
            IFineService fineService,
            IMemberService memberService,
            PaddleOcrServerService ocrService)
        {
            _client = client;
            _fineService = fineService;
            _memberService = memberService;
            _ocrService = ocrService;
        }

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        private Task SendLog(string title, Dictionary<string, string> fields)
            => OfficerLog?.SendMessageAsync(embed: EmbedHelper.Log(title, fields)) ?? Task.CompletedTask;

        // ======================================================================
        // ROOT ENTRY
        // ======================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.Trim().ToLower();

            if (content.StartsWith("!fineuser"))
            {
                await IssueEventFine(message);
                return true;
            }

            if (content.StartsWith("!finereign"))
            {
                await IssueReignFine(message);
                return true;
            }

            if (content.Equals("!finelist"))
            {
                await ShowFineList(message);
                return true;
            }

            if (content.StartsWith("!removefine"))
            {
                await RemoveFine(message);
                return true;
            }

            if (content.Equals("!myfines"))
            {
                await ShowMyFines(message);
                return true;
            }

            if (content.Equals("!finereminder"))
            {
                await SendFineReminder(message);
                return true;
            }

            if (message.Channel.Id == FinePaymentChannelId && message.Attachments.Any())
            {
                await ProcessFinePayment(message);
                return true;
            }

            if (content.StartsWith("!verifiedpayment"))
            {
                await VerifyPayment(message);
                return true;
            }
            if (content.Equals("!unpaidfines"))
            {
                await ShowUnpaidFinesByType(message);
                return true;
            }


            return false;
        }

        private async Task ShowUnpaidFinesByType(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var unpaidFines = (await _fineService.GetUnpaidFinesAsync())
                .OrderBy(f => f.FineType)
                .ThenByDescending(f => f.Amount)
                .ToList();

            if (!unpaidFines.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("🎉 There are no unpaid fines."));
                return;
            }

            var grouped = unpaidFines
                .GroupBy(f => f.FineType)
                .OrderBy(g => g.Key);

            var description = "";

            foreach (var group in grouped)
            {
                description += $"**{group.Key.ToUpper()} FINES**\n";

                foreach (var fine in group)
                {
                    int remaining = fine.Amount - fine.PaidAmount;

                    description +=
                        $"• **{fine.IngameName}** — " +
                        $"{remaining:N0} remaining " +
                        $"(FineID `{fine.FineId}`)\n";
                }

                description += "\n";
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Unpaid Fines (By Type)", description));
        }


        private async Task RemoveFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var parts = message.Content.Split(' ', 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: !removefine FineId");
                return;
            }

            string fineId = parts[1].Trim();

            await _fineService.RemoveFineAsync(fineId);

            await message.Channel.SendMessageAsync($"🗑️ Removed fine {fineId}.");
        }

        // ======================================================================
        // !VERIFIEDPAYMENT
        // ======================================================================
        private async Task VerifyPayment(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!verifiedpayment @user`"));
                return;
            }

            var user = message.MentionedUsers.First();
            var member = await _memberService.GetMemberByDiscordIdAsync(user.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"{user.Username} is not registered."));
                return;
            }

            var fines = await _fineService.GetFinesForUserAsync(member.DiscordUserId);
            var unpaid = fines.Where(f => !f.IsPaid).ToList();

            if (!unpaid.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Info("Fine Status", $"{user.Mention} has no unpaid fines."));
                return;
            }

            foreach (var fine in unpaid)
            {
                fine.PaidAmount = fine.Amount;
                fine.IsPaid = true;
                await _fineService.UpdateFineAsync(fine);
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success($"Verified manual payment for {user.Mention}. All fines marked as **PAID**."));

            try
            {
                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    $"✅ **Your payment was manually verified by an officer.**\n" +
                    $"All your fines are now marked **PAID**.\n" +
                    $"Reign strikes still resolve only at the next scheduled reset.");
            }
            catch { }

            await SendLog("Manual Fine Payment Verified", new()
            {
                { "User", user.Username },
                { "Officer", message.Author.Username }
            });
        }

        // ======================================================================
        // !FINEUSER
        // ======================================================================
        private async Task IssueEventFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!fineuser @user amount reason`"));
                return;
            }

            var target = message.MentionedUsers.First();
            var parts = message.Content.Split(' ', 4);

            if (parts.Length < 4)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!fineuser @user amount reason`"));
                return;
            }

            if (!int.TryParse(parts[2], out int amount) || amount <= 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Invalid fine amount."));
                return;
            }

            string reason = parts[3];
            var member = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"{target.Username} is not registered."));
                return;
            }

            await _fineService.AddEventFineAsync(member, amount, reason);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"💸 **Event Fine Issued**\n\n" +
                    $"• User: {target.Mention}\n" +
                    $"• Amount: **{amount:N0}**\n" +
                    $"• Reason: *{reason}*\n" +
                    $"• Type: **Event Fine**"
                ));

            try
            {
                var dm = await target.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    $"⚠️ **You received an Event Fine!**\n" +
                    $"Amount: **{amount:N0}**\nReason: {reason}\n" +
                    $"Please pay in <#{FinePaymentChannelId}>.");
            }
            catch { }

            await SendLog("Event Fine Issued", new()
            {
                { "User", target.Username },
                { "Amount", amount.ToString("N0") },
                { "Officer", message.Author.Username },
                { "Reason", reason }
            });
        }

        // ======================================================================
        // !FINEREIGN
        // ======================================================================
        private async Task IssueReignFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!finereign @user amount reason`"));
                return;
            }

            var target = message.MentionedUsers.First();
            var parts = message.Content.Split(' ', 4);

            if (parts.Length < 4)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!finereign @user amount reason`"));
                return;
            }

            if (!int.TryParse(parts[2], out int amount) || amount <= 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Invalid fine amount."));
                return;
            }

            string reason = parts[3];
            var member = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"{target.Username} is not registered."));
                return;
            }

            var allFines = await _fineService.GetAllFinesAsync();
            bool repeatOffense = allFines.Any(f =>
                f.DiscordUserId == member.DiscordUserId &&
                f.FineType == "Reign");

            await _fineService.AddReignFineAsync(member, amount, reason);

            string extra = repeatOffense
                ? "Strikes added: **2** (repeat offense)"
                : "No strikes added (first offense)";

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"⚔️ **Reign Fine Issued**\n\n" +
                    $"• User: {target.Mention}\n" +
                    $"• Amount: **{amount:N0}**\n" +
                    $"• Reason: *{reason}*\n" +
                    $"{extra}"
                ));

            try
            {
                var dm = await target.CreateDMChannelAsync();

                string dmMsg = repeatOffense
                    ? $"⚠️ **Repeat Reign Fine!**\nAmount: **{amount:N0}**\nReason: {reason}\nStrikes Added: **2**\n🚫 Blacklisted from the next TWO reign events.\nPay in <#{FinePaymentChannelId}>."
                    : $"⚠️ **Reign Fine**\nAmount: **{amount:N0}**\nReason: {reason}\nNo strikes added.\nPay in <#{FinePaymentChannelId}>.";

                await dm.SendMessageAsync(dmMsg);
            }
            catch { }

            await SendLog("Reign Fine Issued", new()
            {
                { "User", target.Username },
                { "Amount", amount.ToString("N0") },
                { "Officer", message.Author.Username },
                { "Reason", reason },
                { "Repeat Offender", repeatOffense ? "Yes" : "No" }
            });
        }

        // ======================================================================
        // OCR FINE PAYMENT HANDLING
        // ======================================================================
        private async Task ProcessFinePayment(SocketMessage message)
        {
            string discordId = message.Author.Id.ToString();
            var member = await _memberService.GetMemberByDiscordIdAsync(discordId);

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You are not registered."));
                return;
            }

            int total = 0;

            foreach (var att in message.Attachments)
            {
                if (att.ContentType == null || !att.ContentType.StartsWith("image"))
                    continue;

                string tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(att.Url);
                    await File.WriteAllBytesAsync(tmp, data);
                }
                int? amt = await _ocrService.ExtractDonationAmountAsync(tmp);
                File.Delete(tmp);

                if (amt.HasValue) total += amt.Value;
            }

            if (total <= 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("I could not read any fine payment amount."));
                return;
            }

            await _fineService.AddPaymentAsync(discordId, total);
            await message.AddReactionAsync(new Emoji("💸"));

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"Payment received! An officer will verify shortly.\n\n⚠️ Reign fines remain visible until strike resets."
                ));
        }

        // ======================================================================
        // !MYFINES
        // ======================================================================
        private async Task ShowMyFines(SocketMessage message)
        {
            string id = message.Author.Id.ToString();
            var fines = await _fineService.GetFinesForUserAsync(id);

            if (!fines.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("🎉 You have no fines!"));
                return;
            }

            string unpaid = "";
            string paid = "";
            int totalOwed = 0;

            foreach (var f in fines)
            {
                if (!f.IsPaid)
                {
                    unpaid +=
                        $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` " +
                        $"({f.PaidAmount:N0}/{f.Amount:N0} paid)\n";

                    totalOwed += (f.Amount - f.PaidAmount);
                }
                else
                {
                    paid +=
                        $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` — PAID\n";
                }
            }

            string msg =
                $"🧾 **Your Fines**\n\n" +
                "🟥 **Unpaid:**\n" +
                (string.IsNullOrWhiteSpace(unpaid) ? "• None\n" : unpaid) +
                $"\n**Total Owed: {totalOwed:N0}**\n\n" +
                "🟩 **Paid:**\n" +
                (string.IsNullOrWhiteSpace(paid) ? "• None\n" : paid);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Your Fine Overview", msg));
        }

        // ======================================================================
        // !FINELIST
        // ======================================================================
        private async Task ShowFineList(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var unpaid = await _fineService.GetUnpaidFinesAsync();
            var paid = await _fineService.GetPaidFinesAsync();

            string msg = "💀 **Fine List**\n\n";

            msg += "🟥 **UNPAID FINES:**\n";
            msg += unpaid.Any()
                ? string.Join("\n", unpaid.Select(f =>
                    $"• **{f.IngameName}** — {f.Amount:N0} — `{f.FineType}` — ID `{f.FineId}` ({f.PaidAmount}/{f.Amount})"))
                : "• None";

            msg += "\n\n🟩 **PAID (Awaiting Removal):**\n";
            msg += paid.Any()
                ? string.Join("\n", paid.Select(f =>
                    $"• **{f.IngameName}** — {f.Amount:N0} — `{f.FineType}` — ID `{f.FineId}` — PAID"))
                : "• None";

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Fine List", msg));
        }

        // ======================================================================
        // !FINEREMINDER — Officer-only mass DM
        // ======================================================================
        private async Task SendFineReminder(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Fine Reminder", "📨 Sending fine reminders in the background…"));

            await SendLog("Fine Reminder Started", new()
                                                                        {
                                                                            { "Triggered By", message.Author.Username }
                                                                        });

            _ = Task.Run(async () =>
            {
                if (message.Channel is SocketGuildChannel gc)
                    await SendFineReminderBackground(gc, message.Author.Username);
            });
        }


        private async Task SendFineReminderBackground(
            SocketGuildChannel guildChannel,
            string triggeredBy)
        {
            var guild = guildChannel.Guild;
            var allFines = await _fineService.GetAllFinesAsync();

            var unpaidUsers = allFines
                .Where(f => !f.IsPaid)
                .GroupBy(f => f.DiscordUserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    TotalOwed = g.Sum(x => x.Amount - x.PaidAmount),
                    Count = g.Count()
                })
                .ToList();

            int sent = 0;
            int failed = 0;
            List<string> failures = new();

            foreach (var entry in unpaidUsers)
            {
                if (!ulong.TryParse(entry.UserId, out ulong uid))
                {
                    failed++;
                    failures.Add($"Invalid DiscordId: {entry.UserId}");
                    continue;
                }

                var user = guild.GetUser(uid);
                if (user == null)
                {
                    failed++;
                    failures.Add($"User not found: {uid}");
                    continue;
                }

                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    await dm.SendMessageAsync(
                        $"💀 **Fine Reminder**\n\n" +
                        $"You currently have **{entry.Count} unpaid fine(s)** " +
                        $"totaling **{entry.TotalOwed:N0}**.\n\n" +
                        $"Please pay in <#{FinePaymentChannelId}>.\n" +
                        $"If you believe this is incorrect, contact an officer."
                    );

                    sent++;
                    await Task.Delay(1200);
                }
                catch
                {
                    failed++;
                    failures.Add($"{user.Username} ({uid}) — DM failed");
                    await Task.Delay(1500);
                }
            }

            // Officer log summary
            var fields = new Dictionary<string, string>
                            {
                                { "Triggered By", triggeredBy },
                                { "Users With Unpaid Fines", unpaidUsers.Count.ToString() },
                                { "DMs Sent", sent.ToString() },
                                { "Failures", failed.ToString() }
                            };

            if (failures.Any())
                fields["Failure Details"] = string.Join("\n", failures.Take(10));

            await SendLog("Fine Reminder Completed", fields);

            // Feedback to invoking channel
            if (guildChannel is IMessageChannel channel)
            {
                await channel.SendMessageAsync(embed:
                    EmbedHelper.Info("Fine Reminder Summary",
                        $"📨 **Fine reminders completed**\n\n" +
                        $"• Users with unpaid fines: **{unpaidUsers.Count}**\n" +
                        $"• DMs sent: **{sent}**\n" +
                        $"• Failed deliveries: **{failed}**"
                    ));
            }
        }



        // ======================================================================
        // OFFICER CHECK
        // ======================================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                _ = message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("This command must be used inside the server."));
                return false;
            }

            var user = gc.GetUser(message.Author.Id);

            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                _ = message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"{message.Author.Mention}, you do not have permission."));
                return false;
            }

            return true;
        }
    }
}
