using System;
using System.IO;
using System.Net;
using System.Net.Http;

namespace SquareSpaceToJekyll {
    public struct Image {
        public void Download(string imageUrl, string imageSource) {
            // TODO: Do directory creation one time in one place
            var imageDirForPost = UserSettings.PathToJekyllSite + Path.GetDirectoryName(imageSource);
            Directory.CreateDirectory(imageDirForPost);
            var imageName = WebUtility.UrlDecode(Path.GetFileName(imageSource));
            var imagePath = Path.Combine(imageDirForPost, imageName);
            if (!UserSettings.OverwriteExistingImages && File.Exists(imagePath)) {
                Console.WriteLine($"{imageName} already exists, skipping download...");
                return;
            }

            var handler = new HttpClientHandler() {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using (var httpClient = new HttpClient(handler)) {
                httpClient.MaxResponseContentBufferSize = 2000000L;
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_11_5) AppleWebKit/601.6.17 (KHTML, like Gecko) Version/9.1.1 Safari/601.6.17");

                var request = new HttpRequestMessage {
                    RequestUri = new Uri(imageUrl),
                    Method = HttpMethod.Get,
                };

                var getTask = httpClient.SendAsync(request);
                getTask.Wait();
                var response = getTask.Result;
                response.EnsureSuccessStatusCode();
                var jsonTask = response.Content.ReadAsByteArrayAsync();
                jsonTask.Wait();
                if (jsonTask.IsFaulted) {
                    throw jsonTask.Exception;
                }

                File.WriteAllBytes(imagePath, jsonTask.Result);
            }
        }
    }
}

