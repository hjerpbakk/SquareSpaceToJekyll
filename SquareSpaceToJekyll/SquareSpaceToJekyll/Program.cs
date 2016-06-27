using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using SquareSpaceToJekyll.Model;

namespace SquareSpaceToJekyll
{
    class MainClass
    {
        public static void Main (string [] args)
        {
            var xml = XElement.Load("/Users/sankra/Projects/SquareSpaceToJekyll/export.xml");
            var pathToJekyllSite = "/Users/sankra/Projects/sankra.github.io/test";

            var children = xml.Elements().Single().Elements().ToArray();

            // Site meatadata
            var siteMetaData = new SiteMetaData();
            siteMetaData.Title = children.First(e => e.Name == "title").Value;
            siteMetaData.Description = children.First(e => e.Name == "description").Value;
            using (TextWriter writer = File.CreateText(Path.Combine(pathToJekyllSite, "siteMetaData.yaml"))) {
                var a = new YamlDotNet.Serialization.Serializer();
                a.Serialize(writer, siteMetaData);
            }

            // Blog posts
            var io = new IO(pathToJekyllSite);
            var blogPosts = children.Where(i => i.Name == "item" && (string)i.Element(Namespaces.wpNS + "post_type") == "post");
            foreach (var xmlBlogPost in blogPosts) {
                BlogPost blogPost;
                if (xmlBlogPost.Element(Namespaces.wpNS + "postmeta")?.Element(Namespaces.wpNS + "meta_key")?.Value == "passthrough_url") {
                    blogPost = new LinkPost(xmlBlogPost, io) {
                        ExternalLink = xmlBlogPost.Element(Namespaces.wpNS + "postmeta").Element(Namespaces.wpNS + "meta_value").Value
                    };
                } else {
                    blogPost = new BlogPost(xmlBlogPost, io);
                }

                blogPost.Title = xmlBlogPost.Element("title").Value;
                blogPost.Link = xmlBlogPost.Element("link").Value;

                foreach (var tag in xmlBlogPost.Elements("category").Where(e => e.Attribute("domain")?.Value == "post_tag").Select(e => e.Value)) {
                    blogPost.AddTag(tag);
                }

                blogPost.Content = xmlBlogPost.Element(Namespaces.contentNS + "encoded").Value;

                blogPost.Save();
            }

            // TODO: Other pages
        }

        public class LinkPost : BlogPost {
            public LinkPost(XElement blogPostXML, IO io) : base(blogPostXML, io) {

            }

            public string ExternalLink { get; set; }

            public override string Content {
                set {
                    content.AppendLine();
                    content.Append("link: ");
                    content.Append(ExternalLink);
                    base.Content = value;
                }
            }
        }

        public class BlogPost {
            readonly IO io;

            readonly string layout;
            readonly List<string> tags;

            protected readonly StringBuilder content;

            Image[] images;

            public BlogPost(XElement blogPostXML, IO io) {
                this.io = io;
                IsDraft = blogPostXML.Element(Namespaces.wpNS + "status").Value == "draft";

                layout = "post";
                tags = new List<string>();

                content = new StringBuilder("---");
                images = new Image[0];
            }

            public bool IsDraft { get; }

            public string Title { get; set; }

            public string WebSafeTitle { get; private set; }

            public string Link {
                set {
                    var dateParts = value.Split('/');
                    NameWithDate = $"{dateParts[2]}-{dateParts[3]}-{dateParts[4]}-{dateParts[5]}";
                    WebSafeTitle = dateParts[5];
                }
            }

            public string NameWithDate { get; private set; }

            public virtual string Content {
                get {
                    return content.ToString();
                }

                set {
                    Console.WriteLine($"Parsing {Title}");
                    content.AppendLine();
                    content.Append("layout: ");
                    content.AppendLine(layout);
                    content.Append("title: \"");
                    content.Append(Title.Replace("\"", "&quot;"));
                    content.AppendLine("\"");
                    if (tags.Count > 0) {
                        content.Append("tags: ");
                        var tagsString = string.Join(", ", tags);
                        content.AppendLine($"[{tagsString}]");
                    }

                    content.AppendLine("---");
                    var normalizedContent = Regex.Replace(value, @"\[caption id="".*"" align="".*"" width="".*""\]", "");
                    normalizedContent = normalizedContent.Replace("[/caption]", "");

                    var html = new HtmlAgilityPack.HtmlDocument();
                    html.LoadHtml(normalizedContent);

                    var imageTags = html.DocumentNode.SelectNodes("//img");
                    if (imageTags != null) {
                        images = new Image[imageTags.Count];
                        Parallel.For(0, imageTags.Count, i => {
                            var src = imageTags[i].Attributes["src"];
                            var imageUrl = src.Value;
                            if (!imageUrl.StartsWith("http", StringComparison.InvariantCulture)) {
                                var normalizedImageUrl = imageUrl.StartsWith("/", StringComparison.InvariantCulture) ? imageUrl.Remove(0, 1) : imageUrl;
                                imageUrl = UserSettings.InternalLinkPrefix + normalizedImageUrl;
                            }
                            
                            Console.WriteLine($"Downloading {imageUrl}...");

                            using (var httpClient = new HttpClient()) {
                                httpClient.MaxResponseContentBufferSize = 2000000L;
                                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");

                                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_11_5) AppleWebKit/601.6.17 (KHTML, like Gecko) Version/9.1.1 Safari/601.6.17");

                                var request = new HttpRequestMessage() {
                                    RequestUri = new Uri(imageUrl),
                                    Method = HttpMethod.Get,
                                };

                                var getTask = httpClient.SendAsync(request);
                                getTask.Wait();
                                var response = getTask.Result;
                                response.EnsureSuccessStatusCode();
                                var jsonTask = response.Content.ReadAsByteArrayAsync();
                                jsonTask.Wait();
                                images[i] = new Image($"/{UserSettings.ImageFolder}/{WebSafeTitle}/{i + Path.GetFileName(imageUrl)}", jsonTask.Result);
                            }

                            src.Value = images[i].Source;
                        });
                    }

                    var sb = new StringBuilder();
                    var sw = new StringWriter(sb);
                    html.Save(sw);
                    normalizedContent = sb.ToString();
                    content.AppendLine(normalizedContent);
                }
            }

            public Image[] Images { get { return images; } }

            public void AddTag(string tag) {
                tags.Add(tag);
            }

            public void Save() {
                io.Save(this);
            }
        }

        public class IO {
            readonly string postsPath;
            readonly string draftsPath;

            public IO(string pathToJekyllSite) {
                postsPath = Directory.CreateDirectory(Path.Combine(pathToJekyllSite, "_posts")).FullName;
                draftsPath = Directory.CreateDirectory(Path.Combine(pathToJekyllSite, "_drafts")).FullName;
                ImagesPath = Directory.CreateDirectory(Path.Combine(pathToJekyllSite, UserSettings.ImageFolder)).FullName;
            }

            public string ImagesPath { get; }

            public void Save(BlogPost blogPost) {
                SaveBlogPost(blogPost);
                SaveImages(blogPost);
            }

            void SaveBlogPost(BlogPost blogPost) {
                var savePath = blogPost.IsDraft ? draftsPath : postsPath;
                File.WriteAllText(Path.Combine(savePath, blogPost.NameWithDate) + ".html", blogPost.Content);
            }

            void SaveImages(BlogPost blogPost) {
                foreach (var image in blogPost.Images) {
                    var imageDirForPost = Path.Combine(ImagesPath, blogPost.WebSafeTitle);
                    Directory.CreateDirectory(imageDirForPost);
                    File.WriteAllBytes(Path.Combine(imageDirForPost, image.Name), image.Content);
                }
            }
        }

        public struct Image {
            public Image(string source, byte[] content) {
                Source = source;
                Content = content;
            }

            public string Source { get; }
            public string Name => Path.GetFileName(Source);
            public byte[] Content { get; }
        }

        public static class UserSettings {
            public const string ImageFolder = "img";
            public const string InternalLinkPrefix = "https://runar-ovesenhjerpbakk.squarespace.com/";
        }

        public static class Namespaces {
            public static readonly XNamespace wpNS = "http://wordpress.org/export/1.2/";
            public static readonly XNamespace contentNS = "http://purl.org/rss/1.0/modules/content/";
        }
    }
}
