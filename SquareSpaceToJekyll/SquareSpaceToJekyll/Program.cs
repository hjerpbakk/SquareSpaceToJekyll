using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            var postsPath = Directory.CreateDirectory(Path.Combine(pathToJekyllSite, "_posts")).FullName;


            XNamespace wpNS = "http://wordpress.org/export/1.2/";
            XNamespace contentNS = "http://purl.org/rss/1.0/modules/content/";
            var blogPosts = children.Where(i => i.Name == "item" && (string)i.Element(wpNS + "post_type") == "post");
            foreach (var xmlBlogPost in blogPosts) {
                BlogPost blogPost;
                if (xmlBlogPost.Element(wpNS + "postmeta")?.Element(wpNS + "meta_key")?.Value == "passthrough_url") {
                    blogPost = new LinkPost {
                        ExternalLink = xmlBlogPost.Element(wpNS + "postmeta").Element(wpNS + "meta_value").Value
                    };
                } else {
                    blogPost = new BlogPost();
                }

                blogPost.Title = xmlBlogPost.Element("title").Value;
                blogPost.Link = xmlBlogPost.Element("link").Value;

                foreach (var tag in xmlBlogPost.Elements("category").Where(e => e.Attribute("domain")?.Value == "post_tag").Select(e => e.Value)) {
                    blogPost.AddTag(tag);
                }

                blogPost.Content = xmlBlogPost.Element(contentNS + "encoded").Value;
                blogPost.Save(postsPath);

                // TODO: Fix special tags
                // TODO: Download and relink images
            }

            // TODO: Other pages
        }

        public class LinkPost : BlogPost {
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
            readonly string layout;
            readonly List<string> tags;

            protected readonly StringBuilder content;

            public BlogPost() {
                layout = "post";
                tags = new List<string>();

                content = new StringBuilder("---");
            }

            public string Title { get; set; }
            public string Link { get; set; }

            public virtual string Content {
                get {
                    return content.ToString();
                }

                set {
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
                    content.AppendLine(value);
                }
            }

            public void AddTag(string tag) {
                tags.Add(tag);
            }

            public void Save(string basePath) {
                var dateParts = Link.Split('/');
                var fileName = $"{dateParts[2]}-{dateParts[3]}-{dateParts[4]}-{dateParts[5]}";
                File.WriteAllText(Path.Combine(basePath, fileName) + ".html", Content);
            }
        }
    }
}
