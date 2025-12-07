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
        private const string FinesSheet = "Fines";
        private const string PollSheet = "Polls";
        private const string PollVotesSheet = "PollVotes";

        //Multiple use Gid's 
        private const int PollsSheetId = 1167930524;
        private const int PollVotesSheetId = 994564864;

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
                    ReignPoints = ulong.TryParse(row[6]?.ToString(), out var rp) ? rp : 0,
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

            if(deleteRequests.Count == 0)
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
    }
}
