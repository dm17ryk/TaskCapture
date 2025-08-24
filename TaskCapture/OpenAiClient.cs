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
        /// Отправляет список изображений (в порядке: left_0000, right_0000, left_0001, right_0001, …)
        /// и получает HTML-ответ с решением. Ключ читается из переменной окружения OPENAI_API_KEY.
        /// </summary>
        public static async Task<string> SolveFromImagesAsync(List<string> imagePaths, Action<string>? log = null)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Не задан OPENAI_API_KEY в переменных окружения.");

            // Сформируем messages для Chat Completions Vision (gpt-4o-mini / gpt-4o)
            // Отдадим инструкции: вернуть чистый HTML с подсветкой через highlight.js.
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
                "Read them in order and produce the final answer. " +
                "Return pure HTML inside <article>...</article> only. " +
                "If there is a code file to fill, output the complete file in a single fenced block (<pre><code class=\"language-...\">). " +
                "Do not include Markdown."
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

            var payload = new
            {
                model = "gpt-5-nano", // поддерживает vision
                messages = new object[] { sys, user },
                //temperature = 0.2
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI error {res.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var html = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            // Вставим обёртку с highlight.js, чтобы подсветка кодовых блоков работала
            var fullHtml = HtmlWrapper(html);
            return fullHtml;
        }

        private static string HtmlWrapper(string innerHtml)
        {
            // Автоподсветка через highlight.js с CDN
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
