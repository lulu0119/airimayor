using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CitiesSkylines2Agent.Host
{
    /// <summary>
    /// Fetches the model list from an OpenAI-compatible <c>/models</c>
    /// endpoint. Used by the in-game settings tab; failures are reported
    /// as text so the UI can show them without breaking the chat loop.
    /// </summary>
    internal static class ModelCatalog
    {
        internal sealed class FetchResult
        {
            internal List<string> Models = new List<string>();
            internal string Error = "";
        }

        internal static async Task<FetchResult> FetchAsync(string endpoint, string apiKey)
        {
            var result = new FetchResult();
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                result.Error = "Endpoint is empty.";
                return result;
            }
            try
            {
                string url = endpoint.Trim().TrimEnd('/') + "/models";
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                    }
                    string body = await client.GetStringAsync(url).ConfigureAwait(false);
                    result.Models = JObject.Parse(body)["data"]
                        ?.Select(entry => (string)entry["id"])
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct()
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToList() ?? new List<string>();
                    if (result.Models.Count == 0)
                    {
                        result.Error = "No models returned.";
                    }
                }
            }
            catch (HttpRequestException e)
            {
                result.Error = "Request failed: " + e.Message;
            }
            catch (Exception e)
            {
                result.Error = "Fetch failed: " + e.Message;
            }
            return result;
        }
    }
}
