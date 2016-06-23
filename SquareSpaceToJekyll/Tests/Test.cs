using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using System.Linq;

namespace Tests
{
    [TestFixture]
    public class SquareSpaceXMLParserTests {
        [Test]
        public void TestCase() {
            
            var xml = XElement.Load("/Users/sankra/Projects/SquareSpaceToJekyll/SquareSpaceToJekyll/Tests/bin/Debug/export.xml");

            var pathToOriginalSite = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "OriginalSite");
            Directory.CreateDirectory(pathToOriginalSite);

            var children = xml.Elements().Single().Elements().ToArray();

            // Site meatadata
            var siteMetaData = new SiteMetaData();
            siteMetaData.Title = children.First(e => e.Name == "title").Value;
            siteMetaData.Description = children.First(e => e.Name == "description").Value;
            using (TextWriter writer = File.CreateText(Path.Combine(pathToOriginalSite, "siteMetaData.yaml"))) {
                var a = new YamlDotNet.Serialization.Serializer();
                a.Serialize(writer, siteMetaData);
            }
        }

        public class SiteMetaData {
            public string Title { get; set; }
            public string Description { get; set; }
        }
    }
}

