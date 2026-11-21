using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using ReworkedCopium;
using ReworkedCopium.UI;
using ShorelandsBillboard.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Box = ShorelandsBillboard.Core.RemoteLayout.Box;
using Icons = ShorelandsBillboard.Core.RemoteLayout.Icons;

namespace ShorelandsBillboard
{
    // What the remote shows and what its buttons do.
    public interface IRemoteHost
    {
        IList<string> List { get; }
        int ListVersion { get; }
        string Channel { get; }      // the one chosen, also before it is connected
        string State { get; }
        bool Powered { get; }
        bool Muted { get; }
        float Volume { get; }
        bool? Live(string channel);
        void Tune(string channel);
        void Step(int step);
        AddResult Add(string typed);
        bool Remove(string channel);
        void MakeDefault(string channel);
        void Power();
        void Mute();
        void VolumeStep(int step);
    }

    // The remote in the game's HUD: a button left of the game's menu button, made from a copy of it, and
    // the remote itself on the same canvas. Both go when that canvas goes (logout, another scene).
    public sealed class RemoteUi
    {
        const int SortingOrder = 170;   // over the HUD and the game's windows (150, 160), under its pop-ups (200) and the menu (500)
        const int Tries = 10;

        readonly IRemoteHost host;
        readonly Action<string> warn;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        Button menu; GameObject hud; RemoteWindow window;
        double lookAt; int tries; bool gaveUp;
        static Vector2 place = new Vector2(-4, -26);   // where the remote was left, for this session

        public RemoteUi(IRemoteHost host, Action<string> warn) { this.host = host; this.warn = warn; }

        public bool Open => window != null && window.gameObject.activeSelf;

        public void Tick(double now)
        {
            if (hud == null || menu == null || window == null)
            {
                if (gaveUp || now < lookAt) return;
                lookAt = now + 2;
                Destroy();
                string missing = Build();
                if (missing == null) { tries = 0; return; }
                Destroy();
                // the HUD may still be loading right after the login
                if (++tries >= Tries) { gaveUp = true; warn($"no remote: {missing} (game version {Application.version})"); }
                return;
            }
            // shown and hidden with the game's own button
            if (hud.activeSelf != menu.gameObject.activeSelf) hud.SetActive(menu.gameObject.activeSelf);
            var at = ((RectTransform)menu.transform).anchoredPosition - new Vector2(((RectTransform)menu.transform).rect.width + RemoteLayout.HudGap, 0);
            var mine = (RectTransform)hud.transform;
            if (mine.anchoredPosition != at) mine.anchoredPosition = at;
        }

        public void Toggle()
        {
            if (window == null) return;
            if (window.gameObject.activeSelf) window.Close(); else window.Show();
        }

        public void Destroy()
        {
            if (window != null) { place = window.Place; UnityEngine.Object.Destroy(window.gameObject); }
            if (hud != null) UnityEngine.Object.Destroy(hud);
            foreach (var o in owned) if (o != null) UnityEngine.Object.Destroy(o);
            owned.Clear();
            window = null; hud = null; menu = null;
        }

        // The button that opens the game's menu: the one wired to SettingsMenu.Toggle.
        static Button FindMenuButton()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (!b.gameObject.scene.IsValid() || !b.gameObject.activeInHierarchy || !(b.targetGraphic is Image)) continue;
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                    if (b.onClick.GetPersistentTarget(i) is SettingsMenu && b.onClick.GetPersistentMethodName(i) == "Toggle") return b;
            }
            return null;
        }

        Texture2D Art(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("embedded image '" + name + "' missing");
                var bytes = new byte[s.Length];
                for (int n = 0; n < bytes.Length;) n += s.Read(bytes, n, bytes.Length - n);
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.LoadImage(bytes);
                owned.Add(t); return t;
            }
        }

        Sprite Cut(Texture2D t, int x, int w)
        {
            var s = Sprite.Create(t, new Rect(x, 0, w, t.height), new Vector2(0.5f, 0.5f), 32, 0, SpriteMeshType.FullRect);
            owned.Add(s); return s;
        }

        // null, or what the game no longer has as known
        string Build()
        {
            menu = FindMenuButton();
            if (menu == null) return "the game's menu button was not found";
            var canvas = menu.GetComponentInParent<Canvas>();
            var font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == "VioPixelText SDF");
            var face = ((Image)menu.targetGraphic).sprite;
            if (canvas == null || font == null || face == null) return "the game's HUD canvas, font or button sprite was not found";

            hud = UnityEngine.Object.Instantiate(menu.gameObject, menu.transform.parent, false);
            hud.name = "BillboardRemoteButton";
            foreach (Transform child in hud.transform) UnityEngine.Object.Destroy(child.gameObject);
            var button = hud.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();   // the copy came with the menu's listener
            button.onClick.AddListener(() => { Toggle(); RemoteWindow.Deselect(); });
            var tip = hud.GetComponent<TooltipTrigger>();
            if (tip != null) { tip.header = ""; tip.content = "Billboard remote"; }
            var size = ((RectTransform)hud.transform).rect.size;
            var picture = RemoteWindow.Child("Icon", hud.transform);
            var image = picture.gameObject.AddComponent<Image>();
            var hudArt = Art("remote_hud.png");
            image.sprite = Cut(hudArt, 0, hudArt.width); image.raycastTarget = false;
            RemoteWindow.Put(picture, Mathf.RoundToInt(size.x - RemoteLayout.HudW) / 2, Mathf.RoundToInt(size.y - RemoteLayout.HudH) / 2, RemoteLayout.HudW, RemoteLayout.HudH);

            var go = new GameObject("BillboardRemote", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)) { layer = menu.gameObject.layer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.rootCanvas.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var own = go.GetComponent<Canvas>();
            own.overrideSorting = true; own.sortingOrder = SortingOrder;
            window = go.AddComponent<RemoteWindow>();
            var iconArt = Art("remote_icons.png");
            var icons = Enumerable.Range(0, RemoteLayout.IconCount).Select(i => Cut(iconArt, i * RemoteLayout.Icon, RemoteLayout.Icon)).ToArray();
            var bodyArt = Art("remote.png");
            var exit = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == "ui_exit" && s.rect.width == RemoteLayout.Close.W && s.rect.height == RemoteLayout.Close.H);
            window.Build(host, font, face, menu.colors, Cut(bodyArt, 0, bodyArt.width), icons, exit, place);
            go.SetActive(false);
            return null;
        }
    }

    // The remote: the mod's body art, the game's button sprite, font and tooltips, laid out on whole
    // pixels of the game's 640x360 canvas.
    public sealed class RemoteWindow : MonoBehaviour, IMenu, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        const float FlashSeconds = 2f;
        static readonly Color Glass = new Color32(0xc7, 0xd1, 0xe6, 0xff), GlassDim = new Color32(0x5c, 0x62, 0x78, 0xff);
        static readonly Color Accent = new Color32(0xff, 0xd8, 0x8c, 0xff), Print = new Color32(0xdb, 0xb7, 0x85, 0xff);
        static readonly Color Hint = new Color32(0x99, 0x8b, 0x74, 0xff), PowerTint = new Color(1f, 0.5f, 0.45f);

        sealed class Row
        {
            public string Name; public RectTransform Rect; public TextMeshProUGUI Label; public Image Dot; public GameObject Top, Remove; public bool Hot;
        }

        public static bool Typing { get; private set; }

        IRemoteHost host;
        TMP_FontAsset font; Sprite face; ColorBlock colors; Sprite[] icons;
        RectTransform body, view, content, mark;
        TextMeshProUGUI lcdName, lcdState, lcdInfo;
        Image muteIcon, powerFace;
        TMP_InputField field;
        readonly List<Row> rows = new List<Row>();
        int listVersion = -1, offset, rowWidth, selectedAt, shownIndex, shownCount = -1; string shownState; bool shownMuted; string shownChannel, flash; float flashUntil, drawnAtScale; bool dragging; Vector2 settledArea;

        public Vector2 Place => body.anchoredPosition;

        // ---- parts ----

        public static RectTransform Child(string name, Transform parent)
        {
            var t = (RectTransform)new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer }.transform;
            t.SetParent(parent, false);
            return t;
        }

        // from the parent's top-left corner, y down
        public static void Put(RectTransform r, int x, int y, int w, int h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static void Put(RectTransform r, Box b) => Put(r, b.X, b.Y, b.W, b.H);

        public static void Deselect()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // The game's labels: size 9 on a 10 px line, bottom aligned; with its one-pixel shadow on buttons and panels.
        TextMeshProUGUI Label(string name, Transform parent, Color color, bool shadow, bool cut = false)
        {
            var r = Child(name, parent);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSharedMaterial = font.material;
            t.fontSize = 9;
            t.alignment = TextAlignmentOptions.BottomLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = cut ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            t.richText = false;
            t.color = color;
            t.raycastTarget = false;
            if (shadow) r.gameObject.AddComponent<TextShadow>();   // copies the label as it is now
            return t;
        }

        static int Width(TextMeshProUGUI t, string text) => Mathf.CeilToInt(t.GetPreferredValues(text).x);

        // a label exactly as wide as its text, so no alignment lands on a half pixel
        static void PutText(TextMeshProUGUI t, string text, int x, int y)
        {
            if (t.text != text) t.text = text;
            Put(t.rectTransform, x, y, Math.Max(1, Width(t, text)), RemoteLayout.TextH);
        }

        Image Picture(string name, Transform parent, Sprite sprite, int x, int y)
        {
            var r = Child(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.raycastTarget = false;
            Put(r, x, y, Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height));
            return img;
        }

        // The game's button face, 9-sliced, with its hover and press tints; the picture on it is the mod's.
        Button Key(string name, Transform parent, Box box, Icons icon, Action click, string tip, out Image picture)
        {
            var r = Child(name, parent);
            Put(r, box);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = face; img.type = Image.Type.Sliced;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img; b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => { click(); Deselect(); });
            picture = Picture("Icon", r, icons[(int)icon], (box.W - RemoteLayout.Icon) / 2, (box.H - RemoteLayout.Icon) / 2);
            if (tip != null) { var t = r.gameObject.AddComponent<TooltipTrigger>(); t.header = ""; t.content = tip; }
            return b;
        }

        // ---- build ----

        public void Build(IRemoteHost host, TMP_FontAsset font, Sprite face, ColorBlock colors, Sprite bodyArt, Sprite[] icons, Sprite exit, Vector2 place)
        {
            this.host = host; this.font = font; this.face = face; this.colors = colors; this.icons = icons;

            body = Child("Body", transform);
            var bg = body.gameObject.AddComponent<Image>();
            bg.sprite = bodyArt;   // takes the clicks, so none reaches the world behind the remote
            body.anchorMin = body.anchorMax = body.pivot = new Vector2(1f, 1f);
            body.sizeDelta = new Vector2(RemoteLayout.W, RemoteLayout.H);
            body.anchoredPosition = place;

            var power = Key("Power", body, RemoteLayout.Power, Icons.Power, host.Power, "Billboard on / off", out _);
            powerFace = (Image)power.targetGraphic;
            powerFace.color = PowerTint;

            if (exit != null)
            {
                var r = Child("Close", body);
                Put(r, RemoteLayout.Close);
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = exit;
                var b = r.gameObject.AddComponent<Button>();
                b.targetGraphic = img; b.colors = colors; b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.onClick.AddListener(Close);
            }
            else
            {
                var c = RemoteLayout.Close;
                Key("Close", body, new Box(c.Right - RemoteLayout.SmallButton, c.Y, RemoteLayout.SmallButton, RemoteLayout.SmallButton), Icons.Cross, Close, null, out _);
            }

            lcdName = Label("Channel", body, Glass, false, true);
            Put(lcdName.rectTransform, RemoteLayout.LcdLine(0));
            lcdState = Label("State", body, Glass, false);
            lcdInfo = Label("Info", body, GlassDim, false);

            Key("VolumeUp", body, RemoteLayout.VolUp, Icons.Plus, () => Volume(+1), null, out _);
            Key("VolumeDown", body, RemoteLayout.VolDown, Icons.Minus, () => Volume(-1), null, out _);
            Key("ChannelUp", body, RemoteLayout.ChUp, Icons.Up, () => host.Step(-1), "Previous channel", out _);
            Key("ChannelDown", body, RemoteLayout.ChDown, Icons.Down, () => host.Step(+1), "Next channel", out _);
            Key("Mute", body, RemoteLayout.Mute, Icons.Sound, host.Mute, "Mute", out muteIcon);
            Caption("VOL", RemoteLayout.VolUp);
            Caption("CH", RemoteLayout.ChUp);

            view = Child("List", body);
            Put(view, RemoteLayout.ListView);
            view.gameObject.AddComponent<RectMask2D>();
            content = Child("Rows", view);
            mark = Child("ScrollMark", body);
            var markImage = mark.gameObject.AddComponent<Image>();
            markImage.color = Hint; markImage.raycastTarget = false;

            BuildField();
            Key("Add", body, RemoteLayout.Add, Icons.Plus, Add, "Add the channel", out _);
        }

        void Caption(string text, Box over)
        {
            var t = Label(text, body, Print, true);
            PutText(t, text, over.X + (over.W - Width(t, text)) / 2, RemoteLayout.CaptionY);
        }

        // A text field as the game builds its own in code (the chat tab's name field).
        void BuildField()
        {
            var box = RemoteLayout.Field.Inset(1);
            var r = Child("Name", body);
            Put(r, box);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.clear;
            var area = Child("TextArea", r);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(3, 1); area.offsetMax = new Vector2(-2, -1);
            area.gameObject.AddComponent<RectMask2D>();
            var hint = Label("Placeholder", area, Hint, false);
            var text = Label("Text", area, Color.white, false);
            foreach (var t in new[] { hint, text })
            {
                t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            }
            hint.text = "add channel";
            field = r.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = img;
            field.transition = Selectable.Transition.None;
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.fontAsset = font;
            field.pointSize = 9;
            field.characterLimit = 60;   // room for a pasted address
            field.richText = false;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.customCaretColor = true; field.caretColor = Color.white; field.caretWidth = 1;
            field.selectionColor = new Color(1f, 1f, 1f, 0.3f);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.onSelect.AddListener(_ => { SetTyping(true); selectedAt = Time.frameCount; });
            field.onDeselect.AddListener(_ => SetTyping(false));
            field.onSubmit.AddListener(_ => Add());
        }

        // While a name is typed the game takes no keys: the same switch its chat uses, and Enter does not open the chat.
        static void SetTyping(bool on)
        {
            if (Typing == on) return;
            Typing = on;
            var game = GameManager.Instance;
            if (game == null || game.inputSystem == null) return;
            if (on) { GameManager.EnableChatFocus(); game.inputSystem.Chat.Send.Disable(); }
            else { GameManager.DisableChatFocus(); game.inputSystem.Chat.Send.Enable(); }
        }

        void BuildRows()
        {
            foreach (var row in rows) Destroy(row.Rect.gameObject);
            rows.Clear();
            TooltipSystem.Hide();   // of a row button that is gone now
            var list = host.List;
            rowWidth = RemoteLayout.RowWidth(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                string name = list[i];
                var row = new Row { Name = name };
                var b = Key(name, content, new Box(0, i * (RemoteLayout.RowH + RemoteLayout.RowGap), rowWidth, RemoteLayout.RowH), Icons.NotLive, () => host.Tune(name), null, out row.Dot);
                row.Rect = (RectTransform)b.transform;
                Put(row.Dot.rectTransform, RemoteLayout.DotX, RemoteLayout.DotY, RemoteLayout.Icon, RemoteLayout.Icon);
                row.Label = Label("Label", row.Rect, Color.white, true, true);
                row.Label.text = name;
                row.Remove = Key("Remove", row.Rect, RemoteLayout.RowRemove(rowWidth), Icons.Cross, () => Remove(name), "Remove from the list", out _).gameObject;
                row.Top = Key("Top", row.Rect, RemoteLayout.RowTop(rowWidth), Icons.Top, () => host.MakeDefault(name), "Make default (first in the list)", out _).gameObject;
                row.Hot = true;
                rows.Add(row);
            }
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(rowWidth, RemoteLayout.ContentHeight(list.Count));
            Scroll(RemoteLayout.ClampScroll(offset, list.Count));
        }

        void Scroll(int to)
        {
            offset = to;
            content.anchoredPosition = new Vector2(0, offset);
            RemoteLayout.ScrollMark(offset, rows.Count, out int y, out int h);
            mark.gameObject.SetActive(h > 0);
            Put(mark, RemoteLayout.Scroll.X, RemoteLayout.Scroll.Y + y, RemoteLayout.Scroll.W, h);
        }

        // ---- behaviour ----

        public void Show()
        {
            gameObject.SetActive(true);
            EscapeMenus.Opened(this);
            Refresh();
        }

        public void Close()
        {
            gameObject.SetActive(false);
            EscapeMenus.Closed(this);
            TooltipSystem.Hide();
        }

        void OnDisable() { SetTyping(false); dragging = false; }
        void OnDestroy() { SetTyping(false); EscapeMenus.Closed(this); }

        void Flash(string text) { flash = text; flashUntil = Time.unscaledTime + FlashSeconds; }

        void Volume(int step)
        {
            host.VolumeStep(step);
            Flash("VOL " + Mathf.RoundToInt(host.Volume * 100));
        }

        void Add()
        {
            switch (host.Add(field.text))
            {
                case AddResult.Added: field.text = ""; Flash("ADDED"); Refresh(); Scroll(RemoteLayout.ScrollTo(offset, rows.Count - 1, rows.Count)); break;
                case AddResult.Invalid: Flash("BAD NAME"); break;
                case AddResult.Duplicate: Flash("ALREADY IN LIST"); break;
                case AddResult.Full: Flash("LIST FULL"); break;
            }
        }

        void Remove(string name)
        {
            if (!host.Remove(name)) Flash("LAST CHANNEL");
        }

        void Update()
        {
            // Enter and Escape end the typing but leave the field selected: let go of it, so the game has its keys back
            if (Typing && !field.isFocused && Time.frameCount > selectedAt + 2)
            {
                SetTyping(false);
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == field.gameObject) Deselect();
            }
            else if (!Typing && field.isFocused) SetTyping(true);
            Refresh();
        }

        void Refresh()
        {
            // the game's font is a distance field: labels drawn for another canvas scale stay soft after the window changed size
            float scale = GetComponent<Canvas>().rootCanvas.scaleFactor;
            if (scale != drawnAtScale)
            {
                drawnAtScale = scale;
                foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.SetAllDirty();
            }
            if (host.ListVersion != listVersion) { listVersion = host.ListVersion; shownChannel = null; BuildRows(); }
            string channel = host.Channel;
            int index = host.List.IndexOf(channel);
            if (channel != shownChannel)
            {
                shownChannel = channel;
                if (lcdName.text != channel) lcdName.text = channel;
                if (index >= 0) Scroll(RemoteLayout.ScrollTo(offset, index, rows.Count));
            }
            bool powered = host.Powered;
            lcdName.color = powered ? Glass : GlassDim;
            if (flash != null && Time.unscaledTime >= flashUntil) flash = null;
            string state = flash ?? host.State; bool muted = host.Muted; int count = host.List.Count;
            if (state != shownState || muted != shownMuted || index != shownIndex || count != shownCount)
            {
                shownState = state; shownMuted = muted; shownIndex = index; shownCount = count;
                var line = RemoteLayout.LcdLine(1);
                PutText(lcdState, state, line.X, line.Y);
                string info = (muted ? "MUTE  " : "") + (index >= 0 ? (index + 1).ToString() : "-") + "/" + count;
                PutText(lcdInfo, info, line.Right - Width(lcdInfo, info), line.Y);
                // a long message has the line to itself
                lcdInfo.enabled = lcdState.rectTransform.anchoredPosition.x + lcdState.rectTransform.sizeDelta.x + 3 <= lcdInfo.rectTransform.anchoredPosition.x;
            }
            var mute = icons[(int)(muted ? Icons.Muted : Icons.Sound)];
            if (muteIcon.sprite != mute) muteIcon.sprite = mute;

            Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1, -1);
            bool inView = RectTransformUtility.RectangleContainsScreenPoint(view, pointer, null);
            foreach (var row in rows)
            {
                bool? live = host.Live(row.Name);
                var dot = icons[(int)(live == true ? Icons.Live : Icons.NotLive)];
                if (row.Dot.sprite != dot) row.Dot.sprite = dot;
                row.Label.color = row.Name == channel ? Accent : Color.white;
                // the row's own buttons only under the pointer
                bool hot = inView && RectTransformUtility.RectangleContainsScreenPoint(row.Rect, pointer, null);
                if (hot == row.Hot) continue;
                row.Hot = hot;
                row.Remove.SetActive(hot);
                row.Top.SetActive(hot && row != rows[0]);
                int right = hot ? (row == rows[0] ? RemoteLayout.RowRemove(rowWidth).X : RemoteLayout.RowTop(rowWidth).X) - 2 : rowWidth - 3;
                Put(row.Label.rectTransform, RemoteLayout.NameX, RemoteLayout.NameY, right - RemoteLayout.NameX, RemoteLayout.TextH);
            }
            KeepInside();
        }

        // ---- pointer ----

        public void OnScroll(PointerEventData e)
        {
            if (e.scrollDelta.y == 0) return;
            Scroll(RemoteLayout.ClampScroll(offset + (e.scrollDelta.y > 0 ? -1 : 1) * (RemoteLayout.RowH + RemoteLayout.RowGap), rows.Count));
        }

        public void OnBeginDrag(PointerEventData e) => dragging = true;

        public void OnDrag(PointerEventData e) => body.anchoredPosition += e.delta / GetComponent<Canvas>().rootCanvas.scaleFactor;

        public void OnEndDrag(PointerEventData e) { dragging = false; KeepInside(); }

        // on whole pixels and on the screen
        void KeepInside()
        {
            if (dragging) return;
            var area = ((RectTransform)transform).rect.size;
            if (area != settledArea) { settledArea = area; return; }   // mid resize the canvas has its old scale for a frame
            var p = body.anchoredPosition;
            var q = new Vector2(Mathf.Clamp(Mathf.Round(p.x), Mathf.Min(0, RemoteLayout.W - area.x), 0), Mathf.Clamp(Mathf.Round(p.y), Mathf.Min(0, RemoteLayout.H - area.y), 0));
            if (q != p) body.anchoredPosition = q;
        }
    }

    // Which channels of the list are live: one request for all of them, while the remote is open.
    public sealed class LiveLookup
    {
        public const double Every = 60, AfterChange = 10;
        volatile Dictionary<string, bool> known = new Dictionary<string, bool>();
        volatile bool busy;
        double nextAt, lastAt = double.NegativeInfinity; string asked;

        public bool? Live(string channel) => known.TryGetValue(channel, out bool live) ? live : (bool?)null;

        public void Tick(double now, IList<string> list)
        {
            if (busy) return;
            string key = string.Join(",", list);
            if (now < nextAt && (key == asked || now < lastAt + AfterChange)) return;
            asked = key; lastAt = now; nextAt = now + Every; busy = true;
            string body = Twitch.LiveRequest(list);
            new Thread(() =>
            {
                try
                {
                    var answer = Twitch.ParseLive(Stream.Http("POST", Twitch.GqlUrl, body, out int status));
                    if (status == 200 && answer != null) known = answer;
                }
                catch (Exception) { }   // asked again in a minute
                finally { busy = false; }
            }) { IsBackground = true, Name = "billboard-live" }.Start();
        }
    }
}
