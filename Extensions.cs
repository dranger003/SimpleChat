using System.Net.Http.Headers;

namespace SimpleChat
{
    internal static class Extensions
    {
        public static async Task<HttpResponseMessage> PostAsStreamAsync(this HttpClient client, string requestUri, HttpContent content, CancellationToken cancellationToken = default)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUri))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = content;

                return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
        }
    }
}
