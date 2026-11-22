using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CesiumLoader.SDK.Configuration;

namespace SkinChanceMod
{
    /// <summary>一个皮肤文件夹里的 config.json。</summary>
    public sealed class SkinFolderConfig
    {
        /// <summary>皮肤 ItemID，见 SKINS.md。留 0 时用文件夹名前缀。</summary>
        public int itemId;

        /// <summary>备注名，只影响日志。</summary>
        public string name = "";

        public List<SkinStage> stages = new List<SkinStage>();
    }

    /// <summary>
    /// 扫描 skins\&lt;文件夹&gt;\config.json，拼成规则表。
    /// 文件夹存在但没有 config.json 时按默认三档（0 原图 / 1.png / 2.png）补一份。
    /// </summary>
    internal static class SkinCatalog
    {
        public const string ConfigFileName = "config.json";

        private const string DefaultDoc =
            "{\n" +
            "  // 一份示例：血量越低越残，image 写 \"0\" 表示保留游戏原图\n" +
            "  \"itemId\": 0,\n" +
            "  \"name\": \"\",\n" +
            "  \"stages\": [\n" +
            "    { \"hpPercent\": 100, \"image\": \"0\", \"scale\": 1.0, \"offsetX\": 0, \"offsetY\": 0 },\n" +
            "    { \"hpPercent\": 60, \"image\": \"1.png\", \"scale\": 1.0, \"offsetX\": 0, \"offsetY\": 0 },\n" +
            "    { \"hpPercent\": 25, \"image\": \"2.png\", \"scale\": 1.0, \"offsetX\": 0, \"offsetY\": 0 }\n" +
            "  ]\n" +
            "}\n";

        public static List<SkinRule> Load(string skinsRoot, Action<string> info, Action<string> warn)
        {
            List<SkinRule> rules = new List<SkinRule>();
            if (string.IsNullOrEmpty(skinsRoot) || !Directory.Exists(skinsRoot))
            {
                return rules;
            }

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(skinsRoot);
            }
            catch (Exception ex)
            {
                warn("扫描皮肤文件夹失败：" + ex.Message);
                return rules;
            }
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);

            int generated = 0;
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                string folderName = Path.GetFileName(dir);
                int folderItemId = ParseItemId(folderName);
                string configPath = Path.Combine(dir, ConfigFileName);
                bool exists = false;
                try
                {
                    exists = File.Exists(configPath);
                }
                catch
                {
                    exists = false;
                }
                if (!exists)
                {
                    if (folderItemId == 0)
                    {
                        continue;
                    }
                    if (WriteDefault(configPath, folderItemId, folderName))
                    {
                        generated++;
                    }
                    else
                    {
                        continue;
                    }
                }

                SkinFolderConfig config = null;
                string text = null;
                try
                {
                    text = File.ReadAllText(configPath, Encoding.UTF8);
                    config = CesiumJson.Deserialize<SkinFolderConfig>(text);
                }
                catch (Exception ex)
                {
                    warn("读不懂 " + folderName + "\\" + ConfigFileName + "：" + ex.Message);
                    continue;
                }
                if (config == null || config.stages == null || config.stages.Count == 0)
                {
                    warn("没读到任何 stages：" + folderName + "\\" + ConfigFileName + "（检查 JSON 是否写对）");
                    continue;
                }

                int itemId = config.itemId > 0 ? config.itemId : folderItemId;
                if (itemId == 0 || config.stages == null || config.stages.Count == 0)
                {
                    continue;
                }

                SkinRule rule = new SkinRule();
                rule.itemId = itemId;
                rule.folder = "skins/" + folderName;
                foreach (SkinStage stage in config.stages)
                {
                    if (stage == null || string.IsNullOrEmpty(stage.image))
                    {
                        continue;
                    }
                    stage.image = NormalizeImage(stage.image);
                    rule.stages.Add(stage);
                }
                if (rule.stages.Count == 0)
                {
                    continue;
                }
                SkinRules.Normalize(new List<SkinRule> { rule });
                rules.Add(rule);
            }

            if (generated > 0)
            {
                info("已给 " + generated + " 个皮肤文件夹补上默认 config.json。");
            }
            return rules;
        }

        /// <summary>"1" → "1.png"；"0" 保持 "0"（原图）；已经是 "x.png" 就不动。</summary>
        public static string NormalizeImage(string image)
        {
            if (string.IsNullOrEmpty(image))
            {
                return image;
            }
            string value = image.Trim().Replace('\\', '/');
            if (SkinRules.IsOriginal(value))
            {
                return SkinRules.OriginalName;
            }
            if (value.IndexOf('.') < 0)
            {
                return value + ".png";
            }
            return value;
        }

        /// <summary>文件夹名形如 100117006_钢羽森姬-茉莉，取前缀数字。</summary>
        public static int ParseItemId(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
            {
                return 0;
            }
            int end = 0;
            while (end < folderName.Length && folderName[end] >= '0' && folderName[end] <= '9')
            {
                end++;
            }
            if (end == 0)
            {
                return 0;
            }
            int value;
            if (int.TryParse(folderName.Substring(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return 0;
        }

        public static bool WriteDefaultConfig(string path, int itemId, string folderName)
        {
            return WriteDefault(path, itemId, folderName);
        }

        private static bool WriteDefault(string path, int itemId, string folderName)
        {
            try
            {
                string json = BuildDefaultJson(itemId, folderName);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildDefaultJson(int itemId, string folderName)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"itemId\": ").Append(itemId.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"name\": \"").Append(Escape(folderName)).Append("\",\n");
            sb.Append("  \"stages\": [\n");
            sb.Append("    { \"hpPercent\": ").Append(SkinDefaults.FullPercent).Append(", \"image\": \"0\", \"scale\": 1.0, \"offsetX\": 0, \"offsetY\": 0 },\n");
            sb.Append("    { \"hpPercent\": ").Append(SkinDefaults.DamagedPercent).Append(", \"image\": \"1.png\", \"scale\": 1.0, \"offsetX\": 0, \"offsetY\": 0 },\n");
            sb.Append("    { \"hpPercent\": ").Append(SkinDefaults.BrokenPercent).Append(", \"image\": \"2.png\", \"scale\": 1.0, \"offsetX\": 0, \"offsetY\": 0 }\n");
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>把默认 config.json 的说明贴出来（README 用）。</summary>
        public static string DocumentedSample()
        {
            return DefaultDoc;
        }
    }
}
