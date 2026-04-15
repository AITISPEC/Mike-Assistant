using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MikeAssistant.Services
{
    public class PresetInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
    }

    public static class PresetLoader
    {
        public static List<PresetInfo> LoadPresetsFromUserFolder()
        {
            var presets = new List<PresetInfo>();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "config-presets");
            if (!Directory.Exists(folder))
                return presets;

            var presetFiles = Directory.GetFiles(folder, "*.preset.json");
            foreach (var filePath in presetFiles)
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    dynamic data = JsonConvert.DeserializeObject(json);
                    string id = data.identifier ?? Path.GetFileNameWithoutExtension(filePath);
                    string name = data.name ?? Path.GetFileNameWithoutExtension(filePath);
                    presets.Add(new PresetInfo
                    {
                        Id = id,
                        Name = name,
                        FileName = Path.GetFileName(filePath)
                    });
                }
                catch { /* пропускаем битые файлы */ }
            }
            return presets;
        }
    }
}