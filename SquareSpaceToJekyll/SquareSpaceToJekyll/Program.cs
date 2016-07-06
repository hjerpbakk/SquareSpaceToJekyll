using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using HtmlAgilityPack;
using SquareSpaceToJekyll;
using SquareSpaceToJekyll.Model;

namespace SquareSpaceToJekyll
{
    class MainClass
    {
        public static void Main (string [] args) {
            var xml = XElement.Load("/Users/sankra/Projects/SquareSpaceToJekyll/export.xml");

            var children = xml.Elements().Single().Elements().ToArray();

            SaveSiteMetadata(children);
            SaveBlogposts(children);
            SaveOtherPages(children);


            // TODO: Favicon
            // TODO: Refactor

            // TODO: Issue - insert metadata collected from Squarespace site where it's useful for jekyll
        }

        static void SaveSiteMetadata(XElement[] children) {
            var siteMetaData = new SiteMetaData();
            siteMetaData.Title = children.First(e => e.Name == "title").Value;
            siteMetaData.Description = children.First(e => e.Name == "description").Value;
            using (TextWriter writer = File.CreateText(Path.Combine(UserSettings.PathToJekyllSite, "siteMetaData.yaml"))) {
                var a = new YamlDotNet.Serialization.Serializer();
                a.Serialize(writer, siteMetaData);
            }
        }

        static void SaveBlogposts(XElement[] children) {
            var io = new IO(UserSettings.PathToJekyllSite);
            var blogPosts = children.Where(i => i.Name == "item" && i.Element(Namespaces.wpNS + "post_type").Value == "post").ToArray();
            Parallel.ForEach(blogPosts, xmlBlogPost => {
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
            });
        }

        static void SaveOtherPages(XElement[] children) {
            var otherPages = children.Where(i => i.Name == "item" && i.Element(Namespaces.wpNS + "post_type").Value == "page" && i.Element(Namespaces.wpNS + "status").Value == "publish").ToArray();
            Parallel.ForEach(otherPages, xmlPage => {
                var page = new Page();
                page.Title = xmlPage.Element("title").Value;
                page.Link = xmlPage.Element("link").Value;
                page.Content = xmlPage.Element(Namespaces.contentNS + "encoded").Value;
                page.Save();
            });
        }
    }
}

public static class UserSettings {
    public static readonly bool DownloadImages;
    public static readonly bool RemoveImageWidhtAndHeight;
    public static readonly bool OverwriteExistingImages;
    public static readonly bool ReportDeadLinks;

    public const string PathToJekyllSite = "/Users/sankra/Projects/sankra.github.io";
    public const string ImageFolder = "img";
    public const string InternalLinkPrefix = "https://runar-ovesenhjerpbakk.squarespace.com/";

    /// <summary>
    /// This must match Squarespace's pattern to preserve SEO
    /// </summary>
    public const string BlogPostsURLPattern = "/blog/:year/:i_month/:i_day/:title";

    static UserSettings() {
        RemoveImageWidhtAndHeight = true;
        DownloadImages = true;
        OverwriteExistingImages = false;
        ReportDeadLinks = true;
    }


}


public class Page {
    readonly StringBuilder content;
  
    public Page() {
        content = new StringBuilder("---");
    }

    public string Title { get; set; }

    public string Link { get; set; }

    public string Content {
        set {
            Console.WriteLine($"Parsing page {Title}");
            content.AppendLine();
            content.AppendLine("layout: page");
            content.Append("title: \"");
            content.Append(Title.Replace("\"", "&quot;"));
            content.AppendLine("\"");
            content.Append("permalink: ");
            content.AppendLine(Link);
            content.AppendLine("---");
            var squarespaceContentParser = new SquarespaceContentParser();
            var squarespaceContent = squarespaceContentParser.Parse(Title, value);
            content.AppendLine(squarespaceContent.Content);
        }
    }

    public void Save() {
        // Since Jekyll original theme already has an about...
        if (Title.Equals("about", StringComparison.InvariantCultureIgnoreCase)) {
            File.Delete(Path.Combine(UserSettings.PathToJekyllSite, "about.md"));
        }

        File.WriteAllText(Path.Combine(UserSettings.PathToJekyllSite, Title) + ".html", content.ToString());
    }
}

public class LinkPost : BlogPost {
    public LinkPost(XElement blogPostXML, IO io) : base(blogPostXML, io) {

    }

    public string ExternalLink { get; set; }

    public override string Content {
        set {
            content.AppendLine("- link");
            content.Append("link: ");
            content.AppendLine(ExternalLink);
            base.Content = value;
        }
    }
}

public class BlogPost {
    readonly IO io;

    readonly string layout;
    readonly List<string> tags;

    protected readonly StringBuilder content;

    public BlogPost(XElement blogPostXML, IO io) {
        this.io = io;
        IsDraft = blogPostXML.Element(Namespaces.wpNS + "status").Value == "draft";

        layout = "post";
        tags = new List<string>();

        content = new StringBuilder("---");
        content.AppendLine();
        content.AppendLine("categories:");
        content.AppendLine("- blog");
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
            Console.WriteLine($"Parsing blogpost {Title}");
            content.AppendLine($"permalink: {UserSettings.BlogPostsURLPattern}");
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
            var squarespaceContentParser = new SquarespaceContentParser();
            var squarespaceContent = squarespaceContentParser.Parse(WebSafeTitle, value);
            content.AppendLine(squarespaceContent.Content);
        }
    }

    public void AddTag(string tag) {
        tags.Add(tag);
    }

    public void Save() {
        io.Save(this);
    }
}





public static class Namespaces {
    public static readonly XNamespace wpNS = "http://wordpress.org/export/1.2/";
    public static readonly XNamespace contentNS = "http://purl.org/rss/1.0/modules/content/";
}

public class IO {
    readonly string postsPath;
    readonly string draftsPath;

    public IO(string pathToJekyllSite) {
        postsPath = Directory.CreateDirectory(Path.Combine(pathToJekyllSite, "_posts")).FullName;
        draftsPath = Directory.CreateDirectory(Path.Combine(pathToJekyllSite, "_drafts")).FullName;
        Directory.CreateDirectory(Path.Combine(pathToJekyllSite));
    }

    public void Save(BlogPost blogPost) {
        SaveBlogPost(blogPost);
    }

    void SaveBlogPost(BlogPost blogPost) {
        var savePath = blogPost.IsDraft ? draftsPath : postsPath;
        File.WriteAllText(Path.Combine(savePath, blogPost.NameWithDate) + ".html", blogPost.Content);
    }
}
