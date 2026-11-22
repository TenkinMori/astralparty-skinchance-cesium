using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CesiumLoader.SDK;
using CesiumLoader.SDK.Configuration;
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
    /// 战损立绘：局内左下角自己的角色立绘，按当前血量百分比换成 skins 目录里对应的贴图。
    ///
    /// 每张皮肤一个文件夹，文件夹里有自己的 config.json（阈值 / 图名 / 缩放 / 偏移）；
    /// image 写 "0" 表示这一档保留游戏原图。
    /// 做法是不动游戏自己的 loader：在 UICom_HeroSkin 里插一个自己的 GLoader 盖住原图，
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
        private string _lastCatalogSignature;

        private readonly Dictionary<string, NTexture> _nTextures = new Dictionary<string, NTexture>();
        private readonly List<UnityEngine.Object> _graveyard = new List<UnityEngine.Object>();

        private GLoader _overlay;
        private UICom_HeroSkin _appliedSkin;
        private GLoader _appliedSource;
        private SkinCandidate _appliedImage;
        private bool _appliedOriginal;

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
            get { return "1.2.0"; }
        }

        public override string Author
        {
            get { return "TenkinMori"; }
        }

        public override string Description
        {
            get { return "按血量百分比切换局内左下角自己的角色立绘，每张皮肤一个 config.json，可配阈值、贴图、缩放与偏移。"; }
        }

        public override void OnInitialize()
        {
            ReadConfig(true);
            EnsureSkinFolders();
            ReloadCatalog(true);
            Log.Info("战损立绘已加载：共 " + _rules.Count + " 条皮肤规则。");
        }

        public override void OnConfigChanged()
        {
            ReadConfig(false);
            ReleaseCache();
            ReloadCatalog(true);
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
            if (now >= _nextCatalogCheck)
            {
                _nextCatalogCheck = now + _reloadSeconds;
                try
                {
                    ReloadCatalog(false);
                }
                catch (Exception ex)
                {
                    Warn("重读皮肤配置失败：" + ex.Message);
                }
            }
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
                _loggedImage = null;
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

            List<SkinCandidate> candidates = SkinRules.Candidates(_rules, itemId, percent);
            if (candidates.Count == 0)
            {
                Restore();
                return;
            }

            Apply(skin, candidates, percent);

            if (!_ready)
            {
                _ready = true;
                Log.Info("战损立绘已接管：皮肤 " + itemId + " → " + (_appliedImage == null ? "?" : _appliedImage.Label) + "（HP " + percent + "%）。");
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

        private void Apply(UICom_HeroSkin skin, List<SkinCandidate> candidates, int percent)
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

            // 命中档优先，缺图就按「更完好 → 更残」退化；image 写 0 的那一档用游戏原图
            SkinCandidate chosen = null;
            NTexture texture = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                SkinCandidate candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }
                if (candidate.original)
                {
                    chosen = candidate;
                    break;
                }
                if (string.IsNullOrEmpty(candidate.path))
                {
                    continue;
                }
                if (_appliedImage != null && _appliedOriginal == false && _appliedImage.path == candidate.path
                    && _appliedSource == source && _overlay != null && _overlay.texture != null)
                {
                    chosen = candidate;
                    texture = _overlay.texture;
                    break;
                }
                NTexture loaded = GetTexture(candidate.path);
                if (loaded != null)
                {
                    chosen = candidate;
                    texture = loaded;
                    break;
                }
            }
            if (chosen == null)
            {
                Restore();
                return;
            }

            if (chosen.original)
            {
                // 这一档要求用游戏原图
                Restore();
                _ready = true;
                if (_loggedImage != "0")
                {
                    _loggedImage = "0";
                    Log.Info("立绘已切换：游戏原图（HP " + percent + "%）");
                }
                return;
            }

            EnsureOverlay(skin, source);

            if (_overlay.texture != texture)
            {
                _overlay.texture = texture;
            }
            _overlay.customScale = new Vector2(chosen.scale, chosen.scale);

            LayoutOverlay(source, chosen);
            _overlay.visible = true;
            source.visible = false;
            HideVideo(skin);

            _appliedSkin = skin;
            _appliedSource = source;
            _appliedImage = chosen;
            _appliedOriginal = false;

            if (_loggedImage != chosen.path)
            {
                _loggedImage = chosen.path;
                string extra = "";
                if (Math.Abs(chosen.scale - 1f) > 0.001f || Math.Abs(chosen.offsetX) > 0.01f || Math.Abs(chosen.offsetY) > 0.01f)
                {
                    extra = "，缩放 " + chosen.scale.ToString("0.##") + "，偏移 " + chosen.offsetX.ToString("0.##") + "," + chosen.offsetY.ToString("0.##");
                }
                Log.Info("立绘已切换：" + Path.GetFileName(chosen.path) + "（HP " + percent + "%" + extra + "）");
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
                _overlay.fill = FillType.ScaleFree;
                _overlay.align = source.align;
                _overlay.verticalAlign = source.verticalAlign;
                _overlay.pivot = new Vector2(0f, 0f);
            }
            skin.AddChild(_overlay);
            LayoutOverlay(source, null);
        }

        private void LayoutOverlay(GLoader source, SkinCandidate candidate)
        {
            if (_overlay == null || source == null)
            {
                return;
            }
            try
            {
                float w = source.width;
                float h = source.height;
                if (_overlay.width != w || _overlay.height != h)
                {
                    _overlay.SetSize(w, h);
                }
                float dx = candidate == null ? 0f : candidate.offsetX;
                float dy = candidate == null ? 0f : candidate.offsetY;
                _overlay.SetXY(source.x + dx, source.y + dy);
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
                    _overlay.customScale = Vector2.one;
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
            _appliedOriginal = false;
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
            _loggedImage = null;
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
            changed |= cfg.SetDefault("reloadSeconds", 2.0, ConfigDoc.Of(
                "多久检查一次 skins 目录里各皮肤 config.json 的改动（秒），改完保存即可生效，无需重启。",
                "多久檢查一次 skins 目錄裡各皮膚 config.json 的改動（秒），改完儲存即可生效，無需重啟。",
                "How often (seconds) to re-read each skin's config.json so edits apply without restarting.",
                "skins 内の各 config.json を再読み込みする間隔（秒）。保存すれば再起動不要。"));
            if (changed || firstLoad)
            {
                cfg.SaveIfDirty();
            }

            _enabled = cfg.GetBool("enabled", true);
            _hideFightVideo = cfg.GetBool("hideFightVideo", true);
            double reload = cfg.GetFloat("reloadSeconds", 2f);
            if (reload < 0.5)
            {
                reload = 0.5;
            }
            else if (reload > 30.0)
            {
                reload = 30.0;
            }
            _reloadSeconds = reload;
            _nextCatalogCheck = 0;
        }

        private double _reloadSeconds = 2.0;
        private double _nextCatalogCheck;

        /// <summary>
        /// 重新扫描 skins\&lt;文件夹&gt;\config.json。用文件时间戳做指纹，
        /// 没变化就直接返回，不会每 2 秒重读 136 个文件。
        /// </summary>
        private void ReloadCatalog(bool force)
        {
            string root = SdkPath.CurrentModDirectory;
            if (string.IsNullOrEmpty(root))
            {
                return;
            }
            string skinsRoot = Path.Combine(root, DefaultFolder);

            string signature = BuildSignature(skinsRoot);
            if (!force && signature == _lastCatalogSignature)
            {
                return;
            }

            List<SkinRule> rules = SkinCatalog.Load(skinsRoot, Log.Info, Warn);
            _rules.Clear();
            _rules.AddRange(rules);
            _lastCatalogSignature = signature;

            if (!force)
            {
                ReleaseCache();
                Log.Info("皮肤配置已更新：共 " + _rules.Count + " 条规则。");
            }
        }

        private static string BuildSignature(string skinsRoot)
        {
            try
            {
                if (!Directory.Exists(skinsRoot))
                {
                    return "";
                }
                string[] dirs = Directory.GetDirectories(skinsRoot);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                List<string> parts = new List<string>(dirs.Length);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string config = Path.Combine(dirs[i], SkinCatalog.ConfigFileName);
                    long ticks = 0L;
                    try
                    {
                        if (File.Exists(config))
                        {
                            ticks = File.GetLastWriteTimeUtc(config).Ticks;
                        }
                    }
                    catch
                    {
                        ticks = 0L;
                    }
                    parts.Add(Path.GetFileName(dirs[i]) + ":" + ticks.ToString());
                }
                return string.Join("|", parts.ToArray());
            }
            catch
            {
                return "";
            }
        }

        /// <summary>按皮肤 + 血量百分比查候选贴图；未配置该皮肤时返回空表。</summary>
        private List<SkinCandidate> PickImages(int itemId, int percent)
        {
            return SkinRules.Candidates(_rules, itemId, percent);
        }

        /// <summary>
        /// 把每个皮肤的文件夹建出来，并给缺 config.json 的补一份默认配置。
        /// 已经存在就跳过，不动用户改过的内容与放进去的图。
        /// </summary>
        private void EnsureSkinFolders()
        {
            try
            {
                string root = SdkPath.CurrentModDirectory;
                if (string.IsNullOrEmpty(root))
                {
                    Warn("拿不到模组目录，跳过贴图文件夹预建。");
                    return;
                }

                string baseDir = Path.Combine(root, DefaultFolder);
                Directory.CreateDirectory(baseDir);
                int created = 0;
                int configs = 0;
                for (int i = 0; i < SkinDefaults.Folders.Length; i++)
                {
                    string folder = SkinDefaults.Folders[i];
                    string dir = Path.Combine(baseDir, folder);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        created++;
                    }
                    string config = Path.Combine(dir, SkinCatalog.ConfigFileName);
                    if (!File.Exists(config))
                    {
                        int itemId = i < SkinDefaults.ItemIds.Length ? SkinDefaults.ItemIds[i] : SkinCatalog.ParseItemId(folder);
                        if (SkinCatalog.WriteDefaultConfig(config, itemId, folder))
                        {
                            configs++;
                        }
                    }
                }
                if (created > 0)
                {
                    Log.Info("已建好 " + created + " 个皮肤文件夹：" + baseDir);
                }
                if (configs > 0)
                {
                    Log.Info("已补上 " + configs + " 份默认 config.json。");
                }
            }
            catch (Exception ex)
            {
                Warn("预建皮肤文件夹失败：" + ex.Message);
            }
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
        }
    }
}
