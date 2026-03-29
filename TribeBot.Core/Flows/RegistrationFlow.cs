using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Flows.Interfaces;
using TribeBot.Core.Interfaces;

namespace TribeBot.Core.Flows
{
    public class RegistrationFlow : IFlow
    {
        public ulong UserId { get; }

        private readonly IMemberService _memberService;
        private readonly IUserFlowManager _flowManager;
        private readonly DiscordSocketClient _client;

        // Announcement channel
        private readonly ulong _announcementChannelId = 1446455618062909651;

        private enum Step
        {
            AskName,
            AskId,
            AskMight,
            AskKills,
            AskCollector,
            Confirm
        }

        private Step _step = Step.AskName;

        private string _name = "";
        private string _id = "";
        private int _might = 0;
        private long _kills = 0;
        private int _collector = 0;

        public RegistrationFlow(
            ulong userId,
            IMemberService memberService,
            IUserFlowManager flowManager,
            DiscordSocketClient client)
        {
            UserId = userId;
            _memberService = memberService;
            _flowManager = flowManager;
            _client = client;
        }

        // =====================================================
        //  Handle User Message
        // =====================================================
        public async Task<bool> HandleAsync(SocketMessage message)
        {
            if (message.Author.Id != UserId)
                return false;

            string input = message.Content.Trim();

            // Special Commands
            if (input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync("❌ Registration cancelled.");
                _flowManager.EndFlow(UserId);
                return true;
            }

            if (input.Equals("back", StringComparison.OrdinalIgnoreCase))
            {
                await HandleBackAsync(message);
                return true;
            }

            // Route to current step
            return _step switch
            {
                Step.AskName => await HandleNameAsync(message, input),
                Step.AskId => await HandleIdAsync(message, input),
                Step.AskMight => await HandleMightAsync(message, input),
                Step.AskKills => await HandleKillsAsync(message, input),
                Step.AskCollector => await HandleCollectorAsync(message, input),
                Step.Confirm => await HandleConfirmationAsync(message, input),
                _ => false
            };
        }

        // =====================================================
        //      STEP HANDLERS
        // =====================================================

        private async Task<bool> HandleNameAsync(SocketMessage message, string input)
        {
            if (string.IsNullOrWhiteSpace(input) ||
                input.Length > 20 ||
                !System.Text.RegularExpressions.Regex.IsMatch(input, @"^[A-Za-z0-9 ]+$"))
            {
                await message.Channel.SendMessageAsync("❌ Invalid name. Only letters, numbers and spaces allowed (max 20).");
                return true;
            }

            _name = input;
            _step = Step.AskId;

            await message.Channel.SendMessageAsync("Enter your **In-Game ID** (1–10 digits):");
            return true;
        }

        private async Task<bool> HandleIdAsync(SocketMessage message, string input)
        {
            if (!long.TryParse(input, out long idNum) || idNum < 1 || idNum > 9_999_999_999)
            {
                await message.Channel.SendMessageAsync("❌ Invalid ID. Must be 1–10 digits.");
                return true;
            }

            // Duplicate check
            var allMembers = await _memberService.GetAllMembersAsync();
            if (allMembers.Exists(m => m.IngameId == input))
            {
                await message.Channel.SendMessageAsync("❌ That ID is already registered to another member.");
                return true;
            }

            _id = input;
            _step = Step.AskMight;

            await message.Channel.SendMessageAsync("Enter your **Might** (0–3000000000):");
            return true;
        }

        private async Task<bool> HandleMightAsync(SocketMessage message, string input)
        {
            if (!long.TryParse(input, out long might) || might < 0 || might > 3_000_000_000)
            {
                await message.Channel.SendMessageAsync("❌ Invalid Might. Must be 0–3000000000.");
                return true;
            }

            _might = (int)might;
            _step = Step.AskKills;

            await message.Channel.SendMessageAsync("Enter your **Kill Points** (0–500000000000):");
            return true;
        }

        private async Task<bool> HandleKillsAsync(SocketMessage message, string input)
        {
            if (!long.TryParse(input, out long kills) || kills < 0 || kills > 500_000_000_000)
            {
                await message.Channel.SendMessageAsync("❌ Invalid Kill Points.");
                return true;
            }

            _kills = kills;
            _step = Step.AskCollector;

            await message.Channel.SendMessageAsync("Enter your **Collector Level** (0–100):");
            return true;
        }

        private async Task<bool> HandleCollectorAsync(SocketMessage message, string input)
        {
            if (!int.TryParse(input, out int col) || col < 0 || col > 100)
            {
                await message.Channel.SendMessageAsync("❌ Invalid Collector Level.");
                return true;
            }

            _collector = col;
            _step = Step.Confirm;

            await SendSummaryAsync(message);
            return true;
        }

        private async Task<bool> HandleConfirmationAsync(SocketMessage message, string input)
        {
            if (input.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                var member = new Member
                {
                    DiscordUserId = UserId.ToString(),
                    IngameName = _name,
                    IngameId = _id,
                    Might = _might,
                    KillPoints = _kills,
                    CollectorLevel = _collector,
                    ReignPoints = 0,
                    BankExempt = false,
                    DeliveryExempt = false,
                    LastUpdatedUTC = DateTime.UtcNow
                };

                await _memberService.RegisterOrUpdateAsync(member);

                await message.Channel.SendMessageAsync("🎉 **Registration complete!**");

                // =============================
                // ANNOUNCEMENT CHANNEL MESSAGE
                // =============================
                var announcementChannel = _client.GetChannel(_announcementChannelId) as IMessageChannel;
                if (announcementChannel != null)
                {
                    await announcementChannel.SendMessageAsync(
                        $"📢 **New Member Registered!**\n" +
                        $"**Name:** {_name}\n" +
                        $"**ID:** {_id}\n" +
                        $"**Might:** {_might:N0}\n" +
                        $"**Kill Points:** {_kills:N0}\n" +
                        $"**Collector Level:** {_collector}\n" +
                        $"Welcome to the tribe! 🎉"
                    );
                }

                _flowManager.EndFlow(UserId);
                return true;
            }

            if (input.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync("Registration cancelled.");
                _flowManager.EndFlow(UserId);
                return true;
            }

            await message.Channel.SendMessageAsync("Please type **YES** to confirm or **NO** to cancel.");
            return true;
        }

        // =====================================================
        //      HELPERS
        // =====================================================

        private async Task HandleBackAsync(SocketMessage message)
        {
            if (_step == Step.AskName)
            {
                await message.Channel.SendMessageAsync("❌ Registration cancelled.");
                _flowManager.EndFlow(UserId);
                return;
            }

            _step = _step - 1;

            string prompt = _step switch
            {
                Step.AskName => "Enter your **In-Game Name**:",
                Step.AskId => "Enter your **In-Game ID**:",
                Step.AskMight => "Enter your **Might**:",
                Step.AskKills => "Enter your **Kill Points**:",
                Step.AskCollector => "Enter your **Collector Level**:",
                Step.Confirm => "Confirm the information.",
                _ => "Continue:"
            };

            await message.Channel.SendMessageAsync($"🔙 Going back.\n{prompt}");
        }

        private async Task SendSummaryAsync(SocketMessage message)
        {
            var summary =
                $"Please confirm your information:\n\n" +
                $"**Name:** {_name}\n" +
                $"**ID:** {_id}\n" +
                $"**Might:** {_might:N0}\n" +
                $"**Kill Points:** {_kills:N0}\n" +
                $"**Collector Level:** {_collector}\n\n" +
                $"Type **YES** to confirm or **NO** to cancel.";

            await message.Channel.SendMessageAsync(summary);
        }
    }
}
