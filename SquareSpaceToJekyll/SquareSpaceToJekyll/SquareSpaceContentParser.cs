using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace SquareSpaceToJekyll {
    public class SquarespaceContentParser {

        static readonly ConcurrentDictionary<string, string> imagesEncountered;

        static SquarespaceContentParser() {
            imagesEncountered = new ConcurrentDictionary<string, string>();
        }

        public SquarespaceContent Parse(string webSafeTitle, string content) {
            var normalizedContent = Regex.Replace(content, @"\[caption id="".*"" align="".*"" width="".*""\]", "");
            normalizedContent = normalizedContent.Replace("[/caption]", "");

            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(normalizedContent);

            var attributesToRemove = new ConcurrentBag<HtmlAttribute>();
            var images = new ConcurrentBag<HtmlNode>();

            var allTags = htmlDocument.DocumentNode.Descendants();
            Parallel.ForEach(allTags, node => {
                var attributeToRemove = node.Attributes["data-preserve-html-node"];
                if (attributeToRemove != null) {
                    attributesToRemove.Add(attributeToRemove);
                }

                if (node.Name == "img") {
                    images.Add(node);
                }
            });

            if (UserSettings.DownloadImages && images.Count != 0) {
                var imageTags = images.ToArray();
                Parallel.For(0, imageTags.Length, i => {
                    if (UserSettings.RemoveImageWidhtAndHeight) {
                        var width = imageTags[i].Attributes["width"];
                        if (width != null) {
                            attributesToRemove.Add(width);
                        }

                        var height = imageTags[i].Attributes["height"];
                        if (height != null) {
                            attributesToRemove.Add(height);
                        }
                    }

                    var src = imageTags[i].Attributes["src"];
                    var imageUrl = src.Value;
                    if (!imageUrl.StartsWith("http", StringComparison.InvariantCulture)) {
                        var normalizedImageUrl = imageUrl.StartsWith("/", StringComparison.InvariantCulture) ? imageUrl.Remove(0, 1) : imageUrl;
                        imageUrl = UserSettings.InternalLinkPrefix + normalizedImageUrl;
                    }

                    if (imagesEncountered.ContainsKey(imageUrl)) {
                        src.Value = imagesEncountered[imageUrl];
                        Console.WriteLine($"Reusing {imageUrl}");
                        return;
                    }

                    Console.WriteLine($"Downloading {imageUrl}...");
                    var imageName = i + Path.GetFileName(imageUrl).Replace("%", "");
                    var queryIndex = imageName.LastIndexOf('?');
                    if (queryIndex != -1) {
                        imageName = imageName.Remove(queryIndex, imageName.Length - queryIndex);
                    }

                    queryIndex = imageName.LastIndexOf(".png", StringComparison.InvariantCulture);
                    if (queryIndex != -1) {
                        imageName = imageName.Remove(queryIndex + 4, imageName.Length - queryIndex - 4);
                    }

                    imageName = imageName.Replace("+", "%20");

                    var imageFolderNameForPost = Path.GetFileNameWithoutExtension(webSafeTitle);
                    var imageSource = $"/{UserSettings.ImageFolder}/{imageFolderNameForPost}/{(imageName)}";
                    imagesEncountered.TryAdd(imageUrl, imageSource);
                    var image = new Image();
                    image.Download(imageUrl, imageSource);
                    src.Value = imageSource;
                });
            }

            foreach (var attribute in attributesToRemove) {
                attribute.Remove();
            }

            using (var writer = new StringWriter()) {
                htmlDocument.Save(writer);
                var squarespaceContent = new SquarespaceContent(writer.ToString());
                return squarespaceContent;
            }
        }
    }
}

