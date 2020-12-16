using Fclp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GenerateHfOnXml
{
    class Program
    {
        static void Main(string[] args)
        {
            var sourceFile = string.Empty;
            var original = string.Empty;
            var mapped = string.Empty;
            var folder = string.Empty;

            var fluentCommandLineParser = new FluentCommandLineParser();
            fluentCommandLineParser.Setup<string>('s', "source")
                        .Callback(t => sourceFile = t)
                        .WithDescription("The list of hot fixes");

            fluentCommandLineParser.Setup<string>('m', "mapping")
                        .Callback(t => mapped = t)
                        .WithDescription("The mapped txt files");

            fluentCommandLineParser.Setup<string>('o', "original")
                        .Callback(t => original = t)
                        .WithDescription("The original xml");

            fluentCommandLineParser.Setup<string>('f', "folder")
                        .Callback(t => folder = t)
                        .WithDescription("The folder where the rtf are located");

            fluentCommandLineParser.Parse(args);

            List<(string hotfix, string comments)> list;

            if (!string.IsNullOrEmpty(sourceFile))
            {
                list = ReadHotfixes(sourceFile);
            }
            else if (!string.IsNullOrEmpty(folder))
            {
                list = ParseHotFixes(folder);
            }
            else
            {
                throw new InvalidOperationException();
            }
            
            
            Dictionary<string, string> mappedList = ReadMappedHotfixes(mapped);

            var toMerge = XElement.Load(original);
            var targetXml = new XElement("databaseChangeLog");

            foreach (var item in toMerge.Descendants("changeSet"))
            {
                if (mappedList.TryGetValue(item.Attribute("id").Value, out string value) && !string.IsNullOrWhiteSpace(value))
                {
                    var toRemove = list.Where(
                        v => string.Compare(v.hotfix, value, StringComparison.InvariantCultureIgnoreCase) == 0)
                        .ToList();

                    foreach (var rm in toRemove)
                    {
                        list.Remove(rm);
                    }

                    var valuesBefore = list
                            .Where(v => string.Compare(v.hotfix, value, StringComparison.InvariantCultureIgnoreCase) < 0)
                            .ToList();

                    if (valuesBefore.Any())
                    {
                        foreach (var (hotfix, comments) in valuesBefore)
                        {
                            var element = new XElement("changeSet", new XAttribute("id", $"automaticallyInserted_{hotfix}"));
                            if (!string.IsNullOrWhiteSpace(comments))
                            {
                                element.Add(new XElement("comment", comments));
                            }

                            targetXml.Add(element);
                        }

                        list.RemoveAll(x => valuesBefore.Any(a => a == x));
                    }

                    var clone = Clone(item);
                    targetXml.Add(clone);
                }
            }

            var count = targetXml.Descendants("changeSet").Count(s => !s.Attribute("id").Value.StartsWith("automaticallyInserted_"));
            var originalCount = toMerge.Descendants("changeSet").Count();

            if(count != originalCount)
            {
                throw new InvalidOperationException($"Expected: {originalCount}, Got: {count}");
            }

            targetXml.Save("output.xml");
        }

        private static List<(string hotfix, string comments)> ParseHotFixes(string folder)
        {
            List<(string hotfix, string comments)> hfs = new List<(string hotfix, string comments)>();
            var fileList = Directory.GetFiles(folder);
            foreach (var item in fileList)
            {
                var hfName = ExtractName(Path.GetFileName(item));
                string comment = ParseComments(item);
                hfs.Add((hfName, comment));
            }

            return hfs;
        }

        private static string ParseComments(string path)
        {
            using (StreamReader file = new StreamReader(path))
            {
                string line = string.Empty;
                bool startCollect = false;

                var builder = new StringBuilder();

                while ((line = file.ReadLine()) != null)
                {
                    if (!startCollect && line.IndexOf("Beschreibung", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        startCollect = true;
                        continue;
                    }

                    if (!startCollect)
                    {
                        continue;
                    }

                    var result = line.Trim('}').Replace("\\par", string.Empty).Replace("\x00", string.Empty);
                    var splitted = result.Split('\\').LastOrDefault()?.Trim().Normalize();
                    
                    if (!string.IsNullOrEmpty(splitted))
                    {
                        builder.AppendLine(splitted);
                        //break;
                    }
                }

                return builder.ToString();
            }
        }

        private static XElement Clone(XElement item)
        {
            return new XElement(item);
        }


        public static Dictionary<string, string> ReadMappedHotfixes(string path)
        {
            var dict = new Dictionary<string, string>();
            using (StreamReader file = new StreamReader(path))
            {
                string line = string.Empty;
                while ((line = file.ReadLine()) != null)
                {
                    var index = line.IndexOf(".hf");
                    
                    if (index <= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Failed to find {line}");
                        continue;
                    }

                    var id = line.Substring(0, index);
                    var rest = line.Substring(index);
                    var hf = rest.Substring(1, rest.LastIndexOf('.') - 1);

                    dict.Add(id, hf);

                    Console.ResetColor();
                }
            }

            return dict;
        }

        public static List<(string hotfix, string comments)> ReadHotfixes(string path)
        {
            var list = new List<(string hotfix, string comments)>();
            using (StreamReader file = new StreamReader(path))
            {
                string line = string.Empty;
                while ((line = file.ReadLine()) != null)
                {
                    var hfName = ExtractName(line);
                    list.Add((hfName, string.Empty));
                }
            }

            list.Sort();
            return list;
        }

        private static string ExtractName(string line)
        {
            string hfName;
            var index = line.IndexOf('.');
            if (index > 0)
            {
                hfName = line.Substring(0, index);
            }
            else
            {
                hfName = line;
            }

            return hfName;
        }
    }
}
