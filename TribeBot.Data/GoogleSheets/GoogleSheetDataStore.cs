using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;

namespace TribeBot.Data.GoogleSheets
{
    public class GoogleSheetsDataStore : IGoogleSheetsDataStore
    {
        private readonly SheetsService _sheetsService;
        private readonly string _spreadsheetId;

        private const string MembersSheet = "Members";
        private const string ReignSheet = "ReignRegistrations";
        private const string DonationsSheet = "Donations";

        public GoogleSheetsDataStore(string credentialsPath, string spreadsheetId)
        {
            _spreadsheetId = spreadsheetId;

            GoogleCredential credential;
            using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.Spreadsheets);
            }

            _sheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "TribeBot",
            });
        }

        // ------------------------------------------------------
        // MEMBERS
        // ------------------------------------------------------
        public async Task<Member?> GetMemberAsync(string discordUserId)
        {
            var members = await GetAllMembersAsync();
            return members.FirstOrDefault(m => m.DiscordUserId == discordUserId);
        }

        public async Task<List<Member>> GetAllMembersAsync()
        {
            var range = $"{MembersSheet}!A2:I";
            var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, range);
            var response = await request.ExecuteAsync();
            var values = response.Values;

            var list = new List<Member>();
            if (values == null) return list;

            foreach (var row in values)
            {
                if (row.Count < 9) continue;

                var member = new Member
                {
                    DiscordUserId = row[0]?.ToString() ?? "",
                    IngameName = row[1]?.ToString() ?? "",
                    IngameId = row[2]?.ToString() ?? "",
                    Might = int.TryParse(row[3]?.ToString(), out var m) ? m : 0,
                    KillPoints = long.TryParse(row[4]?.ToString(), out var k) ? k : 0,
                    CollectorLevel = int.TryParse(row[5]?.ToString(), out var c) ? c : 0,
                    ReignPoints = int.TryParse(row[6]?.ToString(), out var rp) ? rp : 0,
                    LastUpdatedUTC = DateTime.TryParse(row[7]?.ToString(), out var d) ? d : DateTime.MinValue,
                    IsExempt = bool.TryParse(row[8]?.ToString(), out var ex) ? ex : false
                };

                list.Add(member);
            }

            return list;
        }

        public async Task SaveMemberAsync(Member member)
        {
            var members = await GetAllMembersAsync();
            int index = members.FindIndex(m => m.DiscordUserId == member.DiscordUserId);

            var values = new List<object>
            {
                member.DiscordUserId,
                member.IngameName,
                member.IngameId,
                member.Might,
                member.KillPoints,
                member.CollectorLevel,
                member.ReignPoints,
                member.LastUpdatedUTC.ToString("o"),
                member.IsExempt
            };

            if (index == -1)
            {
                var append = _sheetsService.Spreadsheets.Values.Append(
                    new ValueRange { Values = new List<IList<object>> { values } },
                    _spreadsheetId,
                    $"{MembersSheet}!A:I");
                append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                await append.ExecuteAsync();
            }
            else
            {
                var row = index + 2;
                var range = $"{MembersSheet}!A{row}:I{row}";

                var update = _sheetsService.Spreadsheets.Values.Update(
                    new ValueRange { Values = new List<IList<object>> { values } },
                    _spreadsheetId,
                    range);
                update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                await update.ExecuteAsync();
            }
        }

        // ------------------------------------------------------
        // REIGN
        // ------------------------------------------------------
        public async Task AddReignRegistrationAsync(ReignRegistration reg)
        {
            var values = new List<object>
            {
                reg.DiscordUserId,
                reg.IngameName,
                reg.AppliedAtUtc.ToString("o")
            };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{ReignSheet}!A:C");
            append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }

        public async Task<List<ReignRegistration>> GetAllReignRegistrationsAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, $"{ReignSheet}!A2:C");
            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<ReignRegistration>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 3) continue;

                list.Add(new ReignRegistration
                {
                    DiscordUserId = row[0].ToString(),
                    IngameName = row[1].ToString(),
                    AppliedAtUtc = DateTime.TryParse(row[2].ToString(), out var dt) ? dt : DateTime.MinValue
                });
            }

            return list;
        }

        public async Task ClearReignRegistrationsAsync()
        {
            var clear = new ClearValuesRequest();
            await _sheetsService.Spreadsheets.Values.Clear(clear, _spreadsheetId, $"{ReignSheet}!A2:C").ExecuteAsync();
        }

        // ------------------------------------------------------
        // DONATIONS
        // ------------------------------------------------------
        public async Task AddDonationAsync(DonationRecord record)
        {
            var values = new List<object>
            {
                record.DiscordUserId,
                record.IngameName,
                record.Amount,
                record.TimestampUtc.ToString("o"),
                record.WeekStartUtc.ToString("o"),
                record.WeekEndUtc.ToString("o")
            };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{DonationsSheet}!A:F");

            append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            await append.ExecuteAsync();
        }

        public async Task<List<DonationRecord>> GetDonationsForWeekAsync(DateTime weekStartUtc)
        {
            var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, $"{DonationsSheet}!A2:F");
            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<DonationRecord>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 6) continue;

                var rowWeekStart = DateTime.TryParse(row[4]?.ToString(), out var wk) ? wk : DateTime.MinValue;

                if (rowWeekStart == weekStartUtc)
                {
                    list.Add(new DonationRecord
                    {
                        DiscordUserId = row[0].ToString(),
                        IngameName = row[1].ToString(),
                        Amount = int.TryParse(row[2].ToString(), out var am) ? am : 0,
                        TimestampUtc = DateTime.TryParse(row[3].ToString(), out var ts) ? ts : DateTime.MinValue,
                        WeekStartUtc = rowWeekStart,
                        WeekEndUtc = DateTime.TryParse(row[5].ToString(), out var we) ? we : DateTime.MinValue
                    });
                }
            }

            return list;
        }
    }
}
