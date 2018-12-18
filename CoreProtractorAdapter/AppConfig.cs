using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using ProtractorAdapter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace ProtractorTestAdapter
{
    public enum TestFramework
    {
        Unknown,
        None,
        Jasmine
    }

    [SettingsName(Name)]
    public class AppConfig : ISettingsProvider
    {
        public const string Name = "ProtractorAdapter";
        private static XElement _config = null;
        private static bool tryLoadConfig = false;
        public static XElement Config { get {
                if (_config != null || tryLoadConfig) return _config;
                tryLoadConfig = true;
                var path = Helper.FindInDirectoryTree(Directory.GetCurrentDirectory(), "adapter.xml") + Path.DirectorySeparatorChar + "adapter.xml";
                if (File.Exists(path))
                {
                    Console.WriteLine($"Using configuration file {path}. Any runsettings or testsettings will still override this file.");
                    _config = XElement.Parse(File.ReadAllText(path));
                }
                else
                {
                    Console.WriteLine("Adapter.xml not found, using runsettings / testsettings or defaults");
                }
                return _config;
            }
        }
        public static string GetConfig(string element)
        {
            var value = ReadFromXml(Config, element);
            if(value == null)
                Console.WriteLine($"Couldn't find {element} in xml configuration, using defaults.");
            return value;
        }

        public const string VSTestFileExtension = ".feature"; // This can't be dynamic, we need to recompile the extension
        public static string Include = GetConfig("include") ?? $"**/*{VSTestFileExtension}";
        private static TestFramework _Framework = TestFramework.Unknown;
        public static TestFramework Framework { get {
                if (_Framework == TestFramework.Unknown &&
                    !Enum.TryParse(GetConfig("framework"), true, out _Framework))
                {
                    _Framework = TestFramework.None;
                }
                return _Framework;
            }
            set {
                _Framework = value;
            }
        }
        public static string ResultsPath = GetConfig("results") ?? null;
        public static string Program = GetConfig("program") ?? "npm";
        public static string Arguments = GetConfig("arguments") ?? "run protractor --";
        public static string Exclude = GetConfig("exclude") ?? "node_modules";
        private static string ReadFromXml(XElement config, string element)
        {
            try
            {
                return config.Element(element).Value;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        public void Load(XmlReader xml)
        {
            if (xml == null) return;
            xml.MoveToContent();
            XElement reader;
            using (XmlReader subTree = xml.ReadSubtree())
            {
                reader = XElement.Load(subTree);
            }
            Include = ReadFromXml(reader, "include") ?? Include;
            Exclude = ReadFromXml(reader, "exclude") ?? Exclude;
            Enum.TryParse(ReadFromXml(reader, "framework"), out _Framework);
            ResultsPath = ReadFromXml(reader, "results") ?? ResultsPath;
            Program = ReadFromXml(reader, "program") ?? Program;
            Arguments = ReadFromXml(reader, "arguments") ?? Arguments;
        }
    }
}
