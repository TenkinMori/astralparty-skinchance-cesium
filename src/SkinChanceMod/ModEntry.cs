using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
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
    /// 贴图解码在后台线程做，主线程只负责上传，切档时不会读盘/解压卡一帧。
    /// </summary>
    public sealed class SkinChanceController : ModBase
    {
        private const double PollSeconds = 0.2;
        private const string DefaultFolder = "skins";

        private static readonly FieldInfo PanelDictField =
            typeof(UIManager).GetField("_cachedPanelDict", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// GLoader.UpdateLayout 是 protected：customOffset/customScale 只在这个方法里生效，
        /// 光赋值不重排是没用的（游戏自己也是先设 customOffset/customScale 再设 url 触发重排）。
        /// </summary>
        private static readonly MethodInfo UpdateLayoutMethod =
            typeof(GLoader).GetMethod("UpdateLayout", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>一份后台解码任务。</summary>
        private sealed class TextureJob
        {
            public string Key;
            public string Path;
            public volatile bool Ready;
            public bool Failed;
            public int Width;
            public int Height;
            public byte[] Rgba;
        }

        private bool _enabled = true;
        private bool _hideFightVideo = true;
        private readonly List<SkinRule> _rules = new List<SkinRule>();
        private string _lastCatalogSignature;

        /// <summary>已就绪的贴图（key 为配置里写的相对路径）。加载失败的也记 null，避免反复重试。</summary>
        private readonly Dictionary<string, NTexture> _nTextures = new Dictionary<string, NTexture>();
        private readonly Dictionary<string, double> _failedAt = new Dictionary<string, double>();
        private readonly Dictionary<string, TextureJob> _jobs = new Dictionary<string, TextureJob>();
        private readonly List<TextureJob> _finished = new List<TextureJob>();
        private readonly object _jobLock = new object();

        /// <summary>贴图缺失/解码失败后，隔多久再试一次（秒）——方便用户中途补图。</summary>
        private const double RetryFailedSeconds = 5.0;

        /// <summary>
        /// 游戏自己的立绘图集偏移表（Skin.bin 的 Offsets）：ImageName → 半身偏移与缩放。
        /// 原版不是把 1261×2048 的全身图直接铺上去，而是按 CharacterStateSkinOffsetX/Y + Scale 裁出半身。
        /// 我们照抄原 loader 的 customOffset/customScale；万一接管时游戏还没设（为零），就用这张表兜底。
        /// </summary>
        private Dictionary<string, HeroSkinOffset> _offsets;

        private GLoader _overlay;
        private UICom_HeroSkin _appliedSkin;
        private GLoader _appliedSource;
        private SkinCandidate _appliedImage;

        // ApplyGeometry 的去重缓存：几何没变就不碰 FairyGUI，避免每帧重建合批造成卡顿
        private bool _geomValid;
        private GLoader _geomSource;
        private float _geomWidth;
        private float _geomHeight;
        private float _geomScale;
        private float _geomX;
        private float _geomY;
        private Vector2 _geomBaseOffset;
        private Vector2 _geomBaseScale;

        private string _loggedImage;
        private double _nextPoll;
        private bool _ready;
        private int _preloadedItem;

        /// <summary>本局游戏实际使用的立绘资源名（`UT_Hero_Card_117_04` 这种），Offsets 表按它建键。</summary>
        private string _paintingName;

        /// <summary>已经打过「Offsets 兜底」日志的资源名，避免每次重排都刷一条。</summary>
        private string _loggedFallback;

        /// <summary>接管前的立绘类型（0 静态 / 1 视频），撤掉时还原。</summary>
        private int _savedTypeIndex = -1;
        private bool _videoHidden;

        public override string Name
        {
            get { return "战损立绘"; }
        }

        public override string Version
        {
            get { return "1.2.3"; }
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
            Restore();
            ReloadCatalog(true);
            Log.Info("配置已更新：enabled=" + _enabled + "，规则=" + _rules.Count + " 条。");
        }

        public override void OnUpdate()
        {
            // 后台解码完成的贴图在这里上传（主线程），和面板开关无关
            PumpFinished();

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
                DestroyTexture(item.Value);
            }
            _nTextures.Clear();
            lock (_jobLock)
            {
                _jobs.Clear();
                _finished.Clear();
            }
        }

        private static void DestroyTexture(NTexture texture)
        {
            try
            {
                if (texture != null && texture.nativeTexture != null)
                {
                    UnityEngine.Object.Destroy(texture.nativeTexture);
                }
            }
            catch
            {
                // ignore
            }
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

            // 游戏自己的立绘资源名（Skin.bin 的 Character / InGameCharacter），
            // Offsets 表就是按这个名字建的键，兜底几何必须用它而不是我们的 1.png。
            _paintingName = ResolvePaintingName(self);

            int maxHp = self.Property.maxHP;
            if (maxHp <= 0)
            {
                Restore();
                return;
            }

            // 提前把这张皮肤要用的图都排进后台解码队列，避免掉血那一刻才读盘
            PreloadSkin(itemId);

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

        /// <summary>
        /// 局内左下角实际用的立绘资源名（`Skin.bin` 的 `Character`，angelMode 下取 `SfwCharacter`）。
        /// Offsets 表按这个名字建键，兜底几何与日志都用它。
        /// </summary>
        private static string ResolvePaintingName(BattlePlayerData data)
        {
            try
            {
                SkinStandingPaintingConfigureItem painting = data.player.standingPainting;
                if (painting == null)
                {
                    return null;
                }
                (string, bool) characterInGame = painting.GetCharacterInGame();
                return string.IsNullOrEmpty(characterInGame.Item1) ? null : characterInGame.Item1;
            }
            catch
            {
                return null;
            }
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
            bool waiting = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                SkinCandidate candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }
                if (candidate.original)
                {
                    // 更高优先级的图还在解码时就先维持当前画面，别闪回原图
                    if (!waiting)
                    {
                        chosen = candidate;
                    }
                    break;
                }
                if (string.IsNullOrEmpty(candidate.path))
                {
                    continue;
                }
                NTexture loaded;
                if (_nTextures.TryGetValue(candidate.path, out loaded) && loaded != null)
                {
                    chosen = candidate;
                    texture = loaded;
                    break;
                }
                // 还没解码完就继续看下一档；全部都在解码中时保持当前画面，不要闪回原图
                RequestTexture(candidate.path);
                waiting = true;
            }
            if (chosen == null)
            {
                if (!waiting)
                {
                    Restore();
                }
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
            // 必须先铺几何再给贴图：GLoader.UpdateLayout 只在设 url/texture/尺寸时跑一次，
            // 顺序反了的话 customOffset/customScale 永远不会生效（整张全身图直接铺满，看起来就是尺寸不对）。
            ApplyGeometry(source, chosen);
            if (_overlay.texture != texture)
            {
                _overlay.texture = texture;
            }
            _overlay.visible = true;
            source.visible = false;
            HideVideo(skin);

            _appliedSkin = skin;
            _appliedSource = source;
            _appliedImage = chosen;

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
            }
            // 轴心也要跟着原 loader，否则 SetSize 时会按轴心补偿位置，和原图错开
            _overlay.pivotAsAnchor = source.pivotAsAnchor;
            _overlay.pivot = source.pivot;
            _overlay.align = source.align;
            _overlay.verticalAlign = source.verticalAlign;
            _overlay.fill = source.fill;
            _overlay.shrinkOnly = source.shrinkOnly;
            skin.AddChild(_overlay);
        }

        /// <summary>
        /// 完全照抄游戏原 loader 的几何：同样的开框尺寸、同样的 fill/align、同样的 customOffset/customScale，
        /// 再在此基础上乘一个缩放、加一个偏移。不要自己改 fill（改成 ScaleFree 会被拉扁）。
        /// 几何没变就直接返回 —— 每帧重设尺寸/偏移会让 FairyGUI 重建合批，看起来就是卡一下。
        /// </summary>
        private void ApplyGeometry(GLoader source, SkinCandidate candidate)
        {
            if (_overlay == null || source == null)
            {
                return;
            }
            try
            {
                float scale = candidate == null || candidate.scale <= 0f ? 1f : candidate.scale;
                float userX = candidate == null ? 0f : candidate.offsetX;
                float userY = candidate == null ? 0f : candidate.offsetY;

                float width = source.width;
                float height = source.height;
                Vector2 baseScale = source.customScale;
                if (baseScale.x == 0f || baseScale.y == 0f)
                {
                    baseScale = Vector2.one;
                }
                Vector2 baseOffset = source.customOffset;
                // 原 loader 还没被游戏设过几何时（customOffset 为零且 customScale 为单位），
                // 用 Offsets 表兜底算出半身偏移与缩放，否则会把全身图整张铺上去。
                // 表的键是游戏自己的资源名（UT_Hero_Card_117_04），不是我们的 1.png。
                if (baseOffset == Vector2.zero && baseScale == Vector2.one && candidate != null && !candidate.original)
                {
                    Vector2 fallbackOffset;
                    Vector2 fallbackScale;
                    if (TryFallbackGeometry(_paintingName, out fallbackOffset, out fallbackScale))
                    {
                        baseOffset = fallbackOffset;
                        baseScale = fallbackScale;
                        if (_loggedFallback != _paintingName)
                        {
                            _loggedFallback = _paintingName;
                            Log.Info("用 Offsets 表兜底半身取景：" + _paintingName + "，偏移 " + fallbackOffset.x.ToString("0.#") + "," + fallbackOffset.y.ToString("0.#") + "，缩放 " + fallbackScale.y.ToString("0.##"));
                        }
                    }
                }
                float ox = baseOffset.x + userX;
                // GLoader 里 content.y = -customOffset.y，所以想往下挪要把 customOffset.y 减小
                float oy = baseOffset.y - userY;
                if (ox == 0f && oy == 0f && Math.Abs(scale - 1f) > 0.0001f)
                {
                    // GLoader.UpdateLayout 只在 customOffset 非零时才应用 customScale，
                    // 这里给一个亚像素偏移，保证缩放配置能生效。
                    ox = 0.0001f;
                }

                if (_geomValid
                    && _geomSource == source
                    && _geomWidth == width && _geomHeight == height
                    && _geomScale == scale && _geomX == userX && _geomY == userY
                    && _geomBaseOffset == baseOffset && _geomBaseScale == baseScale
                    && _overlay.fill == source.fill
                    && _overlay.align == source.align
                    && _overlay.verticalAlign == source.verticalAlign
                    && _overlay.shrinkOnly == source.shrinkOnly)
                {
                    return;
                }

                _overlay.fill = source.fill;
                _overlay.align = source.align;
                _overlay.verticalAlign = source.verticalAlign;
                _overlay.shrinkOnly = source.shrinkOnly;
                if (_overlay.width != width || _overlay.height != height)
                {
                    _overlay.SetSize(width, height);
                }
                _overlay.customOffset = new Vector2(ox, oy);
                _overlay.customScale = new Vector2(baseScale.x * scale, baseScale.y * scale);
                if (_overlay.x != source.x || _overlay.y != source.y)
                {
                    _overlay.SetXY(source.x, source.y);
                }
                // 只有几何变了、贴图没换的情况下也要重排一次，否则改配置里的缩放/偏移不生效
                ApplyLayout();

                _geomValid = true;
                _geomSource = source;
                _geomWidth = width;
                _geomHeight = height;
                _geomScale = scale;
                _geomX = userX;
                _geomY = userY;
                _geomBaseOffset = baseOffset;
                _geomBaseScale = baseScale;
            }
            catch
            {
                // ignore
            }
        }

        private void ApplyLayout()
        {
            if (UpdateLayoutMethod == null || _overlay == null)
            {
                return;
            }
            try
            {
                UpdateLayoutMethod.Invoke(_overlay, null);
            }
            catch (Exception ex)
            {
                Warn("重排立绘覆盖层失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 游戏自己的立绘偏移表（Skin.bin 的 Offsets，ImageName 为键）。
        /// 只在原 loader 的 customOffset/customScale 还没设好（为零）时用来兜底。
        /// </summary>
        private Dictionary<string, HeroSkinOffset> Offsets
        {
            get
            {
                if (_offsets != null)
                {
                    return _offsets;
                }
                try
                {
                    GameLogicManager logic = SimpleSingletonProvider<GameLogicManager>.inst;
                    if (logic != null && logic.heroCard != null && logic.heroCard.heroSkinOffsetDict != null)
                    {
                        _offsets = logic.heroCard.heroSkinOffsetDict;
                    }
                }
                catch
                {
                    // ignore
                }
                return _offsets;
            }
        }

        /// <summary>原 loader 还没被游戏设过几何时，用 Offsets 表算出该图的半身偏移与缩放。</summary>
        private bool TryFallbackGeometry(string imageName, out Vector2 offset, out Vector2 scale)
        {
            offset = Vector2.zero;
            scale = Vector2.one;
            if (string.IsNullOrEmpty(imageName))
            {
                return false;
            }
            Dictionary<string, HeroSkinOffset> table = Offsets;
            HeroSkinOffset data;
            if (table == null || !table.TryGetValue(imageName, out data) || data == null)
            {
                return false;
            }
            offset = data.bustSkinOffset;
            float factor = data.reversalAxisX ? -1f : 1f;
            scale = new Vector2(data.bustSkinScale * factor, data.bustSkinScale);
            return true;
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
            _ready = false;
            _geomValid = false;
            _geomSource = null;
        }

        // ---------------- 贴图解码（后台线程）----------------

        /// <summary>把该皮肤所有非「原图」档位的贴图丢进解码队列。</summary>
        private void PreloadSkin(int itemId)
        {
            if (_preloadedItem == itemId)
            {
                return;
            }
            _preloadedItem = itemId;
            for (int i = 0; i < _rules.Count; i++)
            {
                SkinRule rule = _rules[i];
                if (rule == null || rule.itemId != itemId || rule.stages == null)
                {
                    continue;
                }
                for (int j = 0; j < rule.stages.Count; j++)
                {
                    SkinStage stage = rule.stages[j];
                    if (stage == null || string.IsNullOrEmpty(stage.image) || SkinRules.IsOriginal(stage.image))
                    {
                        continue;
                    }
                    RequestTexture(rule.folder + "/" + stage.image);
                }
            }
        }

        /// <summary>排一次后台解码；已完成或正在跑的不会重复排。失败过的隔 RetryFailedSeconds 再试。</summary>
        private void RequestTexture(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return;
            }
            if (_nTextures.ContainsKey(relative))
            {
                return;
            }
            lock (_jobLock)
            {
                if (_jobs.ContainsKey(relative))
                {
                    return;
                }
            }
            double failedAt;
            if (_failedAt.TryGetValue(relative, out failedAt) && Now() - failedAt < RetryFailedSeconds)
            {
                return;
            }

            string path = ResolvePath(relative);
            if (path == null)
            {
                // 用户可能还在往文件夹里拷图，过几秒再找一次
                _failedAt[relative] = Now();
                return;
            }

            TextureJob job = new TextureJob();
            job.Key = relative;
            job.Path = path;
            lock (_jobLock)
            {
                _jobs[relative] = job;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    int width;
                    int height;
                    byte[] rgba;
                    if (!PngDecoder.TryDecode(data, out width, out height, out rgba))
                    {
                        job.Failed = true;
                    }
                    else
                    {
                        FlipRows(rgba, width, height);
                        job.Width = width;
                        job.Height = height;
                        job.Rgba = rgba;
                    }
                }
                catch
                {
                    job.Failed = true;
                }
                job.Ready = true;
            });
        }

        /// <summary>主线程收尾：把解码好的 RGBA 上传成 Texture2D。</summary>
        private void PumpFinished()
        {
            _finished.Clear();
            lock (_jobLock)
            {
                if (_jobs.Count == 0)
                {
                    return;
                }
                List<string> keys = null;
                foreach (KeyValuePair<string, TextureJob> item in _jobs)
                {
                    if (item.Value != null && item.Value.Ready)
                    {
                        if (keys == null)
                        {
                            keys = new List<string>();
                        }
                        keys.Add(item.Key);
                    }
                }
                if (keys == null)
                {
                    return;
                }
                for (int i = 0; i < keys.Count; i++)
                {
                    _finished.Add(_jobs[keys[i]]);
                    _jobs.Remove(keys[i]);
                }
            }

            for (int i = 0; i < _finished.Count; i++)
            {
                TextureJob job = _finished[i];
                if (job.Failed || job.Rgba == null)
                {
                    Warn("解码失败：" + job.Path);
                    _failedAt[job.Key] = Now();
                    continue;
                }
                Texture2D texture = CreateTexture(job.Width, job.Height, job.Rgba);
                if (texture == null)
                {
                    _failedAt[job.Key] = Now();
                    continue;
                }
                _nTextures[job.Key] = new NTexture(texture);
                _failedAt.Remove(job.Key);
                Log.Info("已加载贴图 " + job.Width + "x" + job.Height + "：" + job.Path);
            }
            _finished.Clear();
        }

        private static Texture2D CreateTexture(int width, int height, byte[] rgba)
        {
            try
            {
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.LoadRawTextureData(rgba);
                texture.Apply(false, false);
                return texture;
            }
            catch (Exception ex)
            {
                WarnStatic("创建贴图失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>PNG 自上而下，Unity 贴图自下而上，逐行翻转。</summary>
        private static void FlipRows(byte[] rgba, int width, int height)
        {
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
                candidates.Add(Path.Combine(DefaultFolder, normalized));
                candidates.Add(normalized);
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
        /// 只换规则，不清贴图缓存 —— 清了就要重新解码，改个血量阈值也卡一下。
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
            _preloadedItem = 0;

            if (!force)
            {
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

        private static void WarnStatic(string message)
        {
            try
            {
                UnityEngine.Debug.LogWarning("[SkinChanceMod] " + message);
            }
            catch
            {
                // ignore
            }
        }
    }
}
