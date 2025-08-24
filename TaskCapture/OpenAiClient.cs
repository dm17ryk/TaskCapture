using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TaskCapture
{
    public static class OpenAiClient
    {
        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// Отправляет список изображений (в порядке: L/R/L/R/... c фильтрацией на уровне вызывающего кода)
        /// и получает ПОЛНЫЙ HTML (с обёрткой и подсветкой) для показа в WebView2.
        /// </summary>
        public static async Task<string> SolveFromImagesAsync(
            List<string> imagePaths,
            string modelId,
            double? temperature,
            Action<string>? log = null)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Не задан OPENAI_API_KEY.");

            var sys = new
            {
                role = "system",
                content = "You are a precise assistant that solves coding tasks. Return output as clean HTML (<article>...</article>) with readable layout. Use <pre><code class=\"language-...\"> for code."
            };

            var userParts = new List<object>();
            userParts.Add(new
            {
                type = "text",
                text =
                    "You will receive screenshots from a coding platform (left: instructions, right: code template). " +
                    "Images are in reading order. The right image may repeat if it didn't change. " +
                    "Produce the final answer. Return pure HTML inside <article>...</article> only. " +
                    "If there is a code file to fill, output the complete file in one <pre><code class=\"language-...\"> block. Do not include Markdown."
            });

            foreach (var path in imagePaths)
            {
                var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(path));
                userParts.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:image/png;base64,{b64}" }
                });
            }

            var user = new { role = "user", content = userParts };

            // Готовим payload
            var payload = new Dictionary<string, object?>
            {
                ["model"] = modelId,
                ["messages"] = new object[] { sys, user }
            };
            if (temperature.HasValue) payload["temperature"] = temperature;

            var org = Environment.GetEnvironmentVariable("OPENAI_ORG");
            var project = Environment.GetEnvironmentVariable("OPENAI_PROJECT");

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (!string.IsNullOrWhiteSpace(org)) req.Headers.Add("OpenAI-Organization", org);
            if (!string.IsNullOrWhiteSpace(project)) req.Headers.Add("OpenAI-Project", project);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var err = doc.RootElement.GetProperty("error");
                    var code = err.TryGetProperty("code", out var ce) ? ce.GetString() : null;
                    var msg = err.TryGetProperty("message", out var me) ? me.GetString() : body;

                    if (code == "insufficient_quota")
                        throw new InvalidOperationException(
                            "Недостаточно квоты в OpenAI (insufficient_quota). Проверь оплату/кредиты и организацию/проект.");

                    if ((int)res.StatusCode == 429)
                        throw new InvalidOperationException("Лимит запросов (429) или квота. Попробуй позже/пополни кредиты.");

                    throw new InvalidOperationException($"OpenAI API error {res.StatusCode}: {msg}");
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"OpenAI API error {res.StatusCode}: {body}");
                }
            }

            using var docOk = JsonDocument.Parse(body);
            var content = docOk.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            var fullHtml = WrapHtml(content);
            return fullHtml;
        }

        public static string WrapHtml(string innerHtml)
        {
            return $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>TaskCapture Result</title>
  <link rel=""stylesheet"" href=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github-dark.min.css"">
  <style>
    body {{ font-family: Segoe UI, sans-serif; margin: 0; background:#0b0f13; color:#eaeef2; }}
    .wrap {{ max-width: 1100px; margin: 24px auto; padding: 0 16px; }}
    article {{ background:#11161c; border:1px solid #253040; border-radius:12px; padding:18px; }}
    pre {{ overflow:auto; padding:12px; border-radius:12px; background:#0c1116; }}
    code {{ font-family: Consolas, monospace; font-size: 13px; }}
    h1,h2,h3 {{ color:#c8e1ff; }}
    a {{ color:#7cc0ff; }}
  </style>
</head>
<body>
  <div class=""wrap"">{innerHtml}</div>
  <script src=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js""></script>
  <script>hljs.highlightAll();</script>
</body>
</html>";
        }
    }
}
