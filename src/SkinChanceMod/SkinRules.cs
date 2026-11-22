using System.Collections.Generic;

namespace SkinChanceMod
{
    /// <summary>一档阈值：HP 百分比降到 hpPercent 以下时换成 image。</summary>
    public sealed class SkinStage
    {
        public int hpPercent = 100;
        public string image = "";
    }

    /// <summary>一张皮肤的规则。itemId 即 SkinStandingPaintingConfigureItem.ItemID（见 SKINS.md）。</summary>
    public sealed class SkinRule
    {
        public int itemId;
        public List<SkinStage> stages = new List<SkinStage>();
    }

    /// <summary>纯逻辑部分，便于离线单测。</summary>
    internal static class SkinRules
    {
        /// <summary>
        /// 挑出当前血量百分比对应的贴图；未配置该皮肤、或血量高于所有阈值（还没受伤）时返回 null（保持游戏原图）。
        /// 语义：一档覆盖「血量 ≤ 它的 hpPercent、且比更低那一档更高」的区间；
        /// 命中「hpPercent 不小于当前血量」里最低的那一档。不依赖 stages 已排序。
        /// </summary>
        public static string Pick(IList<SkinRule> rules, int itemId, int percent)
        {
            if (rules == null)
            {
                return null;
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
                    return null;
                }
                SkinStage hit = null;
                for (int j = 0; j < stages.Count; j++)
                {
                    SkinStage stage = stages[j];
                    if (stage == null)
                    {
                        continue;
                    }
                    if (percent <= stage.hpPercent && (hit == null || stage.hpPercent < hit.hpPercent))
                    {
                        hit = stage;
                    }
                }
                return hit == null ? null : hit.image;
            }
            return null;
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
