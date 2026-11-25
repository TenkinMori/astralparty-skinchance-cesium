using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using CesiumLoader.SDK;
using CesiumLoader.SDK.Configuration;
using CesiumLoader.SDK.Inputs;
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

            /// <summary>纯托管解码成功时的像素（自上而下，NGRA 顺序）。</summary>
            public int Width;
            public int Height;
            public byte[] Rgba;

            /// <summary>托管解不了的格式（JPG/WEBP/GIF/…）留原始字节，交给主线程用引擎解码。</summary>
            public byte[] Raw;

            /// <summary>格式名，只用于日志与报错。</summary>
            public string Format;
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
        /// <summary>上一次打过日志的皮肤 ItemID，用来在换皮肤时再打一条「当前皮肤」。</summary>
        private int _loggedItemId;
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

        // ---------------- 编辑器 ----------------

        /// <summary>皮肤贴图文件夹名 → 该皮肤当前是否用「原图」档，编辑器要据此提示。</summary>
        private SkinEditorPanel _editor;
        private readonly SkinEditState _edit = new SkinEditState();
        private bool _editing;
        private KeyCode _editKey = KeyCode.F12;
        private bool _editKeyHeld;
        private int _editItemId;
        private int _editPercent;

        /// <summary>拖动立绘时记录起点，避免每帧累积误差。</summary>
        private bool _skinDragging;
        private Vector2 _dragStartPointer;
        private float _dragStartOffsetX;
        private float _dragStartOffsetY;
        private float _dragStartScale;

        /// <summary>被我们临时放开的宿主组件（游戏原本是 touchableAll = false），收起编辑器时还原。</summary>
        private GComponent _touchHost;

        public override string Name
        {
            get { return "战损立绘"; }
        }

        public override string Version
        {
            get { return "1.3.5"; }
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

            // 编辑热键不跟着 0.2 秒的节流走，否则按一下可能整段被跳过
            try
            {
                TickEditKey();
            }
            catch (Exception ex)
            {
                Warn("编辑热键处理失败：" + ex.Message);
            }

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

        /// <summary>
        /// 缓存的 NTexture 还能不能用：跨局之后可能已经被 Dispose（`_root` 被置空）或 nativeTexture 被销毁。
        /// </summary>
        private static bool IsUsable(NTexture texture)
        {
            if (texture == null || texture.disposed)
            {
                return false;
            }
            try
            {
                return texture.nativeTexture != null;
            }
            catch
            {
                return false;
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
                if (_loggedItemId != itemId)
                {
                    _loggedItemId = itemId;
                    Log.Info("当前皮肤未配置（ItemID " + itemId + "，立绘 " + (_paintingName ?? "?")
                        + "）：到 skins 下对应文件夹里放图或改 config.json。");
                }
                Restore();
                return;
            }

            if (_loggedItemId != itemId)
            {
                _loggedItemId = itemId;
                Log.Info("当前皮肤：ItemID " + itemId + "（立绘 " + (_paintingName ?? "?") + "），规则 "
                    + _rules.Count + " 条里命中 " + candidates.Count + " 档候选。");
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
                if (_nTextures.TryGetValue(candidate.path, out loaded))
                {
                    if (IsUsable(loaded))
                    {
                        chosen = candidate;
                        texture = loaded;
                        break;
                    }
                    // 缓存的 NTexture 已经不可用了（跨局被 release 掉），丢掉重新解码
                    _nTextures.Remove(candidate.path);
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
            // 一局打完，游戏会把整个 UIBattleInfoPanel 树 Dispose 掉，我们是挂在 loader_Skin 下的子对象，
            // 会跟着一起被销毁（GComponent.Dispose 倒序 Dispose 所有子对象）。
            // 这时 _overlay 字段还在，但 displayObject/_content 已经没了，再 AddChild 上去
            // 一碰 texture/SetSize 就是 NullReferenceException，表现就是第二局立绘不出来。
            if (_overlay != null && _overlay.isDisposed)
            {
                _overlay = null;
                _geomValid = false;
                _geomSource = null;
                Log.Info("上一局的覆盖层已随面板销毁，重新创建。");
            }
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
            // 跨局时这些引用可能已经被游戏 Dispose()，碰一下就 NRE（第二局报的就是这个）
            if (_overlay != null && _overlay.isDisposed)
            {
                _overlay = null;
            }
            if (_appliedSkin != null && _appliedSkin.isDisposed)
            {
                _appliedSkin = null;
                _appliedSource = null;
            }
            if (_appliedSource != null && _appliedSource.isDisposed)
            {
                _appliedSource = null;
            }
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
            NTexture existing;
            if (_nTextures.TryGetValue(relative, out existing))
            {
                if (IsUsable(existing))
                {
                    return;
                }
                // 跨局之后旧的 NTexture 可能已被释放，重新排一次解码
                _nTextures.Remove(relative);
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
                    job.Format = ImageDecoder.Describe(data);

                    // 后台线程不能碰 Unity API：PNG/BMP/TGA 走纯托管，其余格式留原始字节给主线程
                    int width;
                    int height;
                    byte[] rgba;
                    if (ImageDecoder.TryDecodeManaged(data, out width, out height, out rgba))
                    {
                        FlipRows(rgba, width, height);
                        job.Width = width;
                        job.Height = height;
                        job.Rgba = rgba;
                    }
                    else
                    {
                        job.Raw = data;
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
                if (job.Failed)
                {
                    Warn("读取失败：" + job.Path);
                    _failedAt[job.Key] = Now();
                    continue;
                }

                Texture2D texture;
                if (job.Rgba != null)
                {
                    texture = CreateTexture(job.Width, job.Height, job.Rgba);
                }
                else
                {
                    // 托管解不了的格式交给引擎（ImageConversion.LoadImage，底层 stb + 平台解码器）
                    texture = LoadWithEngine(job.Raw, job.Format, job.Path);
                }
                if (texture == null)
                {
                    _failedAt[job.Key] = Now();
                    continue;
                }
                _nTextures[job.Key] = new NTexture(texture);
                _failedAt.Remove(job.Key);
                Log.Info("已加载贴图 " + texture.width + "x" + texture.height + "（" + job.Format + "）：" + job.Path);
            }
            _finished.Clear();
        }

        /// <summary>
        /// 用引擎解码任意 Unity 支持的格式（JPG/PNG/GIF/BMP/TGA/PSD/EXR/HDR/…，具体看平台解码器）。
        /// ImageConversion.LoadImage 必须在主线程。
        /// </summary>
        private Texture2D LoadWithEngine(byte[] data, string format, string path)
        {
            if (data == null || data.Length == 0)
            {
                Warn("解码失败（空文件）：" + path);
                return null;
            }
            try
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, data, false))
                {
                    UnityEngine.Object.Destroy(texture);
                    Warn("引擎不支持这种图片格式（" + format + "）：" + path
                        + "　建议转成 PNG 或 JPG 再用。");
                    return null;
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.Apply(false, false);
                return texture;
            }
            catch (Exception ex)
            {
                Warn("引擎解码异常（" + format + "）：" + path + " — " + ex.Message);
                return null;
            }
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
            changed |= cfg.SetDefault("editKey", "F12", ConfigDoc.Of(
                "编辑立绘的快捷键：局内按一下打开编辑器，可拖动立绘调大小/位置，点保存写回该皮肤的 config.json。填 Unity KeyCode 名称（F12、F11、Insert、Home、BackQuote 等），写 None 停用。",
                "編輯立繪的快捷鍵：局內按一下開啟編輯器，可拖動立繪調大小/位置，點儲存寫回該皮膚的 config.json。填 Unity KeyCode 名稱（F12、F11、Insert、Home、BackQuote 等），寫 None 停用。",
                "Hotkey to open the in-game splash-art editor. Press once in a match to drag/scale the art, then click save to write it back to that skin's config.json. Use a Unity KeyCode name (F12, F11, Insert, Home, BackQuote...); write None to disable.",
                "立ち絵エディタを開くホットキー。試合中に一度押すとエディタが開き、ドラッグで位置と大きさを調整して保存すると、そのスキンの config.json に書き戻します。Unity の KeyCode 名（F12、F11、Insert、Home、BackQuote など）を指定し、None で無効化。"));
            if (changed || firstLoad)
            {
                cfg.SaveIfDirty();
            }

            _enabled = cfg.GetBool("enabled", true);
            _hideFightVideo = cfg.GetBool("hideFightVideo", true);
            _editKey = ParseKey(cfg.GetString("editKey", "F12"), KeyCode.F12);
            _editKeyHeld = false;
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

        /// <summary>键名写错就退回默认值，不要因为一个笔误让热键失效。</summary>
        private static KeyCode ParseKey(string name, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }
            KeyCode key;
            if (Enum.TryParse(name.Trim(), true, out key) && key != KeyCode.None)
            {
                return key;
            }
            return fallback;
        }

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

        // ---------------- 编辑器 ----------------

        /// <summary>F12 开关编辑器。用 IsKeyHeld 自算下降沿：IsKeyPressed 只在本帧为真，掉一帧就丢。</summary>
        private void TickEditKey()
        {
            bool held = _editKey != KeyCode.None
                && InputService.IsAvailable
                && !InputService.IsKeyboardCaptured
                && InputService.IsKeyHeld(_editKey);
            bool edge = held && !_editKeyHeld;
            _editKeyHeld = held;
            if (!edge)
            {
                return;
            }
            if (_editing)
            {
                CloseEditor();
            }
            else
            {
                OpenEditor();
            }
        }

        private void OpenEditor()
        {
            BattleInfoPanel panel = FindBattleInfoPanel();
            if (panel == null || !panel.IsOpen())
            {
                Log.Info("编辑器：进对局后（左下角立绘出现时）再按 " + _editKey + "。");
                return;
            }
            BattlePlayerData self = GetSelfPlayerData();
            if (self == null || self.player == null)
            {
                Log.Info("编辑器：还没拿到自己的战斗数据。");
                return;
            }
            int itemId = ResolveItemId(self);
            SkinRule rule = SkinRules.FindRule(_rules, itemId);
            if (rule == null || rule.stages == null || rule.stages.Count == 0)
            {
                Log.Info("编辑器：皮肤 " + itemId + " 没有可用规则。");
                return;
            }

            int percent = CurrentPercent(self);
            int hit = SkinRules.HitIndex(rule, percent);
            if (hit < 0)
            {
                hit = rule.stages.Count - 1;
            }
            if (hit < 0 || hit >= rule.stages.Count)
            {
                return;
            }

            _editItemId = itemId;
            _editPercent = percent;
            _edit.itemId = itemId;
            _edit.folder = rule.folder;
            _edit.skinName = Path.GetFileName(rule.folder);
            _edit.stageIndex = hit;
            _edit.CopyFrom(rule.stages[hit]);
            _edit.percent = rule.stages[hit].hpPercent;

            if (_editor == null)
            {
                _editor = new SkinEditorPanel(Warn);
                _editor.OnScaleDown = delegate { NudgeScale(-ScaleStep); };
                _editor.OnScaleUp = delegate { NudgeScale(ScaleStep); };
                _editor.OnLeft = delegate { NudgeOffset(-OffsetStep, 0f); };
                _editor.OnRight = delegate { NudgeOffset(OffsetStep, 0f); };
                _editor.OnUp = delegate { NudgeOffset(0f, -OffsetStep); };
                _editor.OnDown = delegate { NudgeOffset(0f, OffsetStep); };
                _editor.OnSave = SaveEditor;
                _editor.OnReset = ResetEditor;
                _editor.OnClose = CloseEditor;
            }
            _editing = true;
            EnsureEditorOverlay();
            _editor.Show(
                "立绘编辑 · " + _edit.skinName,
                "第 " + (hit + 1) + " 档（HP ≤ " + _edit.percent + "%） · 当前 HP " + percent + "% · " + ImageLabel(_edit.image),
                _edit.scale,
                _edit.offsetX,
                _edit.offsetY);
            Log.Info("编辑器已打开：" + _edit.skinName + " 第 " + (hit + 1) + " 档（" + ImageLabel(_edit.image) + "）。");
        }

        private void CloseEditor()
        {
            if (!_editing)
            {
                return;
            }
            _editing = false;
            if (_editor != null)
            {
                _editor.Hide();
            }
            ReleaseEditorOverlay();
            Log.Info("编辑器已关闭。");
        }

        private static string ImageLabel(string image)
        {
            return SkinRules.IsOriginal(image) || string.IsNullOrEmpty(image) ? "游戏原图" : image;
        }

        private const float ScaleStep = 0.05f;
        private const float OffsetStep = 10f;
        private const float MinScale = 0.1f;
        private const float MaxScale = 4f;

        private void NudgeScale(float delta)
        {
            _edit.scale = Clamp(_edit.scale + delta, MinScale, MaxScale);
            RefreshEditor();
        }

        private void NudgeOffset(float dx, float dy)
        {
            _edit.offsetX += dx;
            _edit.offsetY += dy;
            RefreshEditor();
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        /// <summary>把编辑器里的数值推到当前档，立刻在画面上生效（不落盘）。</summary>
        private void RefreshEditor()
        {
            SkinRule rule = SkinRules.FindRule(_rules, _editItemId);
            if (rule == null || rule.stages == null)
            {
                return;
            }
            // 编辑的是「打开编辑器那一刻命中的那一档」，不要随着拖动反复重算，
            // 否则改到一半血量变化会跳到别的档上。
            int hit = _edit.stageIndex;
            if (hit < 0 || hit >= rule.stages.Count)
            {
                hit = SkinRules.HitIndex(rule, _editPercent);
            }
            if (hit >= 0 && hit < rule.stages.Count)
            {
                SkinStage stage = rule.stages[hit];
                if (stage == null)
                {
                    return;
                }
                stage.scale = _edit.scale;
                stage.offsetX = _edit.offsetX;
                stage.offsetY = _edit.offsetY;
                _edit.stageIndex = hit;
                _edit.percent = stage.hpPercent;
                _edit.image = stage.image;
            }
            if (_editor != null)
            {
                _editor.UpdateValues(_edit.scale, _edit.offsetX, _edit.offsetY);
            }
            // 数值改了要让 ApplyGeometry 重新走一遍
            _geomValid = false;
        }

        /// <summary>保存：写回该皮肤文件夹的 config.json。</summary>
        private void SaveEditor()
        {
            SkinRule rule = SkinRules.FindRule(_rules, _editItemId);
            if (rule == null || rule.stages == null || rule.stages.Count == 0)
            {
                Warn("保存失败：找不到皮肤 " + _editItemId + " 的规则。");
                return;
            }
            string path = ConfigPathOf(rule);
            if (path == null)
            {
                Warn("保存失败：找不到配置文件 " + rule.folder + "/config.json。");
                return;
            }
            if (!SkinCatalog.SaveStages(path, rule.itemId, _edit.skinName, rule.stages))
            {
                Warn("保存失败：" + path);
                return;
            }
            Log.Info("已保存：" + Path.GetFileName(path) + " 第 " + (_edit.stageIndex + 1) + " 档（缩放 "
                + _edit.scale.ToString("0.00") + "，偏移 " + _edit.offsetX.ToString("0") + "," + _edit.offsetY.ToString("0") + "）。");
            // 让热重载下一轮能察觉（时间戳已变），同时立刻刷一次指纹避免重复读
            _lastCatalogSignature = null;
            // 保存成功就收工：直接退出编辑器，把立绘交还给游戏
            CloseEditor();
        }

        /// <summary>恢复该档到默认缩放/偏移（1 倍、零偏移），要按保存才落盘。</summary>
        private void ResetEditor()
        {
            _edit.scale = 1f;
            _edit.offsetX = 0f;
            _edit.offsetY = 0f;
            RefreshEditor();
            Log.Info("已恢复默认缩放与偏移（记得点保存）。");
        }

        private string ConfigPathOf(SkinRule rule)
        {
            try
            {
                string root = SdkPath.CurrentModDirectory;
                if (string.IsNullOrEmpty(root) || rule == null || string.IsNullOrEmpty(rule.folder))
                {
                    return null;
                }
                string relative = rule.folder.Replace('/', Path.DirectorySeparatorChar);
                string dir = Path.Combine(root, relative);
                string path = Path.Combine(dir, SkinCatalog.ConfigFileName);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        private int CurrentPercent(BattlePlayerData self)
        {
            int maxHp = self.Property.maxHP;
            if (maxHp <= 0)
            {
                return 100;
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
            return (int)Math.Floor(hp * 100.0 / maxHp);
        }

        /// <summary>编辑器开着时，把覆盖层改成可拖：按住拖 = 移动，按住 Ctrl 拖 = 缩放。</summary>
        private void EnsureEditorOverlay()
        {
            if (_overlay == null || _overlay.isDisposed)
            {
                return;
            }
            if (_skinDragging)
            {
                return;
            }
            try
            {
                // 游戏在 BattleInfoPanel.InitTouchable() 里写死了 loader_Skin.touchableAll = false，
                // 而 FairyGUI 的 DisplayObject.InternalHitTest 是「父级不可触摸就不下探子对象」，
                // 所以只把覆盖层设成 touchable 收不到任何事件，必须先把宿主 GComponent 放开。
                GComponent host = _overlay.parent;
                if (host != null)
                {
                    host.touchable = true;
                    _touchHost = host;
                }
                _overlay.touchable = true;
                _overlay.onTouchBegin.Remove(OnSkinTouchBegin);
                _overlay.onTouchBegin.Add(OnSkinTouchBegin);
                _overlay.onTouchMove.Remove(OnSkinTouchMove);
                _overlay.onTouchMove.Add(OnSkinTouchMove);
                _overlay.onTouchEnd.Remove(OnSkinTouchEnd);
                _overlay.onTouchEnd.Add(OnSkinTouchEnd);
            }
            catch (Exception ex)
            {
                Warn("编辑器接管立绘失败：" + ex.Message);
            }
        }

        private void OnSkinTouchBegin(EventContext context)
        {
            if (!_editing)
            {
                return;
            }
            InputEvent input = context.inputEvent;
            _dragStartPointer = new Vector2(input.x, input.y);
            _dragStartOffsetX = _edit.offsetX;
            _dragStartOffsetY = _edit.offsetY;
            _dragStartScale = _edit.scale;
            _skinDragging = true;
            context.CaptureTouch();
            context.StopPropagation();
        }

        private void OnSkinTouchMove(EventContext context)
        {
            if (!_editing || !_skinDragging)
            {
                return;
            }
            InputEvent input = context.inputEvent;
            float dx = input.x - _dragStartPointer.x;
            float dy = input.y - _dragStartPointer.y;
            if (input.ctrl)
            {
                // 缩放：往上拖放大、往下拖缩小
                _edit.scale = Clamp(_dragStartScale + (dy / 240f), MinScale, MaxScale);
            }
            else
            {
                _edit.offsetX = _dragStartOffsetX + dx;
                _edit.offsetY = _dragStartOffsetY + dy;
            }
            RefreshEditor();
            context.StopPropagation();
        }

        private void OnSkinTouchEnd(EventContext context)
        {
            if (!_skinDragging)
            {
                return;
            }
            _skinDragging = false;
            Log.Info("立绘已调整：缩放 " + _edit.scale.ToString("0.00") + "，偏移 "
                + _edit.offsetX.ToString("0") + "," + _edit.offsetY.ToString("0") + "（点保存写回配置）。");
        }

        /// <summary>编辑器关掉后把立绘交还给游戏（不再拦截点击）。</summary>
        private void ReleaseEditorOverlay()
        {
            if (_overlay == null || _overlay.isDisposed)
            {
                return;
            }
            try
            {
                _skinDragging = false;
                _overlay.onTouchBegin.Remove(OnSkinTouchBegin);
                _overlay.onTouchMove.Remove(OnSkinTouchMove);
                _overlay.onTouchEnd.Remove(OnSkinTouchEnd);
                _overlay.touchable = false;
                if (_touchHost != null)
                {
                    // 游戏在 InitTouchable 里把它设成 false，交还时恢复，免得挡住原本的点击穿透
                    if (!_touchHost.isDisposed)
                    {
                        _touchHost.touchable = false;
                    }
                    _touchHost = null;
                }
            }
            catch
            {
                // ignore
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
