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
                Console.WriteLine("==============================================");
                Console.WriteLine("🚀 STARTING PADDLE OCR SERVER");
                Console.WriteLine($"📁 exe:      {_exePath}");
                Console.WriteLine($"📁 work dir: {workDir}");
                Console.WriteLine($"📁 exists:   {File.Exists(_exePath)}");
                Console.WriteLine("==============================================");

                var psi = new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = "-addr=loopback -port=23333",
                    WorkingDirectory = workDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                _server = Process.Start(psi);

                Console.WriteLine("🚀 PaddleOCR-json server started");
                Console.WriteLine("🔌 Listening at 127.0.0.1:23333");
                Console.WriteLine("==============================================");
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
                Console.WriteLine("==============================================");
                Console.WriteLine("🚀 OCR REQUEST");
                Console.WriteLine($"🖼 Image: {imagePath}");
                Console.WriteLine($"📁 Exists: {File.Exists(imagePath)}");
                Console.WriteLine("==============================================");

                if (!File.Exists(imagePath))
                {
                    Console.WriteLine("❌ ERROR: File does not exist.");
                    return null;
                }

                // JSON request for OCR
                string requestJson =
                    $"{{\"image_path\":\"{imagePath.Replace("\\", "\\\\")}\"}}";

                // -------------------------------------------------------
                // SEND JSON TO OCR SERVER
                // -------------------------------------------------------
                using var client = new TcpClient();
                client.Connect(_serverHost, _serverPort);

                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                Console.WriteLine("📨 Sending to OCR server:");
                Console.WriteLine(requestJson);

                await writer.WriteLineAsync(requestJson);
                await writer.FlushAsync();

                string? response = await reader.ReadLineAsync();

                Console.WriteLine("📥 RAW OCR RESPONSE:");
                Console.WriteLine(response);

                if (string.IsNullOrWhiteSpace(response))
                {
                    Console.WriteLine("❌ EMPTY OCR RESPONSE.");
                    return null;
                }

                // -------------------------------------------------------
                // PARSE JSON
                // -------------------------------------------------------
                JsonDocument doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("data", out var dataArray))
                {
                    Console.WriteLine("❌ NO 'data' array found");
                    return null;
                }

                Console.WriteLine("==============================================");
                Console.WriteLine("🔍 ANALYZING OCR TEXT BLOCKS");
                Console.WriteLine("==============================================");

                int total = 0;

                foreach (var block in dataArray.EnumerateArray())
                {
                    if (!block.TryGetProperty("text", out var textProp))
                        continue;

                    string text = textProp.GetString() ?? "";

                    // -------------------------------------------------------
                    // NORMALIZE OCR NOISE
                    // -------------------------------------------------------
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

                    Console.WriteLine($"📝 RAW:  '{text}'");
                    Console.WriteLine($"🔧 NORM: '{norm}'");

                    // -------------------------------------------------------
                    // DEBUG CHARACTER CODES
                    // -------------------------------------------------------
                    Console.Write("🔎 CHAR CODES: ");
                    foreach (char c in norm)
                        Console.Write($"{c}({(int)c}) ");
                    Console.WriteLine();

                    // -------------------------------------------------------
                    // FIND DONATION VALUES
                    // -------------------------------------------------------
                    var matches = Regex.Matches(
                        norm,
                        @"(\d+[.,]\d+|\d+)\s*[Mm]",
                        RegexOptions.IgnoreCase);

                    if (matches.Count == 0)
                    {
                        Console.WriteLine("❌ NO MATCHES");
                        continue;
                    }

                    foreach (Match match in matches)
                    {
                        string num = match.Groups[1].Value.Replace(",", ".");
                        Console.WriteLine($"✔ MATCHED: '{num}'");

                        if (double.TryParse(
                            num,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double millions))
                        {
                            if (millions >= 0.1 && millions <= 10.0)
                            {
                                int amount = (int)(millions * 1_000_000);
                                Console.WriteLine($"➕ ADD {amount:N0}");
                                total += amount;
                            }
                            else
                            {
                                Console.WriteLine($"⚠ IGNORE (out of VR range): {millions}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"❌ CANNOT PARSE '{num}'");
                        }
                    }
                }

                Console.WriteLine("==============================================");
                Console.WriteLine($"🎯 FINAL DONATION TOTAL: {total:N0}");
                Console.WriteLine("==============================================");

                return total > 0 ? total : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("💥 OCR ERROR:");
                Console.WriteLine(ex);
                return null;
            }
        }
    }
}
