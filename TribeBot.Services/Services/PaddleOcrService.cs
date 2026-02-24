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
                Console.WriteLine("✅ OCR server started.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("💥 ERROR STARTING OCR SERVER");
                Console.WriteLine(ex);
            }
        }

        public async Task<int?> ExtractDonationAmountAsync(string imagePath)
        {
            try
            {
                Console.WriteLine("=================================================");
                Console.WriteLine("📸 OCR START");
                Console.WriteLine($"Image: {imagePath}");
                Console.WriteLine("=================================================");

                if (!File.Exists(imagePath))
                {
                    Console.WriteLine("❌ Image file does not exist.");
                    return null;
                }

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
                {
                    Console.WriteLine("❌ OCR returned empty response.");
                    return null;
                }

                JsonDocument doc = JsonDocument.Parse(response);

                if (!doc.RootElement.TryGetProperty("data", out var dataArray))
                {
                    Console.WriteLine("❌ OCR JSON missing 'data' property.");
                    return null;
                }

                // ==============================
                // COMBINE OCR TEXT
                // ==============================

                var sb = new StringBuilder();

                foreach (var block in dataArray.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var textProp))
                    {
                        sb.Append(" ").Append(textProp.GetString());
                    }
                }

                string allText = sb.ToString()
                    .Replace('：', ':')
                    .Replace('／', '/')
                    .Replace(';', ':');

                Console.WriteLine("---- FULL OCR TEXT ----");
                Console.WriteLine(allText);
                Console.WriteLine("------------------------");

                // ==============================
                // DATE DETECTION (TRANSPORT ROWS ONLY)
                // ==============================

                DateTime? detectedDateUtc = null;

                var transportMatches = Regex.Matches(
                    allText,
                    @"Tax\s*Rate:\s*\d+[.,]?\d*\s*%\s*(\d{2})/(\d{2})\s*(\d{2}):(\d{2}):(\d{2})",
                    RegexOptions.IgnoreCase);

                Console.WriteLine($"🔎 Found {transportMatches.Count} transport date matches.");

                for (int i = 0; i < transportMatches.Count; i++)
                {
                    Console.WriteLine($"Match {i}: {transportMatches[i].Value}");
                }

                if (transportMatches.Count > 0)
                {
                    var match = transportMatches[0]; // top-most row

                    int month = int.Parse(match.Groups[1].Value);
                    int day = int.Parse(match.Groups[2].Value);
                    int hour = int.Parse(match.Groups[3].Value);
                    int minute = int.Parse(match.Groups[4].Value);
                    int second = int.Parse(match.Groups[5].Value);

                    Console.WriteLine("📅 Using first transport row date:");
                    Console.WriteLine($"Month={month} Day={day} Time={hour}:{minute}:{second}");

                    TryBuildDate(
                        DateTime.UtcNow.Year,
                        month,
                        day,
                        hour,
                        minute,
                        second,
                        ref detectedDateUtc);
                }
                else
                {
                    Console.WriteLine("⚠ No transport row dates detected.");
                }

                LastDetectedDonationDateUtc = detectedDateUtc;

                Console.WriteLine($"🕒 Final Parsed Date (UTC): {detectedDateUtc}");

                // ==============================
                // DONATION AMOUNT PARSING
                // ==============================

                int total = 0;

                foreach (var block in dataArray.EnumerateArray())
                {
                    if (!block.TryGetProperty("text", out var textProp))
                        continue;

                    string text = textProp.GetString() ?? "";

                    var amountMatches = Regex.Matches(
                        text,
                        @"(\d+[.,]\d+|\d+)\s*[Mm]",
                        RegexOptions.IgnoreCase);

                    foreach (Match match in amountMatches)
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
                                int value = (int)(millions * 1_000_000);
                                total += value;

                                Console.WriteLine($"💰 Detected donation: {millions}M -> {value}");
                            }
                        }
                    }
                }

                Console.WriteLine("---- FINAL OCR RESULT ----");
                Console.WriteLine($"Date UTC: {detectedDateUtc}");
                Console.WriteLine($"Total: {total}");
                Console.WriteLine("---------------------------");

                return total > 0 ? total : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("💥 OCR ERROR:");
                Console.WriteLine(ex);
                return null;
            }
        }

        private void TryBuildDate(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            ref DateTime? detectedDateUtc)
        {
            try
            {
                var parsed = new DateTime(
                    year,
                    month,
                    day,
                    hour,
                    minute,
                    second,
                    DateTimeKind.Utc);

                Console.WriteLine($"🧪 Built DateTime: {parsed}");

                if (parsed <= DateTime.UtcNow.AddMinutes(5))
                {
                    detectedDateUtc = parsed;
                    Console.WriteLine("✅ Date accepted.");
                }
                else
                {
                    Console.WriteLine("⚠ Date rejected (future > 5 min).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Date build failed:");
                Console.WriteLine(ex.Message);
            }
        }
    }
}