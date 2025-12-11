using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot.Services
{
    public class SchedulerService
    {
        private readonly DiscordSocketClient _client;
        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly ulong OfficerLogChannelId = 1440211043820507217;

        public SchedulerService(DiscordSocketClient client, IGoogleSheetsDataStore dataStore)
        {
            _client = client;
            _dataStore = dataStore;
        }

        public void Start()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Tick();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Scheduler Error: {ex}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            });
        }

        private async Task Tick()
        {
            var events = await _dataStore.GetAllScheduledEventsAsync();
            var nowUtc = DateTime.UtcNow;

            foreach (var evt in events)
            {
                if (evt.Completed)
                    continue;

                DateTime reminderTime = evt.EventDateUtc.AddHours(-evt.ReminderOffsetHours);

                if (!evt.ReminderSent && nowUtc >= reminderTime && nowUtc < evt.EventDateUtc)
                {
                    await SendReminder(evt);
                }

                if (nowUtc >= evt.EventDateUtc.AddMinutes(5))
                {
                    evt.Completed = true;
                    await _dataStore.UpdateScheduledEventAsync(evt);
                }
            }
        }

        private async Task SendReminder(ScheduledEvent evt)
        {
            var guild = _client.Guilds.FirstOrDefault();
            if (guild == null) return;

            ulong targetRoleId = 1439972286877794314; // Dev test role or HogsEvents role depending on your testing

            var members = await guild.GetUsersAsync().FlattenAsync();
            var targets = members.Where(u => !u.IsBot && u.RoleIds.Contains(targetRoleId)).ToList();

            int sent = 0;
            var failedUsers = new List<string>();
            var embed = BuildEventReminderEmbed(evt);

            foreach (var user in targets)
            {
                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    await dm.SendMessageAsync(embed: embed);

                    sent++;
                    await Task.Delay(1100);
                }
                catch
                {
                    failedUsers.Add($"{user.Username} ({user.Id})");
                }
            }

            evt.ReminderSent = true;
            await _dataStore.UpdateScheduledEventAsync(evt);

            // Log to officer channel
            var log = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (log != null)
            {
                var logEmbed = new EmbedBuilder()
                    .WithTitle("⏰ Scheduled Reminder Sent")
                    .WithColor(Color.DarkBlue)
                    .AddField("Event", evt.EventName)
                    .AddField("EventId", evt.EventId)
                    .AddField("DMs Sent", sent.ToString(), true)
                    .AddField("DMs Failed", failedUsers.Count.ToString(), true);

                if (failedUsers.Count > 0)
                    logEmbed.AddField("Failed Users", string.Join("\n", failedUsers));

                await log.SendMessageAsync(embed: logEmbed.Build());
            }
        }


        //HELPERS

        private Embed BuildEventReminderEmbed(ScheduledEvent evt)
        {
            long unix = ((DateTimeOffset)evt.EventDateUtc).ToUnixTimeSeconds();

            return new EmbedBuilder()
                .WithTitle("⏰ Event Reminder")
                .WithColor(new Color(0x00A3E0))
                .AddField("Event", $"**{evt.EventName}**", false)
                .AddField("Starts At (UTC)", $"**{evt.EventDateUtc:yyyy-MM-dd HH:mm} UTC**", false)
                .AddField("Starts In", $"<t:{unix}:R>", false)
                .AddField("Message", evt.Message, false)
                .WithFooter($"Event ID: {evt.EventId}")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }

    }
}
