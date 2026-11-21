using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ReworkedCopium;
using ShorelandsBillboard.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ShorelandsBillboard
{
    // What the billboard takes from the town it was made for. Found only when the hedge it stands in is
    // exactly where it stands in game 0.69g; nothing is built or changed otherwise.
    public sealed class Town
    {
        public Material Lit; public int SortingLayer, ObstacleLayer;

        // plain scenery (sprite, sorter, collider) under a parent named Bushes
        public static bool IsBush(SpriteRenderer r)
        {
            var parent = r.transform.parent;
            if (parent == null || parent.name != "Bushes" || !r.enabled || r.transform.childCount != 0) return false;
            return r.GetComponents<Component>().All(x => x is Transform || x is SpriteRenderer || x is YPositionSorter || x is BoxCollider2D);
        }

        public static Town Find()
        {
            var bushes = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Where(IsBush).ToList();
            if (!Layout.IsHedge(bushes.Select(b => (b.bounds.center.x, b.bounds.center.y)))) return null;
            var bush = bushes.FirstOrDefault(b => Mathf.Abs(b.bounds.center.y - Layout.HedgeY) < 0.1f && Mathf.Abs(b.bounds.center.x - Layout.HedgeFirstX) < 0.1f
                && b.sharedMaterial != null && b.GetComponent<BoxCollider2D>() != null);
            if (bush == null) return null;
            return new Town { Lit = bush.sharedMaterial, SortingLayer = bush.sortingLayerID, ObstacleLayer = bush.gameObject.layer };
        }
    }

    // The billboard as scene objects: the plugin's own frame art (art/make_billboard.py), a base collider on
    // the game's obstacle layer, and lights and a ground shadow cloned from the game's lanterns and trees.
    public sealed class World
    {
        const float GlowAtNight = 0.6f;

        public GameObject Root { get; private set; }
        public AudioSource Audio { get; private set; }
        public Camera UiCamera { get; private set; }   // draws the sharp picture; null: the picture is a world sprite
        public readonly List<string> Missing = new List<string>();   // optional parts this town did not offer
        readonly Town town;
        SpriteRenderer picture;
        TextMeshPro label;
        BoxCollider2D wall;
        Light2D glow;
        Texture2D texture; string shownStatus = "";
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<Behaviour> hiddenColliders = new List<Behaviour>();
        readonly List<Renderer> hiddenRenderers = new List<Renderer>();

        public Vector2 ScreenCentre
        {
            get { var r = Layout.Screen; return (Vector2)Root.transform.position + new Vector2(r.X + r.W / 2, r.Y + r.H / 2); }
        }

        public bool Sharp => UiCamera != null;
        public bool Blocking => wall.enabled;
        public int HiddenDecor => hiddenRenderers.Count;

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

        Sprite Whole(Texture2D t, Vector2 pivot)
        {
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, Layout.Ppu, 0, SpriteMeshType.FullRect);
            owned.Add(s); return s;
        }

        SpriteRenderer Part(string name, Sprite sprite, Layout.Rect at, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = new Vector3(at.X, at.Y, 0);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite; r.sharedMaterial = mat;
            r.sortingLayerID = town.SortingLayer; r.sortingOrder = order;
            return r;
        }

        // The plate cut to the name's length: the left end and middle of plate.png, its right end, the letters.
        Texture2D NamePlate(string text)
        {
            Texture2D plate = Art("plate.png"), font = Art("font.png");
            int w = Layout.PlateWidth(text.Length), h = Layout.PlateH;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(0, 0, w - 2, h, plate.GetPixels(0, 0, w - 2, h));
            t.SetPixels(w - 2, 0, 2, h, plate.GetPixels(plate.width - 2, 0, 2, h));
            for (int i = 0; i < text.Length; i++)
            {
                int g = Layout.Glyphs.IndexOf(text[i]);
                for (int y = 0; y < Layout.GlyphH; y++)
                    for (int x = 0; x < Layout.GlyphW - 1; x++)
                    {
                        var c = font.GetPixel(g * Layout.GlyphW + x, y);
                        if (c.a > 0.5f) t.SetPixel(Layout.PlatePad + i * Layout.GlyphW + x, (h - Layout.GlyphH) / 2 + y, c);
                    }
            }
            t.Apply();
            owned.Add(t); return t;
        }

        // The game's overlay camera for name tags and speech bubbles, if it is there, draws one layer and
        // maps the world as the main camera does. In a window that is not 16:9 the main camera letterboxes
        // and the overlay does not: what it draws is squeezed sideways.
        public static Camera FindUiCamera(out int layer)
        {
            layer = -1;
            var cam = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.name == "WorldUI_Camera");
            var main = Camera.main;
            if (cam == null || main == null || !cam.orthographic || Mathf.Abs(cam.aspect / main.aspect - 1f) > 0.01f) return null;
            int mask = cam.cullingMask;
            if (mask == 0 || (mask & (mask - 1)) != 0) return null;
            while ((mask & 1) == 0) { mask = (int)((uint)mask >> 1); layer++; }
            layer++;
            return cam;
        }

        public World(Town town, Vector2 origin, bool sharp, string channel)
        {
            this.town = town;
            try { Build(origin, sharp, channel); }
            catch { Destroy(); throw; }
        }

        void Build(Vector2 origin, bool sharp, string channel)
        {
            var lit = town.Lit;
            var unlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            if (unlitShader == null) throw new Exception("no unlit sprite shader");
            var unlit = new Material(unlitShader); owned.Add(unlit);

            Root = new GameObject("TwitchBillboard");
            Root.transform.position = new Vector3(Layout.Snap(origin.x), Layout.Snap(origin.y), 0);
            int order = Layout.OrderForY(Root.transform.position.y);

            Part("Frame", Whole(Art("frame.png"), Vector2.zero), Layout.Frame, lit, order);
            string name = Layout.PlateText(channel);
            if (name.Length > 0) Part("NamePlate", Whole(NamePlate(name), Vector2.zero), Layout.Plate(name.Length), lit, order + 1);

            var s = Layout.Screen;
            picture = Part("Screen", null, s, unlit, order + 2);
            picture.enabled = false;

            var text = new GameObject("Status");
            text.transform.SetParent(Root.transform, false);
            text.transform.localPosition = new Vector3(s.X + s.W / 2, s.Y + s.H / 2, 0);
            text.transform.localScale = Vector3.one * 10f / Layout.Ppu;   // world text: font size 10 = 1 unit, so size 9 = 9 art px
            label = text.AddComponent<TextMeshPro>();
            var font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == "VioPixelText SDF");
            if (font != null) label.font = font;
            label.rectTransform.sizeDelta = new Vector2(Layout.ScreenW, Layout.ScreenH) / 10f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.fontSize = 18; label.lineSpacing = 20;
            label.color = new Color(0.78f, 0.82f, 0.9f);
            label.sortingLayerID = town.SortingLayer; label.sortingOrder = order + 3;

            // Drawn after the picture by the same camera, so its ring always covers the picture's edge.
            var bezel = Part("Bezel", Whole(Art("bezel.png"), Vector2.zero), Layout.Frame, unlit, order + 4);

            // sharp: the picture, its text and the bezel go through the game's full-resolution world camera
            // (name tags, speech bubbles) instead of the 640x360 one
            if (sharp)
            {
                UiCamera = FindUiCamera(out int uiLayer);
                if (UiCamera == null) Missing.Add("the sharp picture (the window is not 16:9, or the game's world UI camera is not as known)");
                else
                    foreach (var go in new[] { picture.gameObject, text, bezel.gameObject })
                        go.layer = uiLayer;
            }

            var c = Layout.Collider;
            var collision = new GameObject("Collision") { layer = town.ObstacleLayer };
            collision.transform.SetParent(Root.transform, false);
            wall = collision.AddComponent<BoxCollider2D>();
            wall.offset = new Vector2(c.X + c.W / 2, c.Y + c.H / 2); wall.size = new Vector2(c.W, c.H);
            wall.enabled = false;   // Stand() switches it on once the player is clear of it

            HideDecorUnderBase();
            Optional("the ground shadow", AddShadow);
            Optional("the lanterns", () => AddLanterns(order + 1));
            Optional("the screen's glow", AddGlow);

            Audio = Root.AddComponent<AudioSource>();
            Audio.playOnAwake = false; Audio.loop = true; Audio.spatialBlend = 0; Audio.volume = 0;
        }

        // Parts copied from the game's own objects: left out when the game no longer has them as known.
        void Optional(string what, Func<bool> add)
        {
            try { if (add()) return; } catch (Exception) { }
            Missing.Add(what);
        }

        static bool Only(GameObject go, params Type[] components) =>
            go.transform.childCount == 0 && go.GetComponents<Component>().All(x => x is Transform || components.Contains(x.GetType()));

        // The hedge bushes the base stands on.
        void HideDecorUnderBase()
        {
            Vector2 at = Root.transform.position;
            foreach (var r in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (!Town.IsBush(r)) continue;
                var b = r.bounds;
                if (!Layout.HidesDecor(at.x, at.y, b.center.x, b.center.y)) continue;
                r.enabled = false; hiddenRenderers.Add(r);
                foreach (var col in r.GetComponents<Collider2D>().Where(x => x.enabled)) { col.enabled = false; hiddenColliders.Add(col); }
            }
        }

        // Trees darken the ground with a sprite light named ShadowGround; the billboard gets a copy with its
        // own shape.
        bool AddShadow()
        {
            var template = UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None)
                .FirstOrDefault(l => l.lightType == Light2D.LightType.Sprite && l.name == "ShadowGround" && Only(l.gameObject, typeof(Light2D)));
            var cookie = typeof(Light2D).GetField("m_LightCookieSprite", BindingFlags.Instance | BindingFlags.NonPublic);
            if (template == null || cookie == null) return false;
            var go = UnityEngine.Object.Instantiate(template.gameObject, Root.transform);
            go.name = "ShadowGround";
            var at = Layout.Shadow;
            go.transform.localPosition = new Vector3(at.X + at.W / 2, at.Y + at.H / 2, 0);
            go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            cookie.SetValue(go.GetComponent<Light2D>(), Whole(Art("shadow.png"), new Vector2(0.5f, 0.5f)));
            return true;
        }

        // The game's lantern (tree house: lamp2 sprite, a point light and the LightHandler that turns it up
        // at night). The tree house may be unspawned and its lights mid-blink, so the copy is switched on.
        static GameObject LanternCopy(Transform parent, string name, float x, float y)
        {
            var template = UnityEngine.Object.FindObjectsByType<LightHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(h => h.GetComponent<SpriteRenderer>()?.sprite?.name == "lamp2" && h.GetComponent<Light2D>() != null
                    && Only(h.gameObject, typeof(LightHandler), typeof(Light2D), typeof(SpriteRenderer)));
            if (template == null) return null;
            var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = name;
            go.transform.localPosition = new Vector3(x, y, 0);
            go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            go.SetActive(true);
            go.GetComponent<Light2D>().enabled = true;
            return go;
        }

        bool AddLanterns(int order)
        {
            foreach (bool right in new[] { false, true })
            {
                var (x, y) = Layout.Lantern(right);
                var go = LanternCopy(Root.transform, "Lantern", x, y);
                if (go == null) return false;
                var r = go.GetComponent<SpriteRenderer>();
                r.enabled = true; r.sortingLayerID = town.SortingLayer; r.sortingOrder = order;
            }
            return true;
        }

        bool AddGlow() { glow = AddGlowLight(); return glow != null; }

        // The screen's light on its surroundings at night: a lantern's light without the lantern.
        Light2D AddGlowLight()
        {
            var s = Layout.Screen;
            var go = LanternCopy(Root.transform, "ScreenGlow", s.X + s.W / 2, s.Y + s.H * 0.35f);
            if (go == null) return null;
            UnityEngine.Object.Destroy(go.GetComponent<LightHandler>());
            UnityEngine.Object.Destroy(go.GetComponent<SpriteRenderer>());
            var l = go.GetComponent<Light2D>();
            l.color = new Color(0.62f, 0.76f, 1f); l.intensity = 0;
            l.pointLightInnerRadius = 1.5f; l.pointLightOuterRadius = 5f;
            return l;
        }

        // Called every frame with the local player's body box: the base only starts blocking once the player
        // is clear of it, so a billboard that appears on top of a player never traps them.
        public void Stand(Bounds body)
        {
            if (wall.enabled) return;
            var c = Layout.Collider.At(Root.transform.position.x, Root.transform.position.y);
            var p = new Layout.Rect { X = body.min.x, Y = body.min.y, W = body.size.x, H = body.size.y };
            if (!c.Overlaps(p)) wall.enabled = true;
        }

        // null shows the picture
        public void SetStatus(string status, string channel)
        {
            picture.enabled = status == null && texture != null;
            label.enabled = status != null;
            if (glow != null)
            {
                var day = NetworkDayNight.Instance;
                glow.intensity = picture.enabled && day != null ? GlowAtNight * Mathf.Clamp01(1f - day.currentIntensity) : 0f;
            }
            if (status == shownStatus) return;
            shownStatus = status;
            if (status != null) label.text = status + "\n" + ChannelLine(Plain(channel));
        }

        // The channel under the status: with its address if that fits the screen, else the name, cut if it must be.
        string ChannelLine(string name)
        {
            const float room = Layout.ScreenW - 8;
            string Line(string text) => "<size=9><noparse>" + text + "</noparse></size>";
            bool Fits(string text) => label.GetPreferredValues(Line(text)).x * 10f <= room;
            if (Fits("twitch.tv/" + name)) return Line("twitch.tv/" + name);
            while (name.Length > 1 && !Fits(name)) name = name.Substring(0, name.Length - 1);
            return Line(name);
        }

        // a config value as text on the screen: no markup, no more than fits
        static string Plain(string channel)
        {
            string t = (channel ?? "").Trim().Replace("<", "").Replace(">", "");
            return t.Length > Layout.MaxName ? t.Substring(0, Layout.MaxName) : t;
        }

        // topDown: the first row of the data is the picture's top row
        public void SetPictureSize(int w, int h, bool smooth, bool rgba, bool topDown)
        {
            var format = rgba ? TextureFormat.RGBA32 : TextureFormat.RGB24;
            if (texture != null && texture.width == w && texture.height == h && texture.format == format && picture.flipY == topDown) return;
            if (texture != null) { UnityEngine.Object.Destroy(picture.sprite); UnityEngine.Object.Destroy(texture); }
            texture = new Texture2D(w, h, format, false) { filterMode = smooth ? FilterMode.Bilinear : FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            // flipped about a pivot at the top-left it covers the same place
            picture.sprite = Sprite.Create(texture, new Rect(0, 0, w, h), topDown ? Vector2.up : Vector2.zero, w / Layout.Screen.W);
            picture.flipY = topDown;
        }

        public void Show(byte[] pixels)
        {
            texture.LoadRawTextureData(pixels);
            texture.Apply(false);
        }

        // Also undoes a half-built billboard.
        public void Destroy()
        {
            foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (var c in hiddenColliders) if (c != null) c.enabled = true;
            hiddenRenderers.Clear(); hiddenColliders.Clear();
            if (texture != null) { if (picture != null) UnityEngine.Object.Destroy(picture.sprite); UnityEngine.Object.Destroy(texture); texture = null; }
            foreach (var o in owned) if (o != null) UnityEngine.Object.Destroy(o);
            owned.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
    }
}
