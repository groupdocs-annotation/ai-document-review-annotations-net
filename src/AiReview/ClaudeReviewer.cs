using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiReview
{
    /// <summary>
    /// Optional: asks a model for the findings instead of reading them from a file.
    ///
    /// The library does not care where the findings come from - this class exists only to show
    /// the shape of the round trip. Everything it needs is the page text that DocumentText
    /// already extracted, and everything it returns is a list of {quote, comment, severity}.
    ///
    /// Set ANTHROPIC_API_KEY to use it. Nothing else in this sample needs network access.
    /// </summary>
    public static class ClaudeReviewer
    {
        public const string DefaultModel = "claude-sonnet-5-5";

        public const string FallbackPrompt =
            "You are reviewing a document for a professional reader.\n" +
            "Return ONLY a JSON array. Each element must be an object with exactly these keys:\n" +
            "  quote    - text copied VERBATIM from the document, 4 to 15 words, unique in the document\n" +
            "  comment  - one sentence saying what is wrong or worth asking about\n" +
            "  severity - \"high\", \"medium\" or \"low\"\n" +
            "Quote the document exactly: the quote is used to find the text on the page.\n" +
            "Return at most 10 elements. No prose, no markdown fences.";

        public static async Task<List<Finding>> ReviewAsync(
            string documentText,
            string model,
            string? promptPath,
            CancellationToken cancellationToken)
        {
            string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "ANTHROPIC_API_KEY is not set. Use the 'review' command with a findings file instead.");
            }

            string systemPrompt = FallbackPrompt;
            if (!string.IsNullOrWhiteSpace(promptPath) && File.Exists(promptPath))
            {
                systemPrompt = File.ReadAllText(promptPath);
            }

            object payload = new
            {
                model = model,
                max_tokens = 2048,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = documentText }
                }
            };

            using HttpClient http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(2);

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"The model call failed with {(int)response.StatusCode}: {body}");
            }

            return ParseFindings(ExtractText(body));
        }

        private static string ExtractText(string responseBody)
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            StringBuilder text = new StringBuilder();

            if (document.RootElement.TryGetProperty("content", out JsonElement content))
            {
                foreach (JsonElement block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out JsonElement value))
                    {
                        text.Append(value.GetString());
                    }
                }
            }

            return text.ToString();
        }

        /// <summary>Pulls the JSON array out of the answer, tolerating markdown fences and stray prose.</summary>
        public static List<Finding> ParseFindings(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
            {
                return new List<Finding>();
            }

            int start = answer.IndexOf('[');
            int end = answer.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException("The model did not return a JSON array:\n" + answer);
            }

            string json = answer.Substring(start, end - start + 1);
            List<Finding>? findings = JsonSerializer.Deserialize<List<Finding>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return findings ?? new List<Finding>();
        }
    }
}
