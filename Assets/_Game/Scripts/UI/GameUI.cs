using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PulyaDura
{
    /// <summary>Весь интерфейс: гараж, HUD, баннеры, всплывающий текст, пауза, итоги.</summary>
    public class GameUI : MonoBehaviour
    {
        static readonly Vector2 TL = new(0f, 1f), TR = new(1f, 1f), TC = new(0.5f, 1f);
        static readonly Vector2 BL = Vector2.zero, BC = new(0.5f, 0f), C = new(0.5f, 0.5f);

        GameManager gm;
        GameConfig cfg;

        RectTransform garageRoot, hudRoot, pauseRoot, resultRoot, floatLayer, controlsRow;

        // гараж
        readonly List<Image> hullButtons = new();
        readonly List<Image> turretButtons = new();
        Text hullInfo, turretInfo, recordText;
        Image statArmor, statSpeed, statDamage, statRate, statBounces;

        // HUD
        Text roomNumber, roomTitle, hint, timer, weaponLabel, abilityLabel, noiseLabel;
        Image weaponBar, abilityFill, abilityBox, damageFlash;
        readonly List<Image> lifeIcons = new();
        readonly List<Image> hpSegments = new();
        RectTransform hpRow;
        int hpSegmentsFor = -1;
        float hintTime;
        Tank cachedPlayer;
        PlayerController playerController;

        // баннер и итоги
        RectTransform bannerRoot;
        Text bannerTitle, bannerSub;
        float bannerTime, bannerLife;
        Text resultTitle, resultTime, resultKills, resultRicochets, resultExtra, resultRecord;

        class Floater
        {
            public Text text;
            public Vector3 world;
            public float t;
        }
        readonly List<Floater> floaters = new();

        public void Build(GameManager manager)
        {
            gm = manager;
            cfg = manager.config;
            var canvas = UiKit.Canvas("UI", 10).transform;

            hudRoot = UiKit.Stretch("Hud", canvas);
            floatLayer = UiKit.Stretch("Floaters", canvas);
            BuildHud();
            BuildBanner(canvas);
            garageRoot = UiKit.Stretch("Garage", canvas);
            BuildGarage();
            pauseRoot = UiKit.Stretch("Pause", canvas);
            BuildPause();
            resultRoot = UiKit.Stretch("Result", canvas);
            BuildResult();
        }

        // ---------- Гараж ----------

        void BuildGarage()
        {
            var title = UiKit.Text("Title", garageRoot, TL, TL, new Vector2(96f, -84f), new Vector2(900f, 120f), "ПУЛЯ — ДУРА", 108, UiKit.Accent, TextAnchor.UpperLeft, Weight.Black);
            UiKit.Shadow(title, 4f, 0.6f);
            UiKit.Text("Tagline", garageRoot, TL, TL, new Vector2(100f, -206f), new Vector2(800f, 50f), "Стреляй, чтобы увидеть.", 36, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Medium);

            string[] rules =
            {
                "Стены невидимы. Отскок их подсвечивает",
                "После отскока пуля бьёт и тебя",
                "Враги слышат каждый выстрел",
            };
            Color[] marks = { UiKit.Accent, UiKit.Danger, UiKit.Enemy };
            for (int i = 0; i < rules.Length; i++)
            {
                float y = -310f - i * 60f;
                UiKit.Box("Mark", garageRoot, TL, new Vector2(0f, 0.5f), new Vector2(102f, y - 18f), new Vector2(12f, 12f), marks[i]);
                UiKit.Text("Rule", garageRoot, TL, new Vector2(0f, 0.5f), new Vector2(132f, y - 18f), new Vector2(700f, 44f), rules[i], 29, UiKit.TextColor, TextAnchor.MiddleLeft, Weight.Medium);
            }

            recordText = UiKit.Text("Record", garageRoot, BL, BL, new Vector2(100f, 130f), new Vector2(700f, 36f), "", 24, UiKit.Muted, TextAnchor.LowerLeft, Weight.Medium);

            // Карточка выбора справа
            var card = UiKit.Box("Card", garageRoot, TR, TR, new Vector2(-96f, -84f), new Vector2(600f, 760f), UiKit.Panel);
            UiKit.Border(card, UiKit.Line);
            var ct = card.transform;
            const float pad = 32f;
            float inner = 600f - pad * 2f;

            UiKit.Text("HullHead", ct, TL, TL, new Vector2(pad, -28f), new Vector2(inner, 26f), "КОРПУС", 20, UiKit.Muted, TextAnchor.UpperLeft, Weight.Bold);
            float hw = (inner - (cfg.hulls.Count - 1) * 10f) / cfg.hulls.Count;
            for (int i = 0; i < cfg.hulls.Count; i++)
            {
                int index = i;
                var b = UiKit.Button("Hull" + i, ct, TL, TL, new Vector2(pad + i * (hw + 10f), -62f), new Vector2(hw, 60f), cfg.hulls[i].title, 26, () => gm.SelectHull(index), out _);
                hullButtons.Add(b.GetComponent<Image>());
            }
            hullInfo = UiKit.Text("HullInfo", ct, TL, TL, new Vector2(pad, -138f), new Vector2(inner, 36f), "", 25, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Medium);
            statArmor = Stat(ct, "Броня", -192f, pad, inner);
            statSpeed = Stat(ct, "Скорость", -222f, pad, inner);

            UiKit.Box("Divider", ct, TL, TL, new Vector2(pad, -262f), new Vector2(inner, 1f), UiKit.Line);

            UiKit.Text("TurretHead", ct, TL, TL, new Vector2(pad, -288f), new Vector2(inner, 26f), "ПУШКА", 20, UiKit.Muted, TextAnchor.UpperLeft, Weight.Bold);
            float tw = (inner - (cfg.turrets.Count - 1) * 10f) / cfg.turrets.Count;
            for (int i = 0; i < cfg.turrets.Count; i++)
            {
                int index = i;
                var b = UiKit.Button("Turret" + i, ct, TL, TL, new Vector2(pad + i * (tw + 10f), -322f), new Vector2(tw, 60f), cfg.turrets[i].title, 21, () => gm.SelectTurret(index), out _);
                turretButtons.Add(b.GetComponent<Image>());
            }
            turretInfo = UiKit.Text("TurretInfo", ct, TL, TL, new Vector2(pad, -398f), new Vector2(inner, 36f), "", 25, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Medium);
            statDamage = Stat(ct, "Урон", -452f, pad, inner);
            statRate = Stat(ct, "Темп", -482f, pad, inner);
            statBounces = Stat(ct, "Отскоки", -512f, pad, inner);

            var start = UiKit.Button("Start", ct, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, pad), new Vector2(inner, 112f),
                "В БОЙ", 48, () => gm.StartRun(), out _, Weight.Black);
            var startImg = start.GetComponent<Image>();
            startImg.color = new Color(0.12f, 0.45f, 0.52f, 1f);
            UiKit.Text("Enter", startImg.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(120f, 30f), "Enter", 20, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleRight, Weight.Medium);

            controlsRow = UiKit.KeyHints("Controls", garageRoot, BC, BC, new Vector2(0f, 40f), new[]
            {
                ("WASD", "ехать"), ("Shift", "тихо"), ("Мышь", "целиться"), ("ЛКМ", "огонь"), ("Пробел", "скилл"),
            }, 22);
        }

        Image Stat(Transform parent, string name, float y, float pad, float inner)
        {
            UiKit.Text(name, parent, TL, new Vector2(0f, 0.5f), new Vector2(pad, y), new Vector2(150f, 26f), name, 20, UiKit.Muted, TextAnchor.MiddleLeft, Weight.Medium);
            return UiKit.Bar(name + "Bar", parent, TL, new Vector2(0f, 0.5f), new Vector2(pad + 150f, y), new Vector2(inner - 150f, 6f), UiKit.Accent);
        }

        public void RefreshGarage()
        {
            var h = cfg.hulls[gm.HullIndex];
            var t = cfg.turrets[gm.TurretIndex];
            Highlight(hullButtons, gm.HullIndex);
            Highlight(turretButtons, gm.TurretIndex);

            hullInfo.text = $"<color=#FF8F38>{h.abilityTitle}</color> · {h.abilityDescription}";
            turretInfo.text = t.description;
            statArmor.fillAmount = h.maxHp / 8f;
            statSpeed.fillAmount = h.speed / 6.6f;
            statDamage.fillAmount = Mathf.Clamp01(t.damage * t.pellets / 6f);
            statRate.fillAmount = Mathf.Clamp01(Mathf.Sqrt(0.17f / (t.reload + t.chargeTime)));
            statBounces.fillAmount = t.bounces / 3f;

            float bestTime = PlayerPrefs.GetFloat("best_time", 0f);
            int bestRooms = PlayerPrefs.GetInt("best_rooms", 0);
            recordText.text = bestTime > 0f ? $"Рекорд: {FormatTime(bestTime)}"
                : bestRooms > 0 ? $"Рекорд: {bestRooms} из {gm.RoomCount} комнат" : "";
        }

        static void Highlight(List<Image> buttons, int selected)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool on = i == selected;
                buttons[i].color = on ? new Color(0.13f, 0.4f, 0.47f, 1f) : new Color(1f, 1f, 1f, 0.04f);
                buttons[i].GetComponent<Outline>().effectColor = on ? UiKit.Accent : UiKit.Line;
            }
        }

        public void ShowGarage()
        {
            garageRoot.gameObject.SetActive(true);
            hudRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(false);
            pauseRoot.gameObject.SetActive(false);
            bannerRoot.gameObject.SetActive(false);
            ClearFloaters();
            RefreshGarage();
        }

        // ---------- HUD ----------

        void BuildHud()
        {
            damageFlash = UiKit.Fill(UiKit.Stretch("DamageFlash", hudRoot), new Color(1f, 0.2f, 0.05f, 0f));

            roomNumber = UiKit.Text("RoomNumber", hudRoot, TL, TL, new Vector2(48f, -36f), new Vector2(600f, 26f), "", 20, UiKit.Muted, TextAnchor.UpperLeft, Weight.Bold);
            roomTitle = UiKit.Text("RoomTitle", hudRoot, TL, TL, new Vector2(48f, -62f), new Vector2(800f, 50f), "", 40, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Black);
            UiKit.Shadow(roomTitle);
            hint = UiKit.Text("Hint", hudRoot, TC, TC, new Vector2(0f, -44f), new Vector2(1000f, 50f), "", 30, UiKit.Accent, TextAnchor.UpperCenter, Weight.Medium);
            UiKit.Shadow(hint);
            timer = UiKit.Text("Timer", hudRoot, TR, TR, new Vector2(-48f, -36f), new Vector2(300f, 50f), "", 40, UiKit.TextColor, TextAnchor.UpperRight, Weight.Bold);

            // Нижний левый блок: жизни, броня, пушка, скилл
            for (int i = 0; i < cfg.lives; i++)
            {
                var icon = UiKit.Box("Life", hudRoot, BL, BL, new Vector2(48f + i * 30f, 216f), new Vector2(20f, 20f), UiKit.Danger);
                icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                lifeIcons.Add(icon);
            }
            noiseLabel = UiKit.Text("Noise", hudRoot, BL, BL, new Vector2(48f + cfg.lives * 30f + 20f, 210f), new Vector2(240f, 30f), "", 20, UiKit.Muted, TextAnchor.LowerLeft, Weight.Bold);
            hpRow = UiKit.Rect("Hp", hudRoot, BL, BL, new Vector2(48f, 172f), new Vector2(320f, 16f));
            weaponLabel = UiKit.Text("Weapon", hudRoot, BL, BL, new Vector2(48f, 126f), new Vector2(320f, 26f), "", 20, UiKit.Muted, TextAnchor.LowerLeft, Weight.Bold);
            weaponBar = UiKit.Bar("Reload", hudRoot, BL, BL, new Vector2(48f, 114f), new Vector2(320f, 5f), UiKit.TextColor);

            abilityBox = UiKit.Box("Ability", hudRoot, BL, BL, new Vector2(48f, 48f), new Vector2(320f, 44f), new Color(1f, 1f, 1f, 0.05f));
            UiKit.Border(abilityBox, UiKit.Line);
            abilityFill = UiKit.Fill(UiKit.Stretch("Fill", abilityBox.transform), UiKit.AccentDim);
            abilityFill.type = Image.Type.Filled;
            abilityFill.fillMethod = Image.FillMethod.Horizontal;
            abilityFill.sprite = UiKit.WhiteSprite;
            var at = UiKit.Stretch("Text", abilityBox.transform);
            at.offsetMin = new Vector2(16f, 0f);
            abilityLabel = UiKit.Label(at, "", 20, UiKit.TextColor, TextAnchor.MiddleLeft, Weight.Bold);
        }

        void RebuildHpSegments(int count)
        {
            foreach (var s in hpSegments) Destroy(s.gameObject);
            hpSegments.Clear();
            float w = (320f - (count - 1) * 4f) / count;
            for (int i = 0; i < count; i++)
                hpSegments.Add(UiKit.Box("Seg", hpRow, BL, BL, new Vector2(i * (w + 4f), 0f), new Vector2(w, 16f), UiKit.Accent));
            hpSegmentsFor = count;
        }

        public void ShowHud()
        {
            garageRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(false);
            hudRoot.gameObject.SetActive(true);
            bannerRoot.gameObject.SetActive(false);
            ClearFloaters();
        }

        public void ShowRoom(int index, int count, string title, string roomHint)
        {
            roomNumber.text = $"КОМНАТА {index + 1} / {count}";
            roomTitle.text = title;
            hint.text = roomHint;
            hintTime = Time.unscaledTime;
        }

        void UpdateHud()
        {
            timer.text = FormatTime(gm.Stats.time);
            for (int i = 0; i < lifeIcons.Count; i++)
                lifeIcons[i].color = i < gm.Lives ? UiKit.Danger : UiKit.Faint;

            float age = Time.unscaledTime - hintTime;
            var hc = hint.color;
            hc.a = age < 6f ? 1f : Mathf.Clamp01(1f - (age - 6f));
            hint.color = hc;

            var p = gm.Player;
            if (p != null && !p.IsDead)
            {
                if (hpSegmentsFor != p.MaxHp) RebuildHpSegments(p.MaxHp);
                for (int i = 0; i < hpSegments.Count; i++)
                    hpSegments[i].color = i < p.Hp ? (p.Hp <= 1 ? UiKit.Danger : UiKit.Accent) : UiKit.Faint;

                weaponLabel.text = p.Weapon.Charging ? $"{p.Turret.title.ToUpper()} · ЗАРЯД" : p.Turret.title.ToUpper();
                weaponBar.fillAmount = p.Weapon.Charging ? p.Weapon.Charge01 : p.Weapon.Reload01;

                bool ready = p.Ability.Ready;
                abilityFill.fillAmount = p.Ability.Cooldown01;
                abilityFill.color = ready ? new Color(0.36f, 0.88f, 1f, 0.28f) : UiKit.AccentDim;
                abilityLabel.text = $"<color=#{(ready ? "FFFFFF" : "8A96A6")}>ПРОБЕЛ</color>   {p.Ability.Title.ToUpper()}";
                abilityLabel.color = ready ? UiKit.TextColor : UiKit.Muted;
                abilityBox.GetComponent<Outline>().effectColor = ready ? UiKit.Accent : UiKit.Line;

                if (cachedPlayer != p) { cachedPlayer = p; playerController = p.GetComponent<PlayerController>(); }
                bool loud = playerController != null && playerController.IsLoud;
                bool sneak = playerController != null && playerController.IsSneaking;
                noiseLabel.text = loud ? "ШУМНО" : sneak ? "ТИХИЙ ХОД" : "ТИХО";
                noiseLabel.color = loud ? UiKit.Danger : sneak ? UiKit.Accent : UiKit.Muted;
            }

            var fc = damageFlash.color;
            fc.a = Mathf.Max(0f, fc.a - Time.unscaledDeltaTime * 0.6f);
            damageFlash.color = fc;
        }

        public void DamageFlash()
        {
            var c = damageFlash.color;
            c.a = 0.12f;
            damageFlash.color = c;
        }

        // ---------- Баннер и всплывающий текст ----------

        void BuildBanner(Transform canvas)
        {
            bannerRoot = UiKit.Rect("Banner", canvas, C, C, new Vector2(0f, 150f), new Vector2(1400f, 220f));
            bannerTitle = UiKit.Text("Title", bannerRoot, C, C, new Vector2(0f, 30f), new Vector2(1400f, 140f), "", 112, UiKit.Accent, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Shadow(bannerTitle, 5f, 0.6f);
            bannerSub = UiKit.Text("Sub", bannerRoot, C, C, new Vector2(0f, -62f), new Vector2(1400f, 50f), "", 32, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Medium);
            UiKit.Shadow(bannerSub);
            bannerRoot.gameObject.SetActive(false);
        }

        public void Banner(string title, string sub, Color color, float life = 1.8f)
        {
            bannerRoot.gameObject.SetActive(true);
            bannerTitle.text = title;
            bannerTitle.color = color;
            bannerSub.text = sub;
            bannerTime = Time.unscaledTime;
            bannerLife = life;
        }

        void UpdateBanner()
        {
            if (!bannerRoot.gameObject.activeSelf) return;
            float t = Time.unscaledTime - bannerTime;
            if (t > bannerLife) { bannerRoot.gameObject.SetActive(false); return; }
            float pop = t < 0.15f ? Mathf.Lerp(1.5f, 0.96f, t / 0.15f) : t < 0.25f ? Mathf.Lerp(0.96f, 1f, (t - 0.15f) / 0.1f) : 1f;
            bannerRoot.localScale = Vector3.one * pop;
            float a = Mathf.Clamp01((bannerLife - t) / 0.3f);
            SetAlpha(bannerTitle, a);
            SetAlpha(bannerSub, a);
        }

        public void Floating(Vector3 world, string text, Color color, int size)
        {
            Floater f = null;
            foreach (var x in floaters)
                if (!x.text.gameObject.activeSelf) { f = x; break; }
            if (f == null)
            {
                var t = UiKit.Text("Float", floatLayer, C, C, Vector2.zero, new Vector2(700f, 80f), "", size, color, TextAnchor.MiddleCenter, Weight.Black);
                UiKit.Shadow(t, 3f);
                f = new Floater { text = t };
                floaters.Add(f);
            }
            f.text.gameObject.SetActive(true);
            f.text.text = text;
            f.text.fontSize = size;
            f.text.color = color;
            f.world = world;
            f.t = 0f;
            PlaceFloater(f);
        }

        void PlaceFloater(Floater f)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(f.world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(floatLayer, screen, null, out var local);
            var rt = f.text.rectTransform;
            rt.anchoredPosition = local + new Vector2(0f, f.t * 80f);
            float pop = f.t < 0.1f ? Mathf.Lerp(1.4f, 1f, f.t / 0.1f) : 1f;
            rt.localScale = Vector3.one * pop;
        }

        void UpdateFloaters()
        {
            foreach (var f in floaters)
            {
                if (!f.text.gameObject.activeSelf) continue;
                f.t += Time.unscaledDeltaTime;
                if (f.t > 1.1f) { f.text.gameObject.SetActive(false); continue; }
                PlaceFloater(f);
                SetAlpha(f.text, Mathf.Clamp01((1.1f - f.t) / 0.35f));
            }
        }

        void ClearFloaters()
        {
            foreach (var f in floaters) f.text.gameObject.SetActive(false);
        }

        // ---------- Пауза и итоги ----------

        void BuildPause()
        {
            UiKit.Fill(pauseRoot, new Color(0.01f, 0.015f, 0.025f, 0.78f)).raycastTarget = true;
            UiKit.Text("Title", pauseRoot, C, C, new Vector2(0f, 180f), new Vector2(800f, 130f), "ПАУЗА", 104, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Button("Resume", pauseRoot, C, C, new Vector2(0f, 40f), new Vector2(440f, 80f), "Продолжить", 32, () => gm.TogglePause(), out _);
            UiKit.Button("Restart", pauseRoot, C, C, new Vector2(0f, -56f), new Vector2(440f, 80f), "Заново", 32, () => gm.RestartRun(), out _);
            UiKit.Button("Garage", pauseRoot, C, C, new Vector2(0f, -152f), new Vector2(440f, 80f), "В гараж", 32, () => gm.EnterGarage(), out _);
            pauseRoot.gameObject.SetActive(false);
        }

        public void ShowPause(bool on) => pauseRoot.gameObject.SetActive(on);

        void BuildResult()
        {
            UiKit.Fill(resultRoot, new Color(0.01f, 0.015f, 0.025f, 0.86f)).raycastTarget = true;
            resultTitle = UiKit.Text("Title", resultRoot, C, C, new Vector2(0f, 290f), new Vector2(1400f, 140f), "", 120, UiKit.Accent, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Shadow(resultTitle, 5f, 0.6f);

            resultTime = Tile("Время", -300f);
            resultKills = Tile("Подбито", 0f);
            resultRicochets = Tile("Рикошетом", 300f);
            resultExtra = UiKit.Text("Extra", resultRoot, C, C, new Vector2(0f, -40f), new Vector2(1000f, 40f), "", 26, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Medium);
            resultRecord = UiKit.Text("Record", resultRoot, C, C, new Vector2(0f, -100f), new Vector2(1000f, 50f), "", 32, UiKit.Danger, TextAnchor.MiddleCenter, Weight.Bold);

            UiKit.Button("Again", resultRoot, C, C, new Vector2(-240f, -230f), new Vector2(440f, 92f), "Ещё раз", 36, () => gm.RestartRun(), out _, Weight.Black);
            UiKit.Button("Garage", resultRoot, C, C, new Vector2(240f, -230f), new Vector2(440f, 92f), "В гараж", 36, () => gm.EnterGarage(), out _);
            UiKit.KeyHints("Keys", resultRoot, C, new Vector2(0.5f, 0.5f), new Vector2(0f, -320f), new[] { ("R", "ещё раз"), ("G", "в гараж") }, 22);
            resultRoot.gameObject.SetActive(false);
        }

        Text Tile(string label, float x)
        {
            var box = UiKit.Box("Tile", resultRoot, C, C, new Vector2(x, 110f), new Vector2(270f, 150f), UiKit.Panel);
            UiKit.Border(box, UiKit.Line);
            var value = UiKit.Text("Value", box.transform, C, C, new Vector2(0f, 16f), new Vector2(260f, 80f), "", 60, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Text("Label", box.transform, C, C, new Vector2(0f, -44f), new Vector2(260f, 30f), label, 22, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Medium);
            return value;
        }

        public void ShowResult(bool win, RunStats s, bool record, float bestTime, int bestRooms)
        {
            hudRoot.gameObject.SetActive(false);
            bannerRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(true);
            ClearFloaters();
            resultTitle.text = win ? "ПОБЕДА" : "ПРОВАЛ";
            resultTitle.color = win ? UiKit.Accent : UiKit.Danger;
            resultTime.text = FormatTime(s.time);
            resultKills.text = s.kills.ToString();
            resultRicochets.text = s.ricochetKills.ToString();
            resultExtra.text = $"Комнат: {s.rooms} из {gm.RoomCount}   ·   Лучшая серия: {s.bestStreak}   ·   Смертей: {s.deaths}";
            // Лучшее достижение: время полного прохождения важнее числа комнат
            resultRecord.text = record ? "НОВЫЙ РЕКОРД!"
                : bestTime > 0f ? $"Рекорд: {FormatTime(bestTime)}" : $"Рекорд: {bestRooms} из {gm.RoomCount} комнат";
            resultRecord.color = record ? UiKit.Danger : UiKit.Muted;
        }

        // ---------- Общее ----------

        void Update()
        {
            if (hudRoot.gameObject.activeSelf) UpdateHud();
            UpdateBanner();
            UpdateFloaters();
        }

        static void SetAlpha(Graphic g, float a)
        {
            var c = g.color;
            c.a = a;
            g.color = c;
        }

        public static string FormatTime(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m:00}:{s:00.0}";
        }
    }
}
