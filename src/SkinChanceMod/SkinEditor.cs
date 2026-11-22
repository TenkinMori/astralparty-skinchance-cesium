using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace SkinChanceMod
{
    /// <summary>
    /// 编辑器面板：改当前这张皮肤、当前这一档的缩放与偏移，保存后直接写回该皮肤文件夹的 config.json。
    /// 只做界面与数据展示，真正的数值存在 <see cref="SkinEditState"/> 里，由控制器负责应用与落盘。
    /// 布局按文字实际高度往下排，标题/说明变长时面板自己长高，不会压到按钮或溢出面板。
    /// </summary>
    internal sealed class SkinEditorPanel
    {
        private const float Width = 460f;
        private const float Pad = 16f;
        private const float Gap = 6f;
        private const float RowGap = 10f;
        private const float ButtonHeight = 32f;
        private const float ActionHeight = 34f;
        private const float Margin = 18f;
        private const int SortingOrder = 30000;

        private static readonly Color BorderColor = new Color(0.36f, 0.76f, 1f, 0.55f);
        private static readonly Color FillColor = new Color(0.03f, 0.06f, 0.11f, 0.95f);
        private static readonly Color ButtonColor = new Color(0.14f, 0.21f, 0.32f, 0.96f);
        private static readonly Color PrimaryColor = new Color(0.16f, 0.42f, 0.62f, 0.98f);
        private static readonly Color TitleColor = new Color(0.78f, 0.94f, 1f);
        private static readonly Color BodyColor = new Color(0.93f, 0.96f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.72f, 0.84f);

        private readonly Action<string> _warn;

        private GComponent _root;
        private GGraph _bg;
        private GTextField _title;
        private GTextField _info;
        private GTextField _values;
        private GTextField _tip;
        private readonly List<GComponent> _nudgeButtons = new List<GComponent>();
        private readonly List<GComponent> _actionButtons = new List<GComponent>();
        private bool _built;
        private bool _failed;

        private float _scale = 1f;
        private float _offsetX;
        private float _offsetY;

        public Action OnScaleDown;
        public Action OnScaleUp;
        public Action OnLeft;
        public Action OnRight;
        public Action OnUp;
        public Action OnDown;
        public Action OnSave;
        public Action OnReset;
        public Action OnClose;

        public SkinEditorPanel(Action<string> warn)
        {
            _warn = warn;
        }

        public bool Visible
        {
            get { return _root != null && _root.visible; }
        }

        public void Show(string title, string info, float scale, float offsetX, float offsetY)
        {
            if (!Ensure())
            {
                return;
            }
            _scale = scale;
            _offsetX = offsetX;
            _offsetY = offsetY;
            _title.text = title;
            _info.text = info;
            RefreshValues();
            Relayout();
            _root.visible = true;
            Raise();
        }

        public void Hide()
        {
            if (_root != null)
            {
                _root.visible = false;
            }
        }

        /// <summary>不重建面板，只刷新数值行（拖拽/按键调整时调用）。</summary>
        public void UpdateValues(float scale, float offsetX, float offsetY)
        {
            _scale = scale;
            _offsetX = offsetX;
            _offsetY = offsetY;
            RefreshValues();
        }

        public void UpdateInfo(string info)
        {
            if (_info != null)
            {
                _info.text = info;
                Relayout();
            }
        }

        public void Dispose()
        {
            if (_root == null)
            {
                return;
            }
            try
            {
                if (_root.parent != null)
                {
                    _root.parent.RemoveChild(_root);
                }
                _root.Dispose();
            }
            catch
            {
                // ignore
            }
            _root = null;
            _nudgeButtons.Clear();
            _actionButtons.Clear();
            _built = false;
        }

        private void RefreshValues()
        {
            if (_values == null)
            {
                return;
            }
            _values.text = "缩放 " + _scale.ToString("0.00")
                + "    偏移 " + _offsetX.ToString("0") + " , " + _offsetY.ToString("0");
        }

        private void Raise()
        {
            GRoot root = GRoot.inst;
            if (root == null || _root == null)
            {
                return;
            }
            if (_root.parent != root)
            {
                root.AddChild(_root);
            }
            _root.sortingOrder = SortingOrder;
            _root.SetXY(
                Mathf.Max(Margin, root.width - Margin - _root.width),
                Mathf.Max(Margin, root.height - Margin - _root.height));
        }

        /// <summary>
        /// 按当前文字高度从上往下排：标题 → 说明 → 数值 → 微调按钮 → 操作按钮 → 提示，
        /// 最后按总高度重画底板。文字长短变化时面板自己长高，不会把按钮挤到外面。
        /// </summary>
        private void Relayout()
        {
            if (_root == null)
            {
                return;
            }
            float inner = Width - Pad * 2f;
            float y = 10f;

            y = PlaceText(_title, y, inner, 24f) + 4f;
            y = PlaceText(_info, y, inner, 20f) + 2f;
            y = PlaceText(_values, y, inner, 20f) + RowGap;

            int count = _nudgeButtons.Count;
            if (count > 0)
            {
                float w = (inner - Gap * (count - 1)) / count;
                for (int i = 0; i < count; i++)
                {
                    GComponent button = _nudgeButtons[i];
                    button.SetSize(w, ButtonHeight);
                    button.SetXY(Pad + i * (w + Gap), y);
                    ResizeButtonContent(button, w, ButtonHeight);
                }
                y += ButtonHeight + RowGap;
            }

            int actions = _actionButtons.Count;
            if (actions == 3)
            {
                float w1 = 166f;
                float w2 = 118f;
                _actionButtons[0].SetSize(w1, ActionHeight);
                _actionButtons[0].SetXY(Pad, y);
                ResizeButtonContent(_actionButtons[0], w1, ActionHeight);
                _actionButtons[1].SetSize(w2, ActionHeight);
                _actionButtons[1].SetXY(Pad + w1 + 8f, y);
                ResizeButtonContent(_actionButtons[1], w2, ActionHeight);
                _actionButtons[2].SetSize(w2, ActionHeight);
                _actionButtons[2].SetXY(Pad + w1 + 8f + w2 + 8f, y);
                ResizeButtonContent(_actionButtons[2], w2, ActionHeight);
                y += ActionHeight + 8f;
            }

            float tipHeight = PlaceText(_tip, y, inner, 16f);
            y += tipHeight + 10f;

            float height = Mathf.Max(120f, y);
            _bg.DrawRect(Width, height, 1, BorderColor, FillColor);
            _root.SetSize(Width, height);
        }

        /// <summary>把文字放到 (Pad, y)，宽度 inner，返回它实际占的高度。</summary>
        private float PlaceText(GTextField text, float y, float inner, float minHeight)
        {
            if (text == null)
            {
                return minHeight;
            }
            text.SetSize(inner, minHeight);
            float height = Mathf.Max(minHeight, text.textHeight);
            text.SetSize(inner, height);
            text.SetXY(Pad, y);
            return height;
        }

        /// <summary>按钮里的底图与文字跟着按钮尺寸走（DrawRoundRect 内部会 SetSize）。</summary>
        private static void ResizeButtonContent(GComponent button, float w, float h)
        {
            GGraph bg = button.GetChild("bg") as GGraph;
            if (bg != null)
            {
                bg.DrawRoundRect(w, h, bg.color, new float[] { 8f, 8f, 8f, 8f });
            }
            GTextField label = button.GetChild("label") as GTextField;
            if (label != null)
            {
                label.SetSize(w, h);
                label.SetXY(0f, 0f);
            }
        }

        private bool Ensure()
        {
            if (_failed)
            {
                return false;
            }
            if (_built)
            {
                return true;
            }
            try
            {
                GRoot root = GRoot.inst;
                if (root == null)
                {
                    return false;
                }
                _root = new GComponent();
                _root.name = "SkinChanceEditor";
                _root.opaque = true;
                _root.touchable = true;
                _root.draggable = true;
                _root.sortingOrder = SortingOrder;

                _bg = new GGraph();
                _bg.name = "bg";
                _bg.touchable = false;
                _root.AddChild(_bg);

                _title = Text("立绘编辑", 18, TitleColor, true);
                _title.autoSize = AutoSizeType.Height;
                _root.AddChild(_title);

                _info = Text("", 14, BodyColor, false);
                _info.autoSize = AutoSizeType.Height;
                _root.AddChild(_info);

                _values = Text("", 14, MutedColor, false);
                _root.AddChild(_values);

                _nudgeButtons.Add(AddButton("缩小", ButtonColor, delegate { Fire(OnScaleDown); }));
                _nudgeButtons.Add(AddButton("放大", ButtonColor, delegate { Fire(OnScaleUp); }));
                _nudgeButtons.Add(AddButton("←", ButtonColor, delegate { Fire(OnLeft); }));
                _nudgeButtons.Add(AddButton("→", ButtonColor, delegate { Fire(OnRight); }));
                _nudgeButtons.Add(AddButton("↑", ButtonColor, delegate { Fire(OnUp); }));
                _nudgeButtons.Add(AddButton("↓", ButtonColor, delegate { Fire(OnDown); }));

                _actionButtons.Add(AddButton("保存到配置", PrimaryColor, delegate { Fire(OnSave); }));
                _actionButtons.Add(AddButton("恢复默认", ButtonColor, delegate { Fire(OnReset); }));
                _actionButtons.Add(AddButton("关闭", ButtonColor, delegate { Fire(OnClose); }));

                string hint = "在立绘上按住拖动可移动；按住 Ctrl 拖动改缩放。点「保存到配置」写回该皮肤并自动退出。";
                _tip = Text(hint, 12, MutedColor, false);
                _tip.autoSize = AutoSizeType.Height;
                _root.AddChild(_tip);

                Relayout();
                root.AddChild(_root);
                _root.visible = false;
                _built = true;
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                if (_warn != null)
                {
                    _warn("编辑器不可用，已停用：" + ex.Message);
                }
                return false;
            }
        }

        private void Fire(Action action)
        {
            if (action == null)
            {
                return;
            }
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (_warn != null)
                {
                    _warn("编辑器操作失败：" + ex.Message);
                }
            }
        }

        private GComponent AddButton(string label, Color color, Action onClick)
        {
            GComponent button = new GComponent();
            button.name = "btn" + label;
            button.opaque = true;
            button.touchable = true;
            button.SetSize(100f, ButtonHeight);

            GGraph bg = new GGraph();
            bg.name = "bg";
            bg.touchable = false;
            bg.DrawRoundRect(100f, ButtonHeight, color, new float[] { 8f, 8f, 8f, 8f });
            button.AddChild(bg);

            GTextField text = Text(label, 15, new Color(0.94f, 0.97f, 1f), false);
            text.name = "label";
            text.align = AlignType.Center;
            text.verticalAlign = VertAlignType.Middle;
            text.SetSize(100f, ButtonHeight);
            text.SetXY(0f, 0f);
            text.touchable = false;
            button.AddChild(text);

            button.onClick.Add(delegate(EventContext context) { onClick(); });
            _root.AddChild(button);
            return button;
        }

        private static GTextField Text(string value, int size, Color color, bool bold)
        {
            GTextField text = new GTextField();
            // 默认的 GTextField 是 autoSize=Both（宽度跟着文字长，SetSize 会被忽略），
            // 这里统一关掉，让 SetSize 生效；需要自动换行的再单独设成 Height。
            text.autoSize = AutoSizeType.None;
            text.text = value;
            text.touchable = false;
            text.textFormat = new TextFormat();
            text.textFormat.size = size;
            text.textFormat.color = color;
            text.textFormat.bold = bold;
            text.textFormat.font = UIConfig.defaultFont;
            text.textFormat.align = AlignType.Left;
            text.textFormat.lineSpacing = 3;
            return text;
        }
    }

    /// <summary>编辑器当前编辑的那一档（值会实时应用到画面）。</summary>
    internal sealed class SkinEditState
    {
        public int itemId;
        public string folder = "";
        public string skinName = "";
        public int stageIndex;
        public float scale = 1f;
        public float offsetX;
        public float offsetY;
        public int percent = 100;
        public string image = "";

        public void CopyFrom(SkinStage stage)
        {
            if (stage == null)
            {
                return;
            }
            image = stage.image;
            scale = stage.scale <= 0f ? 1f : stage.scale;
            offsetX = stage.offsetX;
            offsetY = stage.offsetY;
        }
    }
}
