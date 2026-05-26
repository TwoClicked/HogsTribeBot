using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TribeBot.Services.Services
{
    public class PaddleOcrServerService
    {
        private readonly string _serverHost;
        private readonly int _serverPort;
        private readonly HttpClient _http = new();

        public DateTime? LastDetectedDonationDateUtc { get; private set; }

        public PaddleOcrServerService(string host, int port)
        {
            _serverHost = host;
            _serverPort = port;
        }

        private string OcrUrl => $"http://{_serverHost}:{_serverPort}/ocr";

        private async Task<List<(string text, int x, int y)>> GetBlocksAsync(string imageUrl)
        {
            var payload = JsonSerializer.Serialize(new { image_url = imageUrl });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(OcrUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"OCR RAW JSON: {json[..Math.Min(json.Length, 300)]}");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return new();

            var blocks = new List<(string text, int x, int y)>();

            foreach (var block in data.EnumerateArray())
            {
                string text = block.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                int x = 0, y = 0;

                try
                {
                    if (block.TryGetProperty("box", out var box) && box.GetArrayLength() > 0)
                    {
                        var firstPoint = box[0];
                        if (firstPoint.ValueKind == JsonValueKind.Array && firstPoint.GetArrayLength() >= 2)
                        {
                            x = (int)firstPoint[0].GetDouble();
                            y = (int)firstPoint[1].GetDouble();
                        }
                    }
                }
                catch { /* ignore box parse errors, text still usable */ }

                blocks.Add((text, x, y));
            }

            return blocks;
        }

        public async Task<string> ExtractRawTextAsync(string imageUrl)
        {
            try
            {
                var blocks = await GetBlocksAsync(imageUrl);

                int bestDistance = int.MaxValue;
                string bestText = "";

                int targetY = await DetectBlueRowYFromUrlAsync(imageUrl);

                foreach (var (text, x, y) in blocks)
                {
                    if (!Regex.IsMatch(text, @"\d")) continue;
                    if (x < 400) continue;

                    int distance = targetY != -1 ? Math.Abs(y - targetY) : int.MaxValue;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestText = text;
                    }
                }

                return bestText
                    .Replace(",", "").Replace(".", "")
                    .Replace("O", "0").Replace("o", "0")
                    .Replace("l", "1").Replace("I", "1")
                    .Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR RAW ERROR: {ex}");
                return "";
            }
        }

        public async Task<int?> ExtractDonationAmountAsync(string imageUrl)
        {
            try
            {
                Console.WriteLine("📸 OCR START");

                var blocks = await GetBlocksAsync(imageUrl);

                var sb = new StringBuilder();
                foreach (var (text, _, _) in blocks)
                    sb.Append(" ").Append(text);

                string allText = sb.ToString()
                    .Replace('：', ':').Replace('／', '/').Replace(';', ':');

                allText = Regex.Replace(allText,
                    @"(\d{2})[./,](\d{2})(\d{2}:\d{2}:\d{2})", "$1.$2 $3");

                Console.WriteLine("---- FULL OCR TEXT ----");
                Console.WriteLine(allText);

                int total = 0;
                var amountMatches = Regex.Matches(allText,
                    @"(\d+[.,]\d+|\d+)\s*[Mm]", RegexOptions.IgnoreCase);

                foreach (Match match in amountMatches)
                {
                    string num = match.Groups[1].Value.Replace(",", ".");
                    if (double.TryParse(num, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out double millions))
                    {
                        if (millions >= 0.1 && millions <= 50.0)
                        {
                            int value = Convert.ToInt32(Math.Round(millions * 1_000_000));
                            total += value;
                            Console.WriteLine($"💰 Detected: {millions}M -> {value}");
                        }
                    }
                }

                return total > 0 ? total : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 OCR ERROR: {ex}");
                return null;
            }
        }

        public async Task<int?> ExtractDeliveryDonationAmountAsync(string imageUrl)
        {
            try
            {
                var blocks = await GetBlocksAsync(imageUrl);
                int total = 0;

                foreach (var (text, _, _) in blocks)
                {
                    var matches = Regex.Matches(text, @"(\d+[.,]\d+|\d+)\s*[Mm]");
                    foreach (Match match in matches)
                    {
                        string num = match.Groups[1].Value.Replace(",", ".");
                        if (double.TryParse(num, NumberStyles.Any,
                            CultureInfo.InvariantCulture, out double millions))
                        {
                            if (millions >= 0.1 && millions <= 50)
                                total += (int)Math.Round(millions * 1_000_000);
                        }
                    }
                }

                return total > 0 ? total : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 DELIVERY OCR ERROR: {ex}");
                return null;
            }
        }

        public int ExtractMaxNumber(string text)
        {
            var matches = Regex.Matches(text, @"\d{1,3}(?:,\d{3})*|\d+");
            var numbers = matches
                .Select(m => int.Parse(m.Value.Replace(",", "")))
                .Where(n => n >= 200 && n <= 100000)
                .ToList();
            return numbers.Any() ? numbers.Max() : 0;
        }

        private async Task<int> DetectBlueRowYFromUrlAsync(string imageUrl)
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(imageUrl);
                using var image = Image.Load<Rgba32>(bytes);

                int bestY = -1;
                int bestScore = 0;

                for (int y = 0; y < image.Height; y++)
                {
                    int bluePixels = 0;
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = image[x, y];
                        if (pixel.B > 140 && pixel.R < 120 && pixel.G < 140)
                            bluePixels++;
                    }
                    if (bluePixels > bestScore) { bestScore = bluePixels; bestY = y; }
                }

                return bestY;
            }
            catch { return -1; }
        }
    }
}