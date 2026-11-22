using System;
using System.Collections.Generic;

namespace SkinChanceMod
{
    /// <summary>一档阈值：HP 百分比降到 hpPercent 以下时换成 image。</summary>
    public sealed class SkinStage
    {
        /// <summary>血量百分比上限（0-100）。这一档管「hpPercent 不小于当前血量」的区间。</summary>
        public int hpPercent = 100;

        /// <summary>贴图文件名；写 "0" 表示用游戏原图（不接管）。</summary>
        public string image = "";

        /// <summary>缩放倍数，1 = 和原图一样大。</summary>
        public float scale = 1f;

        /// <summary>像素偏移（右为正、下为正）。</summary>
        public float offsetX = 0f;
        public float offsetY = 0f;
    }

    /// <summary>一张皮肤的规则。itemId 即 SkinStandingPaintingConfigureItem.ItemID（见 SKINS.md）。</summary>
    public sealed class SkinRule
    {
        public int itemId;

        /// <summary>贴图所在目录（相对模组目录），由模组从 skins\&lt;文件夹&gt;\config.json 读出来时填。</summary>
        public string folder = "";

        public List<SkinStage> stages = new List<SkinStage>();
    }

    /// <summary>候选贴图。original 为真时表示用游戏原图，path 无意义。</summary>
    public sealed class SkinCandidate
    {
        public bool original;
        public string path = "";
        public float scale = 1f;
        public float offsetX;
        public float offsetY;

        /// <summary>用于日志与缓存的名字。</summary>
        public string Label
        {
            get { return original ? "原图" : path; }
        }
    }

    /// <summary>纯逻辑部分，便于离线单测。</summary>
    internal static class SkinRules
    {
        /// <summary>配置里写这个值等于「用游戏原图」。</summary>
        public const string OriginalName = "0";

        /// <summary>
        /// image 是否表示「用游戏原图」。只认裸的 "0"：
        /// "0.png" 是文件名（有人会把图命名成 0.png），不能当成原图吃掉。
        /// </summary>
        public static bool IsOriginal(string image)
        {
            if (string.IsNullOrEmpty(image))
            {
                return false;
            }
            return string.Equals(image.Trim(), OriginalName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>找到某张皮肤的规则，没有就返回 null。</summary>
        public static SkinRule FindRule(IList<SkinRule> rules, int itemId)
        {
            if (rules == null)
            {
                return null;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] != null && rules[i].itemId == itemId)
                {
                    return rules[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 当前血量命中 rule.stages 的哪一档；返回的是 <b>rule.stages 里的下标</b>
        /// （不是排序后的顺序下标，编辑器要拿它直接改用户写的那一档）；没命中返回 -1。
        /// </summary>
        public static int HitIndex(SkinRule rule, int percent)
        {
            if (rule == null || rule.stages == null || rule.stages.Count == 0)
            {
                return -1;
            }
            List<SkinStage> ordered = new List<SkinStage>(rule.stages);
            ordered.RemoveAll((SkinStage stage) => stage == null || string.IsNullOrEmpty(stage.image));
            ordered.Sort((SkinStage a, SkinStage b) => a.hpPercent.CompareTo(b.hpPercent));
            for (int i = 0; i < ordered.Count; i++)
            {
                if (percent <= ordered[i].hpPercent)
                {
                    return rule.stages.IndexOf(ordered[i]);
                }
            }
            return -1;
        }

        /// <summary>
        /// 挑出该皮肤当前血量可用的候选：先给命中的那一档，再从「更完好」往「更残」补，
        /// 这样用户只放了一部分图也能用。未配置该皮肤、或血量还没低到任何一档时返回空表（保持游戏原图）。
        /// 不依赖 stages 已排序。
        /// </summary>
        public static List<SkinCandidate> Candidates(IList<SkinRule> rules, int itemId, int percent)
        {
            List<SkinCandidate> result = new List<SkinCandidate>();
            if (rules == null)
            {
                return result;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                SkinRule rule = rules[i];
                if (rule == null || rule.itemId != itemId)
                {
                    continue;
                }
                List<SkinStage> stages = rule.stages;
                if (stages == null || stages.Count == 0)
                {
                    return result;
                }

                List<SkinStage> ordered = new List<SkinStage>(stages);
                ordered.RemoveAll((SkinStage stage) => stage == null || string.IsNullOrEmpty(stage.image));
                if (ordered.Count == 0)
                {
                    return result;
                }
                ordered.Sort((SkinStage a, SkinStage b) => a.hpPercent.CompareTo(b.hpPercent));

                int hit = -1;
                for (int j = 0; j < ordered.Count; j++)
                {
                    if (percent <= ordered[j].hpPercent)
                    {
                        hit = j;
                        break;
                    }
                }
                if (hit < 0)
                {
                    return result;
                }

                // 命中档 -> 往上（更完好）-> 往下（更残）
                Add(result, rule, ordered[hit]);
                for (int j = hit + 1; j < ordered.Count; j++)
                {
                    Add(result, rule, ordered[j]);
                }
                for (int j = hit - 1; j >= 0; j--)
                {
                    Add(result, rule, ordered[j]);
                }
                return result;
            }
            return result;
        }

        private static void Add(List<SkinCandidate> result, SkinRule rule, SkinStage stage)
        {
            SkinCandidate candidate = new SkinCandidate();
            candidate.scale = stage.scale <= 0f ? 1f : stage.scale;
            candidate.offsetX = stage.offsetX;
            candidate.offsetY = stage.offsetY;
            if (IsOriginal(stage.image))
            {
                candidate.original = true;
            }
            else if (!string.IsNullOrEmpty(rule.folder))
            {
                candidate.path = rule.folder + "/" + stage.image;
            }
            else
            {
                candidate.path = stage.image;
            }
            result.Add(candidate);
        }

        /// <summary>清掉非法档位并按 hpPercent 升序排序，返回是否有改动。</summary>
        public static bool Normalize(List<SkinRule> rules)
        {
            if (rules == null)
            {
                return false;
            }
            bool changed = false;
            for (int i = rules.Count - 1; i >= 0; i--)
            {
                SkinRule rule = rules[i];
                if (rule == null || rule.itemId == 0 || rule.stages == null)
                {
                    rules.RemoveAt(i);
                    changed = true;
                    continue;
                }
                int before = rule.stages.Count;
                rule.stages.RemoveAll((SkinStage stage) => stage == null || string.IsNullOrEmpty(stage.image));
                if (rule.stages.Count == 0)
                {
                    rules.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (rule.stages.Count != before)
                {
                    changed = true;
                }
                foreach (SkinStage stage in rule.stages)
                {
                    if (stage.hpPercent < 0)
                    {
                        stage.hpPercent = 0;
                        changed = true;
                    }
                    else if (stage.hpPercent > 100)
                    {
                        stage.hpPercent = 100;
                        changed = true;
                    }
                }
                List<SkinStage> sorted = new List<SkinStage>(rule.stages);
                sorted.Sort((SkinStage a, SkinStage b) => a.hpPercent.CompareTo(b.hpPercent));
                for (int j = 0; j < sorted.Count; j++)
                {
                    if (!ReferenceEquals(sorted[j], rule.stages[j]))
                    {
                        changed = true;
                        break;
                    }
                }
                rule.stages = sorted;
            }
            return changed;
        }
    }
}
