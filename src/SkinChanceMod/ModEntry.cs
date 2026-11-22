using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CesiumLoader.SDK;
using CesiumLoader.SDK.Configuration;
using CesiumLoader.SDK.Logging;
using CesiumLoader.SDK.Mods;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;

namespace SkinChanceMod
{
    public static class ModEntry
    {
        public static void Main()
        {
            ModBase.Run(new SkinChanceController());
        }
    }

    /// <summary>
    /// 战损立绘：局内左下角自己的角色立绘，按当前血量百分比换成配置里对应的贴图。
    ///
    /// 做法是不动游戏自己的 loader：在 UICom_HeroSkin 里对应位置插一个自己的 GLoader 盖住原图，
    /// 原 loader 只隐藏不改 url —— 游戏侧 RefreshHeroSkin() 会重设 url/布局，改 url 必被顶掉。
    /// </summary>
    public sealed class SkinChanceController : ModBase
    {
        private const double PollSeconds = 0.2;
        private const string DefaultFolder = "skins";

        private static readonly FieldInfo PanelDictField =
            typeof(UIManager).GetField("_cachedPanelDict", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool _enabled = true;
        private bool _hideFightVideo = true;
        private readonly List<SkinRule> _rules = new List<SkinRule>();

        private readonly Dictionary<string, NTexture> _nTextures = new Dictionary<string, NTexture>();
        private readonly List<UnityEngine.Object> _graveyard = new List<UnityEngine.Object>();

        private GLoader _overlay;
        private UICom_HeroSkin _appliedSkin;
        private GLoader _appliedSource;
        private string _appliedImage;
        private string _appliedKey;

        private string _loggedImage;
        private double _nextPoll;
        private bool _ready;

        /// <summary>接管前的立绘类型（0 静态 / 1 视频），撤掉时还原。</summary>
        private int _savedTypeIndex = -1;
        private bool _videoHidden;

        public override string Name
        {
            get { return "战损立绘"; }
        }

        public override string Version
        {
            get { return "1.0.1"; }
        }

        public override string Author
        {
            get { return "TenkinMori"; }
        }

        public override string Description
        {
            get { return "按血量百分比切换局内左下角自己的角色立绘，做成战损效果。"; }
        }

        public override void OnInitialize()
        {
            ReadConfig(true);
            Log.Info("战损立绘已加载：共 " + _rules.Count + " 条皮肤规则。");
        }

        public override void OnConfigChanged()
        {
            ReadConfig(false);
            ReleaseCache();
            Log.Info("配置已更新：enabled=" + _enabled + "，规则=" + _rules.Count + " 条。");
        }

        public override void OnUpdate()
        {
            double now = Now();
            if (now < _nextPoll)
            {
                return;
            }
            _nextPoll = now + PollSeconds;
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Warn("轮询失败：" + ex.Message);
            }
        }

        public override void OnUnload()
        {
            Restore();
            if (_overlay != null)
            {
                try
                {
                    _overlay.Dispose();
                }
                catch
                {
                    // ignore
                }
                _overlay = null;
            }
            foreach (KeyValuePair<string, NTexture> item in _nTextures)
            {
                try
                {
                    if (item.Value != null && item.Value.nativeTexture != null)
                    {
                        UnityEngine.Object.Destroy(item.Value.nativeTexture);
                    }
                }
                catch
                {
                    // ignore
                }
            }
            _nTextures.Clear();
            foreach (UnityEngine.Object obj in _graveyard)
            {
                if (obj != null)
                {
                    try
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            _graveyard.Clear();
        }

        private static double Now()
        {
            return DateTime.UtcNow.Ticks * 1e-7;
        }

        // ---------------- 主流程 ----------------

        private void Tick()
        {
            if (!_enabled)
            {
                Restore();
                return;
            }

            BattleInfoPanel panel = FindBattleInfoPanel();
            if (panel == null || !panel.IsOpen())
            {
                Restore();
                return;
            }

            UICom_HeroSkin skin = ResolveSkin(panel);
            if (skin == null)
            {
                Restore();
                return;
            }

            BattlePlayerData self = GetSelfPlayerData();
            if (self == null || self.player == null)
            {
                Restore();
                return;
            }

            int itemId = ResolveItemId(self);
            if (itemId == 0)
            {
                Restore();
                return;
            }

            int maxHp = self.Property.maxHP;
            if (maxHp <= 0)
            {
                Restore();
                return;
            }

            int hp = self.Property.HP.Value;
            if (hp < 0)
            {
                hp = 0;
            }
            if (hp > maxHp)
            {
                hp = maxHp;
            }
            int percent = (int)Math.Floor(hp * 100.0 / maxHp);

            string image = PickImage(itemId, percent);
            if (image == null)
            {
                Restore();
                return;
            }

            Apply(skin, image, percent);

            if (!_ready)
            {
                _ready = true;
                Log.Info("战损立绘已接管：皮肤 " + itemId + " → " + image + "（HP " + percent + "%）。");
            }
        }

        private BattlePlayerData GetSelfPlayerData()
        {
            GameLogicManager logic = SimpleSingletonProvider<GameLogicManager>.inst;
            if (logic == null || logic.battle == null)
            {
                return null;
            }
            return logic.battle.GetSelfPlayerData();
        }

        private static int ResolveItemId(BattlePlayerData data)
        {
            try
            {
                if (data.player.standingPainting != null)
                {
                    return data.player.standingPainting.ItemID;
                }
            }
            catch
            {
                // ignore
            }
            return 0;
        }

        private BattleInfoPanel FindBattleInfoPanel()
        {
            if (PanelDictField == null)
            {
                return null;
            }
            UIManager manager = SimpleSingletonProvider<UIManager>.inst;
            if (manager == null)
            {
                return null;
            }
            try
            {
                IDictionary dict = PanelDictField.GetValue(manager) as IDictionary;
                if (dict == null || !dict.Contains(UIPanelType.BattleInfo))
                {
                    return null;
                }
                return dict[UIPanelType.BattleInfo] as BattleInfoPanel;
            }
            catch
            {
                return null;
            }
        }

        private static UICom_HeroSkin ResolveSkin(BattleInfoPanel panel)
        {
            try
            {
                UIBattleInfoPanel ui = panel.UIObject;
                if (ui == null || ui.loader_Skin == null)
                {
                    return null;
                }
                return ui.loader_Skin as UICom_HeroSkin;
            }
            catch
            {
                return null;
            }
        }

        // ---------------- 贴图接管 ----------------

        private void Apply(UICom_HeroSkin skin, string image, int percent)
        {
            GLoader source = skin.loader_Skin_Image;
            if (source == null)
            {
                return;
            }

            if (_appliedSkin != null && _appliedSkin != skin)
            {
                Restore();
            }

            EnsureOverlay(skin, source);

            string key = _appliedKey != null && _appliedImage == image ? _appliedKey : image;
            NTexture texture = GetTexture(image);
            if (texture == null)
            {
                Restore();
                return;
            }

            if (_overlay.texture != texture)
            {
                // 先设偏移再贴图：GLoader 在 UpdateLayout 末尾应用 customOffset/customScale
                _overlay.customOffset = source.customOffset;
                _overlay.customScale = source.customScale;
                _overlay.texture = texture;
            }

            LayoutOverlay(source);
            _overlay.visible = true;
            source.visible = false;
            HideVideo(skin);

            _appliedSkin = skin;
            _appliedSource = source;
            _appliedImage = image;
            _appliedKey = key;

            if (!string.IsNullOrEmpty(_appliedImage) && percent >= 0)
            {
                // 只有档位变化时才写日志，避免每帧刷屏
                if (_loggedImage != _appliedImage)
                {
                    _loggedImage = _appliedImage;
                    Log.Info("立绘已切换：" + Path.GetFileName(_appliedImage) + "（HP " + percent + "%）");
                }
            }
        }

        private void EnsureOverlay(UICom_HeroSkin skin, GLoader source)
        {
            if (_overlay != null && _overlay.parent == skin)
            {
                return;
            }
            if (_overlay != null && _overlay.parent != null)
            {
                try
                {
                    _overlay.parent.RemoveChild(_overlay);
                }
                catch
                {
                    // ignore
                }
            }
            if (_overlay == null)
            {
                _overlay = new GLoader();
                _overlay.name = "SkinChanceOverlay";
                _overlay.touchable = false;
                _overlay.fill = source.fill;
                _overlay.align = source.align;
                _overlay.verticalAlign = source.verticalAlign;
            }
            skin.AddChild(_overlay);
            LayoutOverlay(source);
        }

        private void LayoutOverlay(GLoader source)
        {
            if (_overlay == null || source == null)
            {
                return;
            }
            try
            {
                if (_overlay.width != source.width || _overlay.height != source.height)
                {
                    _overlay.SetSize(source.width, source.height);
                }
                _overlay.SetXY(source.x, source.y);
            }
            catch
            {
                // ignore
            }
        }

        private void HideVideo(UICom_HeroSkin skin)
        {
            if (!_hideFightVideo)
            {
                return;
            }
            try
            {
                if (_savedTypeIndex < 0 && skin.type != null)
                {
                    _savedTypeIndex = skin.type.selectedIndex;
                }
                if (skin.loader_Skin_Video != null && skin.loader_Skin_Video.visible)
                {
                    SimpleSingletonProvider<CriMovieManager>.inst.StopAndDestroy(skin.loader_Skin_Video);
                    skin.loader_Skin_Video.visible = false;
                    _videoHidden = true;
                }
                if (skin.type != null && skin.type.selectedIndex != 0)
                {
                    skin.type.selectedIndex = 0;
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>把接管撤掉，恢复游戏自己的立绘。</summary>
        private void Restore()
        {
            if (_overlay != null)
            {
                try
                {
                    _overlay.texture = null;
                    _overlay.visible = false;
                }
                catch
                {
                    // ignore
                }
            }
            if (_appliedSource != null)
            {
                try
                {
                    _appliedSource.visible = true;
                }
                catch
                {
                    // ignore
                }
            }
            if (_appliedSkin != null && _hideFightVideo)
            {
                try
                {
                    if (_appliedSkin.loader_Skin_Video != null && _videoHidden)
                    {
                        _appliedSkin.loader_Skin_Video.visible = true;
                    }
                    if (_savedTypeIndex >= 0 && _appliedSkin.type != null)
                    {
                        _appliedSkin.type.selectedIndex = _savedTypeIndex;
                    }
                }
                catch
                {
                    // ignore
                }
            }
            _videoHidden = false;
            _savedTypeIndex = -1;
            _appliedSkin = null;
            _appliedSource = null;
            _appliedImage = null;
            _appliedKey = null;
            _loggedImage = null;
            _ready = false;
        }

        // ---------------- 贴图缓存 ----------------

        private NTexture GetTexture(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }
            NTexture cached;
            if (_nTextures.TryGetValue(relative, out cached))
            {
                return cached;
            }

            string path = ResolvePath(relative);
            if (path == null)
            {
                Warn("找不到贴图：" + relative);
                _nTextures[relative] = null;
                return null;
            }

            Texture2D texture = LoadTexture(path);
            if (texture == null)
            {
                Warn("解码失败：" + path);
                _nTextures[relative] = null;
                return null;
            }

            NTexture nTexture = new NTexture(texture);
            _nTextures[relative] = nTexture;
            Log.Info("已加载贴图 " + texture.width + "x" + texture.height + "：" + path);
            return nTexture;
        }

        private void ReleaseCache()
        {
            Restore();
            _nTextures.Clear();
            _ready = false;
        }

        private static string ResolvePath(string relative)
        {
            string root = SdkPath.CurrentModDirectory;
            string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
            List<string> candidates = new List<string>();
            if (Path.IsPathRooted(normalized))
            {
                candidates.Add(normalized);
            }
            else
            {
                if (!string.IsNullOrEmpty(root))
                {
                    candidates.Add(Path.Combine(root, normalized));
                }
                candidates.Add(normalized);
                candidates.Add(Path.Combine(DefaultFolder, normalized));
            }
            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                    // ignore
                }
            }
            return null;
        }

        private Texture2D LoadTexture(string path)
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                Warn("读取失败：" + ex.Message);
                return null;
            }

            int width;
            int height;
            byte[] rgba;
            if (!PngDecoder.TryDecode(data, out width, out height, out rgba))
            {
                return null;
            }

            // PNG 自上而下，Unity 贴图自下而上，逐行翻转
            int stride = width * 4;
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * stride;
                int bottom = (height - 1 - y) * stride;
                for (int i = 0; i < stride; i++)
                {
                    byte tmp = rgba[top + i];
                    rgba[top + i] = rgba[bottom + i];
                    rgba[bottom + i] = tmp;
                }
            }

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, false);
            return texture;
        }

        // ---------------- 配置 ----------------

        private void ReadConfig(bool firstLoad)
        {
            ModConfig cfg = Config;
            if (cfg == null)
            {
                return;
            }

            bool changed = false;
            changed |= cfg.SetDefault("enabled", true, ConfigDoc.Of(
                "是否启用战损立绘。",
                "是否啟用戰損立繪。",
                "Enable the damaged splash-art swap.",
                "戦損立ち絵を有効にする。"));
            changed |= cfg.SetDefault("hideFightVideo", true, ConfigDoc.Of(
                "立绘是视频时是否强制切成静态图并停掉视频（否则接管不到）。",
                "立繪是影片時是否強制切成靜態圖並停掉影片（否則接管不到）。",
                "Force static art and stop the video when the skin uses a movie.",
                "立ち絵が動画の場合に静止画へ切り替えて動画を止める。"));
            changed |= cfg.SetDefault("characters", new List<SkinRule>(), ConfigDoc.Of(
                "按皮肤配置阈值与贴图：itemId 见 SKINS.md，stages 的 hpPercent 是血量百分比上限（从小到大命中第一档），image 是相对模组目录的路径。",
                "依皮膚設定閾值與貼圖：itemId 見 SKINS.md，stages 的 hpPercent 是血量百分比上限（由小到大命中第一檔），image 是相對模組目錄的路徑。",
                "Rules per skin: itemId from SKINS.md; stage.hpPercent is the HP%% upper bound (first match from low to high); image is relative to the mod folder.",
                "スキンごとの設定：itemId は SKINS.md 参照、hpPercent は HP%% の上限（小さい順に最初の一致）、image は MOD フォルダー相対パス。"));
            if (changed || firstLoad)
            {
                cfg.SaveIfDirty();
            }

            _enabled = cfg.GetBool("enabled", true);
            _hideFightVideo = cfg.GetBool("hideFightVideo", true);

            _rules.Clear();
            List<SkinRule> rules = cfg.Get<List<SkinRule>>("characters", null);
            if (rules != null)
            {
                foreach (SkinRule rule in rules)
                {
                    if (rule == null || rule.itemId == 0 || rule.stages == null)
                    {
                        continue;
                    }
                    rule.stages.RemoveAll((SkinStage stage) => stage == null || string.IsNullOrEmpty(stage.image));
                    if (rule.stages.Count == 0)
                    {
                        continue;
                    }
                    rule.stages.Sort((SkinStage a, SkinStage b) => a.hpPercent.CompareTo(b.hpPercent));
                    _rules.Add(rule);
                }
            }
            _nextPoll = 0;
        }

        /// <summary>取命中档位对应的贴图；未配置该皮肤时返回 null。</summary>
        private string PickImage(int itemId, int percent)
        {
            return SkinRules.Pick(_rules, itemId, percent);
        }

        private void Warn(string message)
        {
            try
            {
                Log.Warn(message);
            }
            catch
            {
                // ignore
            }
        }    }
}
