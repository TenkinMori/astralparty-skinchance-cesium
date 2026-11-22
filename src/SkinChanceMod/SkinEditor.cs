using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace SkinChanceMod
{
    /// <summary>
    /// 编辑器面板：改当前这张皮肤、当前这一档的缩放与偏移，保存后直接写回该皮肤文件夹的 config.json。
    /// 只做界面与数据展示，真正的数值存在 <see cref="SkinEditState"/> 里，由控制器负责应用与落盘。
    /// </summary>
    internal sealed class SkinEditorPanel
    {
        private const float Width = 420f;
        private const float Height = 232f;
        private const float Margin = 18f;
        private const int SortingOrder = 30000;

        private static readonly Color BorderColor = new Color(0.36f, 0.76f, 1f, 0.55f);
        private static readonly Color FillColor = new Color(0.03f, 0.06f, 0.11f, 0.94f);
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
                Mathf.Max(Margin, root.width - Margin - Width),
                Mathf.Max(Margin, root.height - Margin - Height));
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
                _bg.touchable = false;
                _root.AddChild(_bg);
                _bg.DrawRect(Width, Height, 1, BorderColor, FillColor);

                _title = Text("立绘编辑", 18, TitleColor, true);
                AddAt(_title, 16f, 10f, Width - 32f, 24f);

                _info = Text("", 14, BodyColor, false);
                AddAt(_info, 16f, 36f, Width - 32f, 20f);

                _values = Text("", 14, MutedColor, false);
                AddAt(_values, 16f, 58f, Width - 32f, 20f);

                // 第一行：缩放 / 四向微调
                float y1 = 88f;
                float w = 62f;
                float gap = 8f;
                float x = 16f;
                AddButton("缩小", x, y1, w, 30, ButtonColor, delegate { Fire(OnScaleDown); });
                x += w + gap;
                AddButton("放大", x, y1, w, 30, ButtonColor, delegate { Fire(OnScaleUp); });
                x += w + gap;
                AddButton("←", x, y1, w, 30, ButtonColor, delegate { Fire(OnLeft); });
                x += w + gap;
                AddButton("→", x, y1, w, 30, ButtonColor, delegate { Fire(OnRight); });
                x += w + gap;
                AddButton("↑", x, y1, w, 30, ButtonColor, delegate { Fire(OnUp); });
                x += w + gap;
                AddButton("↓", x, y1, w, 30, ButtonColor, delegate { Fire(OnDown); });

                // 第二行：保存 / 恢复默认 / 关闭
                float y2 = 130f;
                AddButton("保存到配置", 16f, y2, 150f, 34, PrimaryColor, delegate { Fire(OnSave); });
                AddButton("恢复默认", 176f, y2, 100f, 34, ButtonColor, delegate { Fire(OnReset); });
                AddButton("关闭", 286f, y2, 100f, 34, ButtonColor, delegate { Fire(OnClose); });

                // 说明
                string hint = "在立绘上按住拖动可移动；按住 Ctrl 拖动改缩放；改完点保存写回该皮肤的 config.json。";
                GTextField tip = Text(hint, 12, MutedColor, false);
                AddAt(tip, 16f, 176f, Width - 32f, 44f);

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

        private void AddAt(GObject obj, float x, float y, float w, float h)
        {
            obj.SetXY(x, y);
            obj.SetSize(w, h);
            _root.AddChild(obj);
        }

        private void AddButton(string label, float x, float y, float w, float h, Color color, Action onClick)
        {
            GComponent button = new GComponent();
            button.opaque = true;
            button.touchable = true;
            button.SetSize(w, h);
            button.SetXY(x, y);

            GGraph bg = new GGraph();
            bg.touchable = false;
            bg.DrawRoundRect(w, h, color, new float[] { 8f, 8f, 8f, 8f });
            button.AddChild(bg);

            GTextField text = Text(label, 15, new Color(0.94f, 0.97f, 1f), false);
            text.align = AlignType.Center;
            text.verticalAlign = VertAlignType.Middle;
            text.SetSize(w, h);
            text.SetXY(0f, 0f);
            text.touchable = false;
            button.AddChild(text);

            button.onClick.Add(delegate(EventContext context) { onClick(); });
            _root.AddChild(button);
        }

        private static GTextField Text(string value, int size, Color color, bool bold)
        {
            GTextField text = new GTextField();
            text.text = value;
            text.touchable = false;
            text.textFormat = new TextFormat();
            text.textFormat.size = size;
            text.textFormat.color = color;
            text.textFormat.bold = bold;
            text.textFormat.font = UIConfig.defaultFont;
            text.textFormat.align = AlignType.Left;
            text.textFormat.lineSpacing = 3;
            if (text.textFormat.outlineColor == default(Color))
            {
                text.textFormat.outline = 0f;
            }
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
