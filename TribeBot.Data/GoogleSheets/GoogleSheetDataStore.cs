using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.Globalization;
using TribeBot.Core.Entities;
using TribeBot.Core.Enums;
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
        private const string FinesSheet = "Fines";
        private const string PollSheet = "Polls";
        private const string PollVotesSheet = "PollVotes";
        private const string ScheduledEventsSheet = "ScheduledEvents";
        private const string TitleQueueSheet = "TitleQueue";
        private const string FarmTribesSheet = "FarmTribes";
        private const string FarmsSheet = "Farms";
        private const string FarmTribesAssignmentsSheet = "FarmTribeAssignments";
        private const string KvKEventsSheet = "KvKEvents";
        private const string KvKTimedEventsSheet = "KvKTimedEvents";
        private const string RaidEventsSheet = "RaidEvents";
        private const string RaidSignupsSheet = "RaidSignups";
        private const string DeliveryEventsSheet = "DeliveryEvents";
        private const string DeliveryEntriesSheet = "DeliveryEntries";



        //Multiple use Gid's 
        private const int PollsSheetId = 1167930524;
        private const int PollVotesSheetId = 994564864;
        private const int TitleQueueSheetId = 1775322331;
        private const int FarmTribesSheetId = 1089417883;
        private const int FarmsSheetId = 1580596082;
        private const int FarmTribeAssignmentsSheetId = 1758889070;


        private int GetFarmTribesSheetId() => FarmTribesSheetId;




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
            var range = $"{MembersSheet}!A2:J";
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
                    ReignPoints = long.TryParse(row[6]?.ToString(), out var rp) ? rp : 0,
                    LastUpdatedUTC = DateTime.TryParse(row[7]?.ToString(), out var d) ? d : DateTime.MinValue,

                    BankExempt = bool.TryParse(row[8]?.ToString(), out var be) ? be : false,
                    DeliveryExempt = bool.TryParse(row[9]?.ToString(), out var de) ? de : false
                };

                list.Add(member);
            }

            return list;
        }

        public async Task RemoveFarmsForUserAsync(string discordUserId)
        {
            var allFarms = await GetAllFarmsAsync();
            var rowIndexes = allFarms
                .Select((f, i) => new { f, i })
                .Where(x => x.f.OwnerDiscordId == discordUserId)
                .Select(x => x.i + 1) // +1 for header row
                .OrderByDescending(r => r)
                .ToList();

            if (rowIndexes.Count == 0) return;

            var requests = rowIndexes.Select(row => new Google.Apis.Sheets.v4.Data.Request
            {
                DeleteDimension = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
                {
                    Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                    {
                        SheetId = FarmsSheetId,
                        Dimension = "ROWS",
                        StartIndex = row,
                        EndIndex = row + 1
                    }
                }
            }).ToList();

            var batch = new BatchUpdateSpreadsheetRequest { Requests = requests };
            await _sheetsService.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
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
                member.BankExempt,
                member.DeliveryExempt
            };

            if (index == -1)
            {
                var append = _sheetsService.Spreadsheets.Values.Append(
                    new ValueRange { Values = new List<IList<object>> { values } },
                    _spreadsheetId,
                    $"{MembersSheet}!A:J");  // ✅
                append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                await append.ExecuteAsync();
            }
            else
            {
                var row = index + 2;
                var range = $"{MembersSheet}!A{row}:J{row}";  // ✅
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

        public async Task<bool> GetReignLockedAsync()
        {
            string range = "Settings!A2:B2";

            var response = await _sheetsService.Spreadsheets.Values
                .Get(_spreadsheetId, range)
                .ExecuteAsync();

            if (response.Values == null || response.Values.Count == 0)
                return false;

            // Ensure B2 exists
            if (response.Values[0].Count < 2)
                return false;

            string val = response.Values[0][1].ToString().Trim().ToLower();
            return val == "true";
        }


        public async Task SetReignLockedAsync(bool locked)
        {
            string range = "Settings!A2:B2";

            var body = new ValueRange
            {
                MajorDimension = "ROWS",   // ← important
                Values = new List<IList<object>>
        {
            new List<object>
            {
                "ReignLocked",
                locked ? "true" : "false"
            }
        }
            };

            var request = _sheetsService.Spreadsheets.Values.Update(body, _spreadsheetId, range);

            request.ValueInputOption = SpreadsheetsResource
                .ValuesResource
                .UpdateRequest
                .ValueInputOptionEnum
                .RAW;

            await request.ExecuteAsync();
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

        public async Task<bool> RemoveMemberByDiscordIdAsync(string discordId)
        {
            var members = await GetAllMembersAsync();
            int index = members.FindIndex(m => m.DiscordUserId == discordId);

            if (index == -1)
                return false;

            int row = index + 2; // Account for header
            var deleteRequest = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
            {
                Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                {
                    SheetId = 0,            // Sheet 0 = Members
                    Dimension = "ROWS",
                    StartIndex = row - 1,
                    EndIndex = row
                }
            };

            var batch = new Google.Apis.Sheets.v4.Data.BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Google.Apis.Sheets.v4.Data.Request>
        {
            new Google.Apis.Sheets.v4.Data.Request { DeleteDimension = deleteRequest }
        }
            };

            await _sheetsService.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
            return true;
        }

        // ------------------------------------------------------
        // FINES
        // ------------------------------------------------------

        public async Task AddFineAsync(FineRecord fine)
        {

            var values = new List<object>
            {
                fine.FineId,
                fine.DiscordUserId,
                fine.IngameName,
                fine.Amount,
                fine.FineType,
                fine.IsPaid,
                fine.PaidAmount,
                fine.ReignStrikes,
                fine.Notes,
                fine.IssuedAtUtc.ToString("O")
            };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{FinesSheet}!A:J");

            append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            await append.ExecuteAsync();
        }

        public async Task<List<FineRecord>> GetAllFinesAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, $"{FinesSheet}!A2:J");
            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<FineRecord>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 10) continue;

                list.Add(new FineRecord
                {
                    FineId = row[0].ToString(),
                    DiscordUserId = row[1].ToString(),
                    IngameName = row[2].ToString(),
                    Amount = int.TryParse(row[3]?.ToString(), out var a) ? a : 0,
                    FineType = row[4].ToString(),
                    IsPaid = bool.TryParse(row[5]?.ToString(), out var p) ? p : false,
                    PaidAmount = int.TryParse(row[6]?.ToString(), out var pa) ? pa : 0,
                    ReignStrikes = int.TryParse(row[7]?.ToString(), out var rs) ? rs : 0,
                    Notes = row[8].ToString(),
                    IssuedAtUtc = DateTime.TryParse(row[9]?.ToString(), out var dt) ? dt : DateTime.MinValue
                });
            }
            return list;
        }

        public async Task UpdateFineAsync(FineRecord fine)
        {

            var all = await GetAllFinesAsync();
            int index = all.FindIndex(f => f.FineId == fine.FineId);

            if (index == -1)
                return;

            var values = new List<object>
            {
               fine.FineId,
               fine.DiscordUserId,
               fine.IngameName,
               fine.Amount,
               fine.FineType,
               fine.IsPaid,
               fine.PaidAmount,
               fine.ReignStrikes,
               fine.Notes,
               fine.IssuedAtUtc.ToString("o")
            };

            var row = index + 2; // Account for header

            var update = _sheetsService.Spreadsheets.Values.Update(
            new ValueRange { Values = new List<IList<object>> { values } },
            _spreadsheetId,
            $"{FinesSheet}!A{row}:J{row}");

            update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await update.ExecuteAsync();
        }

        public async Task RemoveFineByIdAsync(string fineId)
        {
            var all = await GetAllFinesAsync();
            int index = all.FindIndex(f => f.FineId == fineId);

            if (index == -1)
                return;

            int row = index + 2;

            var deleteRequest = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
            {
                Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                {
                    SheetId = 546582633,  //GID listed at top of URL bar
                    Dimension = "ROWS",
                    StartIndex = row - 1,
                    EndIndex = row
                }
            };

            var batch = new Google.Apis.Sheets.v4.Data.BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Google.Apis.Sheets.v4.Data.Request>
        {
            new Google.Apis.Sheets.v4.Data.Request { DeleteDimension = deleteRequest }
        }
            };

            await _sheetsService.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
        }


        // AddPoll

        public async Task AddPollAsync(PollRecord poll)
        {

            var values = new List<object>
            {
                poll.PollId,
                poll.Question,
                poll.EndDateUtc.ToString("o"),
                Newtonsoft.Json.JsonConvert.SerializeObject(poll.Options),
                poll.CreatedByDiscordId,
                poll.CreatedAtUtc.ToString("o")
            };

            var append = _sheetsService.Spreadsheets.Values.Append(
            new ValueRange { Values = new List<IList<object>> { values } },
            _spreadsheetId,
            $"{PollSheet}!A:F");

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();

        }

        //Get all polls
        public async Task<List<PollRecord>> GetAllPollsAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{PollSheet}!A2:F");

            var response = await request.ExecuteAsync();
            var list = new List<PollRecord>();

            if (response.Values == null)
            {
                return list;
            }

            foreach (var row in response.Values)
            {
                if (row.Count < 6) continue;

                list.Add(new PollRecord
                {
                    PollId = row[0].ToString(),
                    Question = row[1].ToString(),
                    EndDateUtc = DateTime.Parse(row[2].ToString()),
                    Options = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(row[3].ToString()),
                    CreatedByDiscordId = row[4].ToString(),
                    CreatedAtUtc = DateTime.Parse(row[5].ToString())
                });
            }
            return list;
        }

        //Remove poll
        public async Task RemovePollAsync(string pollId)
        {
            var polls = await GetAllPollsAsync();
            int index = polls.FindIndex(p => p.PollId == pollId);

            if (index == -1)
                return;

            int row = index + 2;

            var delete = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
            {
                Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                {
                    SheetId = PollsSheetId,
                    Dimension = "ROWS",
                    StartIndex = row - 1,
                    EndIndex = row
                }
            };

            var batch = new Google.Apis.Sheets.v4.Data.BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Google.Apis.Sheets.v4.Data.Request>
        {
            new Google.Apis.Sheets.v4.Data.Request { DeleteDimension = delete }
        }
            };

            await _sheetsService.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
        }

        // AddPollVote
        public async Task AddPollVoteAsync(PollVoteRecord vote)
        {
            var values = new List<object>
             {
                 vote.PollId,
                 vote.DiscordUserId,
                 vote.IngameName,
                 vote.Choice,
                 vote.TimestampUtc.ToString("o")
             };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{PollVotesSheet}!A:E");

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }

        //Get votes for Pol

        public async Task<List<PollVoteRecord>> GetVotesForPollAsync(string pollId)
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{PollVotesSheet}!A2:E");

            var response = await request.ExecuteAsync();
            var list = new List<PollVoteRecord>();

            if (response.Values == null)
                return list;

            foreach (var row in response.Values)
            {
                if (row.Count < 5) continue;
                if (row[0].ToString() != pollId) continue;

                list.Add(new PollVoteRecord
                {
                    PollId = row[0].ToString(),
                    DiscordUserId = row[1].ToString(),
                    IngameName = row[2].ToString(),
                    Choice = row[3].ToString(),
                    TimestampUtc = DateTime.Parse(row[4].ToString())
                });
            }

            return list;
        }

        public async Task RemoveVotesForPollAsync(string pollId)
        {

            //Load all rows 
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{PollVotesSheet}!A2:E");

            var response = await request.ExecuteAsync();
            if (response.Values == null) return;

            var rows = response.Values;
            var deleteRequests = new List<Google.Apis.Sheets.v4.Data.Request>();

            int rowIndex = 2; // Sheet row number (Header is row 1)

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                if (row.Count > 0 && row[0].ToString() == pollId)
                {
                    deleteRequests.Add(new Google.Apis.Sheets.v4.Data.Request
                    {
                        DeleteDimension = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
                        {
                            Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                            {
                                SheetId = PollVotesSheetId,
                                Dimension = "ROWS",
                                StartIndex = rowIndex - 1, //Inclusive
                                EndIndex = rowIndex //Exclusive
                            }
                        }
                    });
                }
                rowIndex++;
            }

            if (deleteRequests.Count == 0)
            {
                return;
            }

            // reverse order > delete bottom up 
            deleteRequests.Reverse();

            var batch = new Google.Apis.Sheets.v4.Data.BatchUpdateSpreadsheetRequest
            {
                Requests = deleteRequests
            };

            await _sheetsService.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
        }

        //Only do one read not whole list when calling votes

        public async Task<PollVoteRecord?> GetVoteAsync(string pollId, string discordUserId)
        {

            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{PollVotesSheet}!A2:E");

            var response = await request.ExecuteAsync();
            if (response.Values == null) return null;

            int rowIndex = 2; // sheet row number (account for header)

            foreach (var row in response.Values)
            {
                if (row.Count >= 2 &&
                    row[0].ToString() == pollId &&
                    row[1].ToString() == discordUserId)
                {
                    return new PollVoteRecord
                    {
                        PollId = row[0].ToString(),
                        DiscordUserId = row[1].ToString(),
                        IngameName = row[2].ToString(),
                        Choice = row[3].ToString(),
                        TimestampUtc = DateTime.Parse(row[4].ToString()),
                    };
                }
            }

            return null;
        }


        // Remove vote for user
        public async Task RemoveVoteAsync(string pollId, string discordUserId)
        {
            // Load vote rows (Don't include the header) 

            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{PollVotesSheet}!A2:E");

            var response = await request.ExecuteAsync();
            if (response.Values == null) return;

            int rowIndex = 2; // sheet row number (account for header)

            foreach (var row in response.Values)
            {
                if (row.Count >= 2 &&
                    row[0].ToString() == pollId &&
                    row[1].ToString() == discordUserId)
                {
                    // Found the row > Delete only this exact row (The current vote thats being overwritten) 

                    var delete = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
                    {
                        Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                        {
                            SheetId = PollVotesSheetId,
                            Dimension = "ROWS",
                            StartIndex = rowIndex - 1, //Inclusive
                            EndIndex = rowIndex //Exclusive
                        }
                    };

                    var batch = new Google.Apis.Sheets.v4.Data.BatchUpdateSpreadsheetRequest
                    {
                        Requests = new List<Google.Apis.Sheets.v4.Data.Request>
                        {
                            new Google.Apis.Sheets.v4.Data.Request { DeleteDimension = delete }
                        }
                    };

                    await _sheetsService.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
                    return;
                }

                rowIndex++;
            }
        }

        public async Task SetReignRegistrationsAsync(List<ReignRegistration> registrations)
        {
            // 1. Clear old data (but keep header row)
            var clear = new ClearValuesRequest();
            await _sheetsService.Spreadsheets.Values.Clear(
                clear,
                _spreadsheetId,
                $"{ReignSheet}!A2:C"
            ).ExecuteAsync();

            // 2. If there are no registrations left, we stop here.
            if (registrations.Count == 0)
                return;

            // 3. Prepare rows for writing back
            var values = new List<IList<object>>();

            foreach (var reg in registrations)
            {
                values.Add(new List<object>
        {
            reg.DiscordUserId,
            reg.IngameName,
            reg.AppliedAtUtc.ToString("o")
        });
            }

            var body = new ValueRange
            {
                Values = values
            };

            // 4. Write them all starting at A2
            var update = _sheetsService.Spreadsheets.Values.Update(
                body,
                _spreadsheetId,
                $"{ReignSheet}!A2:C"
            );

            update.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

            await update.ExecuteAsync();
        }

        public async Task AddScheduledEventAsync(ScheduledEvent evt)
        {
            var values = new List<object>
            {
                evt.EventId,
                evt.EventName,
                evt.EventDateUtc.ToString("o"),
                evt.ReminderOffsetHours,
                evt.Message,
                evt.CreatedBy,
                evt.CreatedAtUtc.ToString("o"),
                evt.ReminderSent,
                evt.Completed
            };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{ScheduledEventsSheet}!A:I");

            append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            await append.ExecuteAsync();
        }

        public async Task<List<ScheduledEvent>> GetAllScheduledEventsAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{ScheduledEventsSheet}!A2:I");

            var response = await request.ExecuteAsync();
            var rows = response.Values;
            var list = new List<ScheduledEvent>();

            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 9) continue;

                // Skip row if EventDateUtc is not a valid ISO string
                if (!DateTime.TryParse(row[2].ToString(), out var parsedDate))
                    continue;

                list.Add(new ScheduledEvent
                {
                    EventId = row[0].ToString(),
                    EventName = row[1].ToString(),
                    EventDateUtc = DateTime.Parse(row[2].ToString()),
                    ReminderOffsetHours = int.Parse(row[3].ToString()),
                    Message = row[4].ToString(),
                    CreatedBy = row[5].ToString(),
                    CreatedAtUtc = DateTime.Parse(row[6].ToString()),
                    ReminderSent = bool.Parse(row[7].ToString()),
                    Completed = bool.Parse(row[8].ToString())
                });
            }

            return list;
        }

        public async Task UpdateScheduledEventAsync(ScheduledEvent evt)
        {
            var all = await GetAllScheduledEventsAsync();

            int index = all.FindIndex(e => e.EventId == evt.EventId);

            if (index == -1) return;

            int row = index + 2;

            var values = new List<object>
            {
                evt.EventId,
                evt.EventName,
                evt.EventDateUtc.ToString("o"),
                evt.ReminderOffsetHours,
                evt.Message,
                evt.CreatedBy,
                evt.CreatedAtUtc.ToString("o"),
                evt.ReminderSent,
                evt.Completed
            };

            var update = _sheetsService.Spreadsheets.Values.Update(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{ScheduledEventsSheet}!A{row}:I{row}"
                );

            update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await update.ExecuteAsync();
        }

        public async Task<List<TitleApplicant>> GetAllTitleApplicantsAsync()
        {

            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{TitleQueueSheet}!A2:C"
            );

            var response = await request.ExecuteAsync();
            var rows = response.Values;

            List<TitleApplicant> list = new();

            if (rows == null) { return list; }

            foreach (var row in rows)
            {
                if (row.Count() < 3) continue;

                list.Add(new TitleApplicant
                {
                    Title = row[0].ToString(),
                    DiscordUserId = row[1].ToString(),
                    AppliedUtc = row[2].ToString()
                });
            }
            return list;
        }

        public async Task<List<TitleApplicant>> GetTitleQueueAsync(string title)
        {
            var all = await GetAllTitleApplicantsAsync();

            return all
                .Where(a => a.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => DateTime.Parse(a.AppliedUtc))
                .ToList();
        }

        public async Task AddTitleApplicantAsync(string title, string discordUserId)
        {
            var values = new List<object>
             {
                 title,
                 discordUserId,
                 DateTime.UtcNow.ToString("o")
             };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{TitleQueueSheet}!A:C"
            );

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }
        public async Task RemoveTitleApplicantAsync(string discordUserId)
        {
            var all = await GetAllTitleApplicantsAsync();

            int index = all.FindIndex(a => a.DiscordUserId == discordUserId);
            if (index == -1)
                return;

            // Sheets rows start at 2 (row 1 = header)
            int row = index + 2;

            var delete = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
            {
                Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                {
                    SheetId = TitleQueueSheetId,
                    Dimension = "ROWS",
                    StartIndex = row - 1,
                    EndIndex = row
                }
            };

            var batch = new Google.Apis.Sheets.v4.Data.BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Google.Apis.Sheets.v4.Data.Request>
                 {
                     new Google.Apis.Sheets.v4.Data.Request { DeleteDimension = delete }
                 }
            };

            await _sheetsService.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
        }

        public async Task<string?> GetNextTitleRotationUtcAsync(string title)
        {
            string key = title.Equals("tycoon", StringComparison.OrdinalIgnoreCase)
                ? "NextTycoonRotationUtc"
                : "NextPriestRotationUtc";

            var response = await _sheetsService.Spreadsheets.Values
                .Get(_spreadsheetId, "Settings!A2:B100")
                .ExecuteAsync();

            if (response.Values == null)
                return null;

            foreach (var row in response.Values)
            {
                if (row.Count < 2) continue;

                string rowKey = row[0]?.ToString()?.Trim() ?? "";
                if (rowKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return row[1]?.ToString();
            }

            return null;
        }


        public async Task SetNextTitleRotationUtcAsync(string title, string utcTimestamp)
        {
            string key = title.Equals("tycoon", StringComparison.OrdinalIgnoreCase)
                ? "NextTycoonRotationUtc"
                : "NextPriestRotationUtc";

            var response = await _sheetsService.Spreadsheets.Values
                .Get(_spreadsheetId, "Settings!A2:B100")
                .ExecuteAsync();

            if (response.Values == null)
                return;

            int rowIndex = -1;

            for (int i = 0; i < response.Values.Count; i++)
            {
                string rowKey = CleanKey(response.Values[i][0]?.ToString() ?? "");
                if (rowKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    rowIndex = i + 2;
                    break;
                }
            }

            if (rowIndex == -1)
                return;

            var body = new ValueRange
            {
                Values = new List<IList<object>>
        {
            new List<object> { key, utcTimestamp }
        }
            };

            var update = _sheetsService.Spreadsheets.Values.Update(
                body,
                _spreadsheetId,
                $"Settings!A{rowIndex}:B{rowIndex}"
            );

            update.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

            await update.ExecuteAsync();
        }
        private string CleanKey(string raw)
        {
            if (raw == null) return "";

            return raw
                .Replace("\uFEFF", "")  // zero width no-break space
                .Replace("\u200B", "")  // zero width space
                .Replace("\u200F", "")  // RTL mark
                .Replace("\u00A0", "")  // non-breaking space
                .Trim();
        }

        public async Task<string?> GetLastAwardedUserIdAsync(string title)
        {
            string key = title.Equals("tycoon", StringComparison.OrdinalIgnoreCase)
                ? "LastTycoonAwardedDiscordId"
                : "LastPriestAwardedDiscordId";

            var response = await _sheetsService.Spreadsheets.Values
                .Get(_spreadsheetId, "Settings!A2:B100")
                .ExecuteAsync();

            if (response.Values == null)
                return null;

            foreach (var row in response.Values)
            {
                if (row.Count < 2) continue;

                string rowKey = row[0]?.ToString()?.Trim() ?? "";
                if (rowKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return row[1]?.ToString();
            }

            return null;
        }
        public async Task SetLastAwardedUserIdAsync(string title, string discordUserId)
        {
            string key = title.Equals("tycoon", StringComparison.OrdinalIgnoreCase)
                ? "LastTycoonAwardedDiscordId"
                : "LastPriestAwardedDiscordId";

            var response = await _sheetsService.Spreadsheets.Values
                .Get(_spreadsheetId, "Settings!A2:B100")
                .ExecuteAsync();

            if (response.Values == null)
                return;

            int rowIndex = -1;

            for (int i = 0; i < response.Values.Count; i++)
            {
                string rowKey = response.Values[i][0]?.ToString()?.Trim() ?? "";
                if (rowKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    rowIndex = i + 2;
                    break;
                }
            }

            if (rowIndex == -1)
                return;

            var body = new ValueRange
            {
                Values = new List<IList<object>>
        {
            new List<object> { key, discordUserId }
        }
            };

            var update = _sheetsService.Spreadsheets.Values.Update(
                body,
                _spreadsheetId,
                $"Settings!A{rowIndex}:B{rowIndex}"
            );

            update.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

            await update.ExecuteAsync();
        }

        // Register a farm tribe to the system 
        public async Task AddFarmTribeAsync(FarmTribe tribe)
        {
            var values = new List<object>
            {
                tribe.FarmTribeId,
                tribe.FarmTribeName,
                tribe.TotalSlots,
                tribe.UsedSlots,
                tribe.CreatedUtc.ToString("o")
            };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{FarmTribesSheet}!A:E"
            );

            append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }

        public async Task<List<FarmTribe>> GetAllFarmTribesAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmTribesSheet}!A2:E"
            );

            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<FarmTribe>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 5) continue;

                list.Add(new FarmTribe
                {
                    FarmTribeId = row[0].ToString(),
                    FarmTribeName = row[1].ToString(),
                    TotalSlots = int.TryParse(row[2].ToString(), out var ts) ? ts : 0,
                    UsedSlots = int.TryParse(row[3].ToString(), out var us) ? us : 0,
                    CreatedUtc = DateTime.TryParse(row[4].ToString(), out var dt)
                        ? dt
                        : DateTime.MinValue
                });
            }

            return list;
        }


        public async Task UpdateFarmTribeAsync(FarmTribe tribe)
        {
            var all = await GetAllFarmTribesAsync();
            int index = all.FindIndex(t => t.FarmTribeId == tribe.FarmTribeId);

            if (index == -1)
                return;

            int row = index + 2; // header offset

            var values = new List<object>
    {
        tribe.FarmTribeId,
        tribe.FarmTribeName,
        tribe.TotalSlots,
        tribe.UsedSlots,
        tribe.CreatedUtc.ToString("o")
    };

            var update = _sheetsService.Spreadsheets.Values.Update(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{FarmTribesSheet}!A{row}:E{row}"
            );

            update.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

            await update.ExecuteAsync();
        }


        public async Task<FarmTribe?> GetFarmTribeByIdAsync(string farmTribeId)
        {
            var all = await GetAllFarmTribesAsync();
            return all.FirstOrDefault(t => t.FarmTribeId == farmTribeId);
        }

        public async Task DeleteFarmTribeAsync(string farmTribeId)
        {
            // Load all farm tribes (already parsed)
            var all = await GetAllFarmTribesAsync();

            int index = all.FindIndex(t => t.FarmTribeId == farmTribeId);
            if (index == -1)
                throw new InvalidOperationException("Farm tribe not found.");

            // Sheet rows start at 2 (row 1 = header)
            int row = index + 2;

            var deleteRequest = new Google.Apis.Sheets.v4.Data.DeleteDimensionRequest
            {
                Range = new Google.Apis.Sheets.v4.Data.DimensionRange
                {
                    SheetId = GetFarmTribesSheetId(),
                    Dimension = "ROWS",
                    StartIndex = row - 1, // inclusive
                    EndIndex = row        // exclusive
                }
            };

            var batch = new Google.Apis.Sheets.v4.Data.BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Google.Apis.Sheets.v4.Data.Request>
        {
            new Google.Apis.Sheets.v4.Data.Request
            {
                DeleteDimension = deleteRequest
            }
        }
            };

            await _sheetsService
                .Spreadsheets
                .BatchUpdate(batch, _spreadsheetId)
                .ExecuteAsync();
        }


        // Player adds farm to their profile(DiscordId)
        public async Task AddFarmAsync(Farm farm)
        {
            var values = new List<object>
    {
        farm.FarmId,
        farm.FarmName,
        farm.OwnerDiscordId,
        farm.OwnerIngameName,
        farm.RegisteredUtc.ToString("o")
    };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{FarmsSheet}!A:E"
            );

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }

        // Getting all the farms, more to fill lists, not making this a display command 
        public async Task<List<Farm>> GetAllFarmsAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmsSheet}!A2:E"
            );

            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<Farm>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 5) continue;

                list.Add(new Farm
                {
                    FarmId = row[0].ToString(),
                    FarmName = row[1].ToString(),
                    OwnerDiscordId = row[2].ToString(),
                    OwnerIngameName = row[3].ToString(),
                    RegisteredUtc = DateTime.Parse(row[4].ToString())
                });
            }

            return list;
        }
        public async Task<List<FarmRow>> GetAllFarmRowsAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmsSheet}!A2:E"
            );

            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<FarmRow>();
            if (rows == null) return list;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Count < 5) continue;

                list.Add(new FarmRow
                {
                    RowIndex = i + 2, // 🔥 THIS is the key fix
                    Farm = new Farm
                    {
                        FarmId = row[0].ToString(),
                        FarmName = row[1].ToString(),
                        OwnerDiscordId = row[2].ToString(),
                        OwnerIngameName = row[3].ToString(),
                        RegisteredUtc = DateTime.Parse(row[4].ToString())
                    }
                });
            }

            return list;
        }
        public class FarmRow
        {
            public Farm Farm { get; set; }
            public int RowIndex { get; set; }
        }

        // Single ID, Linked to a user or returning NULL if farm has no owner
        public async Task<Farm?> GetFarmByIdAsync(string farmId)
        {
            var all = await GetAllFarmsAsync();
            return all.FirstOrDefault(f => f.FarmId == farmId);
        }

        public async Task<List<Farm>> GetFarmsByOwnerAsync(string discordUserId)
        {
            var all = await GetAllFarmsAsync();
            return all.Where(f => f.OwnerDiscordId == discordUserId).ToList();
        }

        // Remove a farm (Player method)
        public async Task RemoveFarmAsync(string farmId)
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmsSheet}!A2:E"
            );

            var response = await request.ExecuteAsync();
            if (response.Values == null)
                throw new InvalidOperationException("No farms found.");

            int index = -1;

            for (int i = 0; i < response.Values.Count; i++)
            {
                if (response.Values[i].Count > 0 &&
                    response.Values[i][0].ToString() == farmId)
                {
                    index = i;
                    break;
                }
            }

            if (index == -1)
                throw new InvalidOperationException("Farm not found.");

            int row = index + 2; // header offset

            var deleteRequest = new DeleteDimensionRequest
            {
                Range = new DimensionRange
                {
                    SheetId = FarmsSheetId,
                    Dimension = "ROWS",
                    StartIndex = row - 1,
                    EndIndex = row
                }
            };

            var batch = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>
        {
            new Request { DeleteDimension = deleteRequest }
        }
            };

            await _sheetsService
                .Spreadsheets
                .BatchUpdate(batch, _spreadsheetId)
                .ExecuteAsync();
        }


        //Get the asignment per player
        public async Task<PlayerFarmTribeAssignment?> GetAssignmentForUserAsync(string discordUserId)
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmTribesAssignmentsSheet}!A2:C");

            var response = await request.ExecuteAsync();
            if (response.Values == null)
            {
                return null;
            }

            foreach (var row in response.Values)
            {
                if (row.Count < 3)
                    continue;

                if (row[0].ToString() == discordUserId)
                {
                    return new PlayerFarmTribeAssignment
                    {
                        DiscordUserId = row[0].ToString(),
                        FarmTribeId = row[1].ToString(),
                        AssignedUtc = DateTime.TryParse(row[2].ToString(), out var dt)
                            ? dt
                            : DateTime.MinValue,
                    };
                }
            }
            return null;
        }

        // RETURN LIST OF MEMBERS ASSIGNED TO A TRIBE
        public async Task<List<PlayerFarmTribeAssignment>> GetAssignmentsForTribeAsync(string farmTribeId)
        {

            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmTribesAssignmentsSheet}!A2:C");

            var response = await request.ExecuteAsync();
            var list = new List<PlayerFarmTribeAssignment>();

            if (response.Values == null)
                return list;

            foreach (var row in response.Values)
            {
                if (row.Count < 3)
                    continue;

                if (string.Equals(row[1]?.ToString(), farmTribeId, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new PlayerFarmTribeAssignment
                    {
                        DiscordUserId = row[0].ToString(),
                        FarmTribeId = row[1].ToString(),
                        AssignedUtc = DateTime.TryParse(row[2].ToString(), out var dt)
                            ? dt
                            : DateTime.MinValue
                    });
                }
            }
            return list;
        }

        public async Task AddAssignmentAsync(PlayerFarmTribeAssignment assignment)
        {
            var values = new List<object>
    {
        assignment.DiscordUserId,
        assignment.FarmTribeId,
        assignment.AssignedUtc.ToString("o")
    };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{FarmTribesAssignmentsSheet}!A:C"
            );

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }


        public async Task RemoveAssignmentAsync(string discordUserId)
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmTribesAssignmentsSheet}!A2:C"
            );

            var response = await request.ExecuteAsync();
            if (response.Values == null)
                return;

            int index = -1;

            for (int i = 0; i < response.Values.Count; i++)
            {
                if (response.Values[i].Count > 0 &&
                    response.Values[i][0].ToString() == discordUserId)
                {
                    index = i;
                    break;
                }
            }

            if (index == -1)
                return;

            int row = index + 2; // header offset

            var deleteRequest = new DeleteDimensionRequest
            {
                Range = new DimensionRange
                {
                    SheetId = FarmTribeAssignmentsSheetId,
                    Dimension = "ROWS",
                    StartIndex = row - 1,
                    EndIndex = row
                }
            };

            var batch = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>
        {
            new Request { DeleteDimension = deleteRequest }
        }
            };

            await _sheetsService
                .Spreadsheets
                .BatchUpdate(batch, _spreadsheetId)
                .ExecuteAsync();
        }

        public async Task<List<PlayerFarmTribeAssignment>> GetAllAssignmentsAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{FarmTribesAssignmentsSheet}!A2:C");

            var response = await request.ExecuteAsync();
            var list = new List<PlayerFarmTribeAssignment>();

            if (response.Values == null)
                return list;

            foreach (var row in response.Values)
            {
                if (row.Count < 3)
                    continue;

                list.Add(new PlayerFarmTribeAssignment
                {
                    DiscordUserId = row[0].ToString(),
                    FarmTribeId = row[1].ToString(),
                    AssignedUtc = DateTime.TryParse(row[2].ToString(), out var dt)
                        ? dt
                        : DateTime.MinValue
                });
            }

            return list;
        }

        public async Task AddKvKEventAsync(KvKEvent kvk)
        {
            var values = new List<IList<object>>
             {
                 new List<object>
                 {
                     kvk.KvKId,
                     kvk.Name,
                     kvk.StartUtc.ToString("o"),
                     kvk.EndUtc.ToString("o"),
                     kvk.IsActive
                 }
             };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = values },
                _spreadsheetId,
                $"{KvKEventsSheet}!A:E"
            );

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest
                    .ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }


        public async Task<List<KvKEvent>> GetAllKvKEventsAsync()
        {
            var response = await _sheetsService.Spreadsheets.Values
                .Get(_spreadsheetId, $"{KvKEventsSheet}!A2:E")
                .ExecuteAsync();

            var list = new List<KvKEvent>();
            if (response.Values == null) return list;

            foreach (var row in response.Values)
            {
                if (row.Count < 5) continue;

                list.Add(new KvKEvent
                {
                    KvKId = row[0].ToString(),
                    Name = row[1].ToString(),
                    StartUtc = DateTime.SpecifyKind(
                    DateTime.Parse(row[2].ToString()),
                    DateTimeKind.Utc
                ),
                    EndUtc = DateTime.SpecifyKind(
                    DateTime.Parse(row[3].ToString()),
                    DateTimeKind.Utc
                ),
                    IsActive = bool.Parse(row[4].ToString())
                });
            }

            return list;
        }

        public async Task UpdateKvKEventAsync(KvKEvent kvk)
        {
            var all = await GetAllKvKEventsAsync();
            int index = all.FindIndex(x => x.KvKId == kvk.KvKId);
            if (index == -1)
                return;

            int row = index + 2; // +2 because row 1 is headers

            var values = new List<object>
               {
                   kvk.KvKId,
                   kvk.Name,
                   kvk.StartUtc.ToString("o"),
                   kvk.EndUtc.ToString("o"),
                   kvk.IsActive
               };

            var request = _sheetsService.Spreadsheets.Values.Update(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{KvKEventsSheet}!A{row}:E{row}"
            );

            request.ValueInputOption =
                Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource
                    .UpdateRequest.ValueInputOptionEnum.RAW;

            await request.ExecuteAsync();
        }


        public async Task<KvKEvent?> GetActiveKvKAsync()
        {
            var all = await GetAllKvKEventsAsync();
            return all.FirstOrDefault(x => x.IsActive);
        }
        public async Task AddKvKTimedEventAsync(KvKTimedEvent evt)
        {
            var values = new List<object>
            {
                evt.EventId,
                evt.KvKId,
                evt.EventType,
                evt.StartUtc.ToString("o"),
                evt.AnnouncementSent
            };

            var request = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{KvKTimedEventsSheet}!A:E"
            );

            request.ValueInputOption =
                Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource
                    .AppendRequest.ValueInputOptionEnum.RAW;

            await request.ExecuteAsync();
        }


        public async Task<List<KvKTimedEvent>> GetAllKvKTimedEventsAsync()
        {
            var response = await _sheetsService.Spreadsheets.Values
                .Get(_spreadsheetId, $"{KvKTimedEventsSheet}!A2:E")
                .ExecuteAsync();

            var list = new List<KvKTimedEvent>();
            if (response.Values == null) return list;

            foreach (var row in response.Values)
            {
                if (row.Count < 5) continue;

                list.Add(new KvKTimedEvent
                {
                    EventId = row[0].ToString(),
                    KvKId = row[1].ToString(),
                    EventType = row[2].ToString(),
                    StartUtc = DateTime.SpecifyKind(
                        DateTime.Parse(row[3].ToString()),
                        DateTimeKind.Utc
                    ),
                    AnnouncementSent = bool.Parse(row[4].ToString())
                });
            }

            return list;
        }

        public async Task<List<KvKTimedEvent>> GetTimedEventsForKvKAsync(string kvkId)
        {
            var all = await GetAllKvKTimedEventsAsync();
            return all.Where(x => x.KvKId == kvkId).ToList();
        }

        public async Task UpdateKvKTimedEventAsync(KvKTimedEvent evt)
        {
            var all = await GetAllKvKTimedEventsAsync();
            int index = all.FindIndex(x => x.EventId == evt.EventId);
            if (index == -1) return;

            int row = index + 2;

            var values = new List<object>
    {
        evt.EventId,
        evt.KvKId,
        evt.EventType,
        evt.StartUtc.ToString("o"),
        evt.AnnouncementSent
    };

            var request = _sheetsService.Spreadsheets.Values.Update(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{KvKTimedEventsSheet}!A{row}:E{row}"
            );
            request.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

            await request.ExecuteAsync();
        }

        public async Task CreateRaidAsync(Raid raid)
        {
            var values = new List<object>
    {
        raid.RaidId,
        raid.RaidType,                   
        raid.StartUtc.ToString("o"),
        raid.ChannelId.ToString(),
        raid.MessageId.ToString(),
        raid.IsClosed.ToString().ToLower()
    };

            var request = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{RaidEventsSheet}!A:F"
            );

            request.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;

            await request.ExecuteAsync();
        }


        public async Task<Raid?> GetRaidByMessageIdAsync(ulong messageId)
        {
            var all = await GetAllRaidsAsync();
            return all.FirstOrDefault(r => r.MessageId == messageId);
        }

        public async Task UpsertRaidSignupAsync(RaidSignup signup)
        {
            var all = await GetAllRaidSignupsAsync();
            var index = all.FindIndex(s =>
                s.RaidId == signup.RaidId &&
                s.UserId == signup.UserId);

            var values = new List<object>
    {
        signup.RaidId,
        signup.UserId.ToString(),
        signup.Response.ToString(),
        signup.UpdatedUtc.ToString("o")
    };

            if (index == -1)
            {
                var request = _sheetsService.Spreadsheets.Values.Append(
                    new ValueRange
                    {
                        Values = new List<IList<object>> { values }
                    },
                    _spreadsheetId,
                    $"{RaidSignupsSheet}!A:D"
                );

                request.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;

                await request.ExecuteAsync();
            }
            else
            {
                int row = index + 2;

                var request = _sheetsService.Spreadsheets.Values.Update(
                    new ValueRange
                    {
                        Values = new List<IList<object>> { values }
                    },
                    _spreadsheetId,
                    $"{RaidSignupsSheet}!A{row}:D{row}"
                );

                request.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;

                await request.ExecuteAsync();

            }
        }

        public async Task<List<RaidSignup>> GetRaidSignupsAsync(string raidId)
        {
            var all = await GetAllRaidSignupsAsync();
            return all.Where(s => s.RaidId == raidId).ToList();
        }



        //Raid helpers
        private async Task<List<RaidSignup>> GetAllRaidSignupsAsync()
        {
            var response = await _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{RaidSignupsSheet}!A:D"
            ).ExecuteAsync();

            var rows = response.Values?.Skip(1) ?? Enumerable.Empty<IList<object>>();

            return rows.Select(r => new RaidSignup
            {
                RaidId = r[0].ToString()!,
                UserId = ulong.Parse(r[1].ToString()!,CultureInfo.InvariantCulture),
                Response = Enum.Parse<RaidSignupResponse>(r[2].ToString()!),
                UpdatedUtc = DateTime.Parse(r[3].ToString()!).ToUniversalTime()
            }).ToList();
        }


        private async Task<List<Raid>> GetAllRaidsAsync()
        {
            var response = await _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{RaidEventsSheet}!A:F"
            ).ExecuteAsync();

            var rows = response.Values?.Skip(1) ?? Enumerable.Empty<IList<object>>();

            // Column indexes (NOT values)
            const int RaidIdColumn = 0;
            const int RaidTypeColumn = 1;
            const int StartUtcColumn = 2;
            const int ChannelIdColumn = 3;
            const int MessageIdColumn = 4;
            const int IsClosedColumn = 5;

            return rows.Select(r => new Raid
            {
                RaidId = r[0].ToString()!,
                RaidType = r[1].ToString()!,
                StartUtc = DateTime.Parse(r[2].ToString()!).ToUniversalTime(),
                ChannelId = ulong.Parse(r[3].ToString()!, CultureInfo.InvariantCulture),
                MessageId = ulong.Parse(r[4].ToString()!, CultureInfo.InvariantCulture),
                IsClosed = bool.Parse(r[5].ToString()!)
            }).ToList();
        }

        public async Task UpdateFarmAsync(string oldFarmId, Farm updatedFarm)
        {

            Console.WriteLine("=== UPDATE FARM DEBUG ===");
            Console.WriteLine($"OLD ID: {oldFarmId}");
            Console.WriteLine($"NEW ID: {updatedFarm.FarmId}");
            Console.WriteLine($"NEW NAME: {updatedFarm.FarmName}");

            var farmRows = await GetAllFarmRowsAsync();

            var match = farmRows.FirstOrDefault(f =>
                f.Farm.FarmId.Trim() == oldFarmId.Trim());

            if (match == null)
                throw new Exception("Farm not found.");

            Console.WriteLine($"FOUND ROW: {match.RowIndex}");

            int row = match.RowIndex; // ✅ REAL row

            var values = new List<object>
    {
        long.Parse(updatedFarm.FarmId),
        updatedFarm.FarmName,
        updatedFarm.OwnerDiscordId,
        updatedFarm.OwnerIngameName,
        updatedFarm.RegisteredUtc.ToString("o")
    };

            var request = _sheetsService.Spreadsheets.Values.Update(
                new ValueRange
                {
                    Values = new List<IList<object>> { values }
                },
                _spreadsheetId,
                $"{FarmsSheet}!A{row}:E{row}"
            );

            request.ValueInputOption =
                Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource
                    .UpdateRequest.ValueInputOptionEnum.RAW;

            await request.ExecuteAsync();
        }


        private async Task UpdateRowAsync(int rowIndex, IList<object> values)
        {
            var range = $"{FarmsSheet}!A{rowIndex}:E{rowIndex}";

            var valueRange = new ValueRange
            {
                Values = new List<IList<object>> { values }
            };

            var request = _sheetsService.Spreadsheets.Values.Update(
                valueRange,
                _spreadsheetId,
                range
            );

            request.ValueInputOption =
                Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource
                    .UpdateRequest.ValueInputOptionEnum.USERENTERED;

            await request.ExecuteAsync();
        }


        public async Task AddDeliveryEventAsync(string eventId, DateTime startUtc)
        {
            var values = new List<object>
    {
        eventId,
        startUtc.ToString("o"),
        "",         // EndUtc
        true        // IsActive
    };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{DeliveryEventsSheet}!A:D");

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }

        public async Task EndDeliveryEventAsync(string eventId)
        {
            var all = await GetAllDeliveryEventsAsync();
            int index = all.FindIndex(e => e.EventId == eventId);

            if (index == -1)
                return;

            int row = index + 2;

            var values = new List<object>
    {
        eventId,
        all[index].StartUtc.ToString("o"),
        DateTime.UtcNow.ToString("o"),
        false
    };

            var update = _sheetsService.Spreadsheets.Values.Update(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{DeliveryEventsSheet}!A{row}:D{row}");

            update.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

            await update.ExecuteAsync();
        }

        public async Task<List<(string EventId, DateTime StartUtc, bool IsActive)>> GetAllDeliveryEventsAsync()
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{DeliveryEventsSheet}!A2:D");

            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<(string, DateTime, bool)>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 4) continue;

                list.Add((
                    row[0].ToString(),
                    DateTime.Parse(row[1].ToString()),
                    bool.Parse(row[3].ToString())
                ));
            }

            return list;
        }

        public async Task AddDeliveryEntryAsync(DeliveryEntry entry)
        {
            var values = new List<object>
    {
        entry.EventId,
        entry.DiscordUserId,
        entry.IngameName,
        entry.SubmissionType,
        entry.Amount,
        entry.TimestampUtc.ToString("o")
    };

            var append = _sheetsService.Spreadsheets.Values.Append(
                new ValueRange { Values = new List<IList<object>> { values } },
                _spreadsheetId,
                $"{DeliveryEntriesSheet}!A:F");

            append.ValueInputOption =
                SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            await append.ExecuteAsync();
        }

        public async Task<List<DeliveryEntry>> GetDeliveryEntriesByEventAsync(string eventId)
        {
            var request = _sheetsService.Spreadsheets.Values.Get(
                _spreadsheetId,
                $"{DeliveryEntriesSheet}!A2:F");

            var response = await request.ExecuteAsync();
            var rows = response.Values;

            var list = new List<DeliveryEntry>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row.Count < 6) continue;
                if (row[0].ToString() != eventId) continue;

                list.Add(new DeliveryEntry
                {
                    EventId = row[0].ToString(),
                    DiscordUserId = row[1].ToString(),
                    IngameName = row[2].ToString(),
                    SubmissionType = row[3].ToString(),
                    Amount = int.Parse(row[4].ToString()),
                    TimestampUtc = DateTime.Parse(row[5].ToString())
                });
            }

            return list;
        }
    }
}
