using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TribeBot.Services.Services
{
    public class PaddleOcrServerService
    {
        private readonly string _exePath;
        private readonly string _serverHost = "127.0.0.1";
        private readonly int _serverPort = 23333;
        private Process? _server;

        public DateTime? LastDetectedDonationDateUtc { get; private set; }

        public PaddleOcrServerService(string exePath)
        {
            _exePath = exePath;
            StartOcrServer();
        }

        // -------------------------------------------------------
        // START SERVER
        // -------------------------------------------------------
        private void StartOcrServer()
        {
            try
            {
                string workDir = Path.GetDirectoryName(_exePath)!;

                var psi = new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = "-addr=loopback -port=23333",
                    WorkingDirectory = workDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                _server = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine("💥 ERROR STARTING OCR SERVER");
                Console.WriteLine(ex);
            }
        }

        // -------------------------------------------------------
        // OCR REQUEST
        // -------------------------------------------------------
        public async Task<int?> ExtractDonationAmountAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return null;

                string requestJson =
                    $"{{\"image_path\":\"{imagePath.Replace("\\", "\\\\")}\"}}";

                using var client = new TcpClient();
                client.Connect(_serverHost, _serverPort);

                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                await writer.WriteLineAsync(requestJson);
                await writer.FlushAsync();

                string? response = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(response))
                    return null;

                JsonDocument doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("data", out var dataArray))
                    return null;

                int total = 0;

                // ✅ Declare OUTSIDE loop
                DateTime? detectedDateUtc = null;

                foreach (var block in dataArray.EnumerateArray())
                {
                    if (!block.TryGetProperty("text", out var textProp))
                        continue;

                    string text = textProp.GetString() ?? "";

                    string norm = text
                        .Replace('М', 'M')
                        .Replace('м', 'm')
                        .Replace('О', '0')
                        .Replace('о', 'o')
                        .Replace('б', '6')
                        .Replace('‚', ',')
                        .Replace('¸', ',')
                        .Replace('˛', ',')
                        .Replace('٫', '.')
                        .Replace('﹐', ',')
                        .Replace('，', ',')
                        .Replace('､', ',')
                        .Replace('·', '.')
                        .Replace('/', '.')
                        .Replace("\u200E", "")
                        .Replace("\u200F", "")
                        .Replace("\u202A", "")
                        .Replace("\u202B", "")
                        .Replace("\u202C", "");

                    // -------------------------------------------------------
                    // DATE DETECTION (only once)
                    // -------------------------------------------------------


                    if (detectedDateUtc == null)
                    {
                        // yyyy.MM.dd
                        var fullDateMatch = Regex.Match(
                            norm,
                            @"(\d{4})[.](\d{1,2})[.](\d{1,2})");

                        if (fullDateMatch.Success)
                        {
                            int year = int.Parse(fullDateMatch.Groups[1].Value);
                            int month = int.Parse(fullDateMatch.Groups[2].Value);
                            int day = int.Parse(fullDateMatch.Groups[3].Value);

                            if (DateTime.TryParse($"{year}-{month}-{day}",
                                out DateTime parsed))
                            {
                                detectedDateUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                            }
                        }
                        else
                        {
                            // MM.dd
                            var shortDateMatch = Regex.Match(
                                norm,
                                @"(\d{1,2})[.](\d{1,2})");

                            if (shortDateMatch.Success)
                            {
                                int month = int.Parse(shortDateMatch.Groups[1].Value);
                                int day = int.Parse(shortDateMatch.Groups[2].Value);

                                int year = DateTime.UtcNow.Year;

                                if (DateTime.TryParse($"{year}-{month}-{day}",
                                    out DateTime parsed))
                                {
                                    detectedDateUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                                }
                            }
                        }
                    }



                    // -------------------------------------------------------
                    // FIND DONATION VALUES
                    // -------------------------------------------------------
                    var matches = Regex.Matches(
                norm,
                @"(\d+[.,]\d+|\d+)\s*[Mm]",
                RegexOptions.IgnoreCase);

                    if (matches.Count == 0)
                        continue;

                    foreach (Match match in matches)
                    {
                        string num = match.Groups[1].Value.Replace(",", ".");

                        if (double.TryParse(
                            num,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double millions))
                        {
                            if (millions >= 0.1 && millions <= 15.0)
                            {
                                int amount = (int)(millions * 1_000_000);
                                total += amount;
                            }
                        }
                    }
                }



                // ✅ Store detected date for external validation
                LastDetectedDonationDateUtc = detectedDateUtc;


                Console.WriteLine("---- OCR RESULT ----");
                Console.WriteLine($"Date: {detectedDateUtc}");
                Console.WriteLine($"Total: {total}");
                Console.WriteLine("--------------------");

                return total > 0 ? total : null;
            }
            catch
            {
                return null;
            }


        }
    }
}
