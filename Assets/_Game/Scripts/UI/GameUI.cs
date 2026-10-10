using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PulyaDura
{
    /// <summary>
    /// Весь интерфейс: гараж, HUD, обучение у танка, стрелки к врагам за кадром, баннеры, пауза, итоги.
    /// Принцип: иконка и цифра вместо фразы, текст только там, где без него не понять.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        static readonly Vector2 TL = new(0f, 1f), TR = new(1f, 1f), TC = new(0.5f, 1f);
        static readonly Vector2 BL = Vector2.zero, BC = new(0.5f, 0f), C = new(0.5f, 0.5f), ML = new(0f, 0.5f);

        GameManager gm;
        GameConfig cfg;
        RectTransform canvasRect, garageRoot, hudRoot, pauseRoot, resultRoot, floatLayer, edgeLayer;

        // гараж
        readonly List<Button> hullButtons = new();
        readonly List<Button> turretButtons = new();
        Text hullInfo, turretInfo, recordText;
        Image statArmor, statSpeed, statDamage, statRate, statBounces;

        // HUD
        Text roomNumber, roomTitle, hint, timer, weaponLabel, noiseLabel, abilityKey;
        Image hintPanel, weaponIcon, noiseIcon, weaponBar, abilityIcon, abilityFill, abilityTile, damageFlash;
        readonly List<Image> lifeIcons = new();
        readonly List<Image> hpSegments = new();
        RectTransform hpRow;
        int hpSegmentsFor = -1;
        float hintTime;
        Tank cachedPlayer;
        PlayerController playerController;

        // обучение у танка (только первая комната)
        RectTransform tutorialChip;
        int tutorialStep = -1;
        int tutorialProgress; // до какого шага игрок дошёл за сессию: после смерти не начинаем заново
        Vector3 tutorialStart;
        float tutorialStepTime;

        // баннер и итоги
        RectTransform bannerRoot;
        Text bannerTitle, bannerSub;
        Image bannerLineL, bannerLineR;
        float bannerTime, bannerLife;
        Text resultTitle, resultTime, resultKills, resultRicochets, resultExtra, resultRecord;

        class Floater { public Text text; public Vector3 world; public float t; }
        class EdgeMark { public Image img; public Tank target; public Color color; public float t; }
        readonly List<Floater> floaters = new();
        readonly List<EdgeMark> edgeMarks = new();

        public static IconId IconOf(AbilityType a) => a switch { AbilityType.Dash => IconId.Dash, AbilityType.Mirror => IconId.Mirror, _ => IconId.Wall };
        public static IconId IconOf(TurretStyle s) => s switch { TurretStyle.Plasma => IconId.Plasma, TurretStyle.Shotgun => IconId.Shotgun, TurretStyle.Rail => IconId.Rail, _ => IconId.Cannon };

        public void Build(GameManager manager)
        {
            gm = manager;
            cfg = manager.config;
            var canvas = UiKit.Canvas("UI", 10).transform;
            canvasRect = (RectTransform)canvas;

            hudRoot = UiKit.Stretch("Hud", canvas);
            edgeLayer = UiKit.Stretch("EdgeMarks", canvas);
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

        void OnEnable() => GameEvents.ShotFired += OnShot;
        void OnDisable() => GameEvents.ShotFired -= OnShot;

        // ---------- Гараж ----------

        void BuildGarage()
        {
            var title = UiKit.Text("Title", garageRoot, TL, TL, new Vector2(96f, -80f), new Vector2(900f, 120f), "ПУЛЯ — ДУРА", 108, UiKit.Accent, TextAnchor.UpperLeft, Weight.Black);
            UiKit.Shadow(title, 4f, 0.6f);
            UiKit.Text("Tagline", garageRoot, TL, TL, new Vector2(100f, -202f), new Vector2(800f, 50f), "Стреляй, чтобы увидеть.", 36, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Medium);

            (IconId icon, Color color, string text)[] rules =
            {
                (IconId.Ricochet, UiKit.Accent, "Стены невидимы. Отскок их подсвечивает"),
                (IconId.Target, UiKit.Danger, "После отскока пуля бьёт и тебя"),
                (IconId.Sound, UiKit.Enemy, "Враги слышат каждый выстрел"),
            };
            for (int i = 0; i < rules.Length; i++)
            {
                float y = -318f - i * 64f;
                UiKit.Icon("Mark", garageRoot, TL, ML, new Vector2(100f, y), 34f, rules[i].icon, rules[i].color);
                UiKit.Text("Rule", garageRoot, TL, ML, new Vector2(150f, y), new Vector2(700f, 44f), rules[i].text, 29, UiKit.TextColor, TextAnchor.MiddleLeft, Weight.Medium);
            }

            recordText = UiKit.Text("Record", garageRoot, BL, BL, new Vector2(100f, 128f), new Vector2(700f, 36f), "", 24, UiKit.Muted, TextAnchor.LowerLeft, Weight.Mono);

            // Карточка выбора справа
            var card = UiKit.Panel("Card", garageRoot, TR, TR, new Vector2(-96f, -80f), new Vector2(600f, 780f));
            var ct = card.transform;
            const float pad = 32f;
            float inner = 600f - pad * 2f;

            UiKit.Text("HullHead", ct, TL, TL, new Vector2(pad, -26f), new Vector2(inner, 26f), "КОРПУС", 18, UiKit.Muted, TextAnchor.UpperLeft, Weight.Mono);
            float hw = (inner - (cfg.hulls.Count - 1) * 10f) / cfg.hulls.Count;
            for (int i = 0; i < cfg.hulls.Count; i++)
            {
                int index = i;
                hullButtons.Add(UiKit.Button("Hull" + i, ct, TL, TL, new Vector2(pad + i * (hw + 10f), -58f), new Vector2(hw, 82f),
                    cfg.hulls[i].title, 22, () => gm.SelectHull(index), out _, Weight.Bold, IconOf(cfg.hulls[i].ability)));
            }
            hullInfo = UiKit.Text("HullInfo", ct, TL, TL, new Vector2(pad, -156f), new Vector2(inner, 34f), "", 24, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Medium);
            statArmor = Stat(ct, "БРОНЯ", -206f, pad, inner);
            statSpeed = Stat(ct, "СКОРОСТЬ", -234f, pad, inner);

            UiKit.Box("Divider", ct, TL, TL, new Vector2(pad, -272f), new Vector2(inner, 1f), UiKit.Line);

            UiKit.Text("TurretHead", ct, TL, TL, new Vector2(pad, -294f), new Vector2(inner, 26f), "ПУШКА", 18, UiKit.Muted, TextAnchor.UpperLeft, Weight.Mono);
            float tw = (inner - (cfg.turrets.Count - 1) * 10f) / cfg.turrets.Count;
            for (int i = 0; i < cfg.turrets.Count; i++)
            {
                int index = i;
                turretButtons.Add(UiKit.Button("Turret" + i, ct, TL, TL, new Vector2(pad + i * (tw + 10f), -326f), new Vector2(tw, 82f),
                    cfg.turrets[i].title, 20, () => gm.SelectTurret(index), out _, Weight.Bold, IconOf(cfg.turrets[i].style)));
            }
            turretInfo = UiKit.Text("TurretInfo", ct, TL, TL, new Vector2(pad, -424f), new Vector2(inner, 34f), "", 24, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Medium);
            statDamage = Stat(ct, "УРОН", -474f, pad, inner);
            statRate = Stat(ct, "ТЕМП", -502f, pad, inner);
            statBounces = Stat(ct, "ОТСКОКИ", -530f, pad, inner);

            var start = UiKit.Button("Start", ct, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, pad), new Vector2(inner, 112f),
                "В БОЙ", 50, () => gm.StartRun(), out _, Weight.Black);
            start.GetComponent<Image>().color = new Color(0.1f, 0.42f, 0.5f, 1f);
            UiKit.FrameOf(start).color = UiKit.Accent;
            UiKit.Text("Enter", start.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(120f, 30f), "ENTER", 18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleRight, Weight.Mono);

            UiKit.KeyHints("Controls", garageRoot, BC, BC, new Vector2(0f, 40f), new[]
            {
                ("WASD", "ехать"), ("SHIFT", "тихо"), ("МЫШЬ", "целиться"), ("ЛКМ", "огонь"), ("ПРОБЕЛ", "скилл"),
            }, 22);
        }

        Image Stat(Transform parent, string name, float y, float pad, float inner)
        {
            UiKit.Text(name, parent, TL, ML, new Vector2(pad, y), new Vector2(150f, 24f), name, 16, UiKit.Muted, TextAnchor.MiddleLeft, Weight.Mono);
            return UiKit.Bar(name + "Bar", parent, TL, ML, new Vector2(pad + 150f, y), new Vector2(inner - 150f, 6f), UiKit.Accent);
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
            recordText.text = bestTime > 0f ? $"РЕКОРД  {FormatTime(bestTime)}"
                : bestRooms > 0 ? $"РЕКОРД  {bestRooms}/{gm.RoomCount} КОМНАТ" : "";
        }

        static void Highlight(List<Button> buttons, int selected)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool on = i == selected;
                buttons[i].GetComponent<Image>().color = on ? new Color(0.12f, 0.38f, 0.45f, 1f) : new Color(1f, 1f, 1f, 0.04f);
                UiKit.FrameOf(buttons[i]).color = on ? UiKit.Accent : UiKit.Line;
                var icon = buttons[i].transform.Find("Icon");
                if (icon != null) icon.GetComponent<Image>().color = on ? Color.white : UiKit.Muted;
            }
        }

        public void ShowGarage()
        {
            garageRoot.gameObject.SetActive(true);
            hudRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(false);
            pauseRoot.gameObject.SetActive(false);
            bannerRoot.gameObject.SetActive(false);
            ClearTransient();
            RefreshGarage();
        }

        // ---------- HUD ----------

        void BuildHud()
        {
            damageFlash = UiKit.Fill(UiKit.Stretch("DamageFlash", hudRoot), new Color(1f, 0.2f, 0.05f, 0f));

            roomNumber = UiKit.Text("RoomNumber", hudRoot, TL, TL, new Vector2(48f, -34f), new Vector2(600f, 26f), "", 20, UiKit.Accent, TextAnchor.UpperLeft, Weight.Mono);
            roomTitle = UiKit.Text("RoomTitle", hudRoot, TL, TL, new Vector2(48f, -60f), new Vector2(800f, 50f), "", 40, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Black);
            UiKit.Shadow(roomTitle);

            hintPanel = UiKit.Panel("HintPanel", hudRoot, TC, TC, new Vector2(0f, -30f), new Vector2(600f, 56f));
            hint = UiKit.Text("Hint", hintPanel.transform, C, C, Vector2.zero, new Vector2(1200f, 50f), "", 26, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Medium);

            UiKit.Icon("Clock", hudRoot, TR, TR, new Vector2(-218f, -36f), 30f, IconId.Clock, UiKit.Muted);
            timer = UiKit.Text("Timer", hudRoot, TR, TR, new Vector2(-48f, -30f), new Vector2(170f, 44f), "", 34, UiKit.TextColor, TextAnchor.UpperRight, Weight.Mono);

            // Приборная панель слева внизу
            var dash = UiKit.Panel("Dashboard", hudRoot, BL, BL, new Vector2(40f, 40f), new Vector2(430f, 140f)).transform;
            for (int i = 0; i < cfg.lives; i++)
                lifeIcons.Add(UiKit.Icon("Life", dash, TL, TL, new Vector2(18f + i * 40f, -12f), 38f, IconId.Tank, UiKit.Danger));
            noiseIcon = UiKit.Icon("Noise", dash, TR, TR, new Vector2(-132f, -18f), 26f, IconId.Quiet, UiKit.Muted);
            noiseLabel = UiKit.Text("NoiseLabel", dash, TR, TR, new Vector2(-20f, -18f), new Vector2(110f, 26f), "", 16, UiKit.Muted, TextAnchor.MiddleRight, Weight.Mono);

            UiKit.Icon("Armor", dash, TL, ML, new Vector2(20f, -72f), 26f, IconId.Armor, UiKit.Accent);
            hpRow = UiKit.Rect("Hp", dash, TL, ML, new Vector2(60f, -72f), new Vector2(350f, 14f));

            weaponIcon = UiKit.Icon("Weapon", dash, TL, ML, new Vector2(20f, -112f), 26f, IconId.Cannon, UiKit.TextColor);
            weaponLabel = UiKit.Text("WeaponLabel", dash, TL, ML, new Vector2(60f, -106f), new Vector2(300f, 22f), "", 16, UiKit.Muted, TextAnchor.MiddleLeft, Weight.Mono);
            weaponBar = UiKit.Bar("Reload", dash, TL, ML, new Vector2(60f, -122f), new Vector2(350f, 4f), UiKit.TextColor);

            // Плитка скилла с круговой перезарядкой
            abilityTile = UiKit.Panel("Ability", hudRoot, BL, BL, new Vector2(482f, 40f), new Vector2(140f, 140f));
            abilityFill = UiKit.Fill(UiKit.Stretch("Cooldown", abilityTile.transform), UiKit.AccentDim);
            abilityFill.sprite = Icons.Panel;
            abilityFill.type = Image.Type.Filled;
            abilityFill.fillMethod = Image.FillMethod.Radial360;
            abilityFill.fillOrigin = (int)Image.Origin360.Top;
            abilityFill.fillClockwise = false;
            abilityIcon = UiKit.Icon("Icon", abilityTile.transform, C, C, new Vector2(0f, 14f), 62f, IconId.Mirror, UiKit.TextColor);
            abilityKey = UiKit.Text("Key", abilityTile.transform, BC, BC, new Vector2(0f, 12f), new Vector2(140f, 22f), "ПРОБЕЛ", 15, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Mono);
        }

        void RebuildHpSegments(int count)
        {
            foreach (var s in hpSegments) Destroy(s.gameObject);
            hpSegments.Clear();
            float w = (350f - (count - 1) * 4f) / count;
            for (int i = 0; i < count; i++)
                hpSegments.Add(UiKit.Box("Seg", hpRow, ML, ML, new Vector2(i * (w + 4f), 0f), new Vector2(w, 14f), UiKit.Accent));
            hpSegmentsFor = count;
        }

        public void ShowHud()
        {
            garageRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(false);
            hudRoot.gameObject.SetActive(true);
            bannerRoot.gameObject.SetActive(false);
            ClearTransient();
        }

        public void ShowRoom(int index, int count, string title, string roomHint)
        {
            roomNumber.text = $"{index + 1:00} / {count:00}";
            roomTitle.text = title;
            hint.text = roomHint;
            hintPanel.rectTransform.sizeDelta = new Vector2(hint.preferredWidth + 64f, 56f);
            hintTime = Time.unscaledTime;
            tutorialStep = index == 0 && tutorialProgress < 3 ? tutorialProgress : -1;
            tutorialStepTime = Time.unscaledTime;
            if (gm.Player != null) tutorialStart = gm.Player.transform.position;
        }

        void UpdateHud()
        {
            timer.text = FormatTime(gm.Stats.time);
            for (int i = 0; i < lifeIcons.Count; i++)
                lifeIcons[i].color = i < gm.Lives ? UiKit.Danger : UiKit.Faint;

            float age = Time.unscaledTime - hintTime;
            float ha = age < 6f ? 1f : Mathf.Clamp01(1f - (age - 6f));
            hintPanel.gameObject.SetActive(ha > 0f && hint.text.Length > 0);
            SetAlpha(hint, ha);
            hintPanel.color = new Color(UiKit.PanelFill.r, UiKit.PanelFill.g, UiKit.PanelFill.b, UiKit.PanelFill.a * ha);

            var p = gm.Player;
            if (p != null && !p.IsDead)
            {
                if (hpSegmentsFor != p.MaxHp) RebuildHpSegments(p.MaxHp);
                for (int i = 0; i < hpSegments.Count; i++)
                    hpSegments[i].color = i < p.Hp ? (p.Hp <= 1 ? UiKit.Danger : UiKit.Accent) : UiKit.Faint;

                weaponIcon.sprite = Icons.Get(IconOf(p.Turret.style));
                weaponLabel.text = p.Weapon.Charging ? $"{p.Turret.title.ToUpper()} · ЗАРЯД" : p.Turret.title.ToUpper();
                weaponBar.fillAmount = p.Weapon.Charging ? p.Weapon.Charge01 : p.Weapon.Reload01;
                weaponBar.color = p.Weapon.Ready ? UiKit.TextColor : UiKit.Muted;

                bool ready = p.Ability.Ready;
                abilityIcon.sprite = Icons.Get(IconOf(p.Hull.ability));
                abilityFill.fillAmount = 1f - p.Ability.Cooldown01;
                abilityIcon.color = ready ? Color.white : UiKit.Muted;
                UiKit.FrameOf(abilityTile).color = ready ? UiKit.Accent : UiKit.Line;
                abilityKey.color = ready ? UiKit.Accent : UiKit.Muted;
                float pulse = ready ? 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.03f : 1f;
                abilityIcon.rectTransform.localScale = Vector3.one * pulse;

                if (cachedPlayer != p) { cachedPlayer = p; playerController = p.GetComponent<PlayerController>(); }
                bool loud = playerController != null && playerController.IsLoud;
                bool sneak = playerController != null && playerController.IsSneaking;
                noiseIcon.sprite = Icons.Get(loud ? IconId.Sound : IconId.Quiet);
                noiseIcon.color = loud ? UiKit.Danger : sneak ? UiKit.Accent : UiKit.Muted;
                noiseLabel.text = loud ? "ШУМНО" : sneak ? "КРАДУСЬ" : "ТИХО";
                noiseLabel.color = noiseIcon.color;

                UpdateTutorial(p);
            }
            else SetTutorialChip(null); // танк взорван — подсказка не висит над обломками

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

        // ---------- Обучение у танка ----------

        /// <summary>Первая комната учит делом: подсказка клавиши висит под танком, пока игрок её не нажмёт.</summary>
        void UpdateTutorial(Tank p)
        {
            if (tutorialStep < 0 || gm.State != GameState.Playing)
            {
                SetTutorialChip(null);
                return;
            }
            bool done = tutorialStep switch
            {
                0 => Vector3.Distance(p.transform.position, tutorialStart) > 1.5f,
                1 => gm.Stats.shots > 0,
                _ => !p.Ability.Ready || Time.unscaledTime - tutorialStepTime > 10f,
            };
            if (done)
            {
                tutorialProgress = tutorialStep + 1;
                tutorialStep = tutorialStep >= 2 ? -1 : tutorialStep + 1;
                tutorialStepTime = Time.unscaledTime;
                SetTutorialChip(null);
                return;
            }
            if (tutorialChip == null)
            {
                var item = tutorialStep switch
                {
                    0 => ("WASD", "ехать"),
                    1 => ("ЛКМ", "выстрел в стену"),
                    _ => ("ПРОБЕЛ", p.Hull.abilityTitle.ToLower()),
                };
                SetTutorialChip(UiKit.KeyHints("Tutorial", floatLayer, C, new Vector2(0.5f, 1f), Vector2.zero, new[] { item }, 24));
            }
            Vector3 screen = Camera.main.WorldToScreenPoint(p.transform.position - new Vector3(0f, 0f, 1.1f));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(floatLayer, screen, null, out var local);
            tutorialChip.anchoredPosition = local;
        }

        void SetTutorialChip(RectTransform chip)
        {
            if (tutorialChip != null) Destroy(tutorialChip.gameObject);
            tutorialChip = chip;
        }

        // ---------- Стрелки к врагам за кадром ----------

        void OnShot(Tank t)
        {
            if (t.Team != Team.Enemy || gm.State != GameState.Playing || Camera.main == null) return;
            Vector3 vp = Camera.main.WorldToViewportPoint(t.transform.position);
            if (vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.04f && vp.y < 0.96f) return;

            EdgeMark mark = null;
            foreach (var m in edgeMarks)
                if (m.target == t || !m.img.gameObject.activeSelf) { mark = m; break; }
            if (mark == null)
            {
                mark = new EdgeMark { img = UiKit.Icon("Edge", edgeLayer, C, C, Vector2.zero, 44f, IconId.Chevron, Color.white) };
                edgeMarks.Add(mark);
            }
            mark.target = t;
            mark.color = EchoSystem.Ldr(t.Enemy != null && t.Enemy.accent.maxColorComponent > 0f ? t.Enemy.accent : cfg.enemyAccent);
            mark.t = 0f;
            mark.img.gameObject.SetActive(true);
        }

        void UpdateEdgeMarks()
        {
            Vector2 half = canvasRect.rect.size * 0.5f - new Vector2(56f, 56f);
            foreach (var m in edgeMarks)
            {
                if (!m.img.gameObject.activeSelf) continue;
                m.t += Time.unscaledDeltaTime;
                if (m.t > 1.6f || m.target == null || Camera.main == null) { m.img.gameObject.SetActive(false); continue; }
                Vector3 screen = Camera.main.WorldToScreenPoint(m.target.transform.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
                Vector2 dir = local.sqrMagnitude > 1f ? local.normalized : Vector2.up;
                float k = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(dir.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                var rt = m.img.rectTransform;
                rt.anchoredPosition = dir * k;
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                rt.localScale = Vector3.one * (1f + Mathf.Max(0f, 0.4f - m.t) * 1.5f);
                var c = m.color;
                c.a = Mathf.Clamp01((1.6f - m.t) / 0.5f);
                m.img.color = c;
            }
        }

        public int ActiveEdgeMarks
        {
            get { int n = 0; foreach (var m in edgeMarks) if (m.img.gameObject.activeSelf) n++; return n; }
        }

        // ---------- Баннер и всплывающий текст ----------

        void BuildBanner(Transform canvas)
        {
            bannerRoot = UiKit.Rect("Banner", canvas, C, C, new Vector2(0f, 150f), new Vector2(1400f, 220f));
            bannerTitle = UiKit.Text("Title", bannerRoot, C, C, new Vector2(0f, 30f), new Vector2(1400f, 140f), "", 108, UiKit.Accent, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Shadow(bannerTitle, 5f, 0.6f);
            bannerLineL = UiKit.Box("LineL", bannerRoot, C, new Vector2(1f, 0.5f), new Vector2(-400f, 30f), new Vector2(180f, 3f), Color.white);
            bannerLineR = UiKit.Box("LineR", bannerRoot, C, new Vector2(0f, 0.5f), new Vector2(400f, 30f), new Vector2(180f, 3f), Color.white);
            bannerSub = UiKit.Text("Sub", bannerRoot, C, C, new Vector2(0f, -62f), new Vector2(1400f, 50f), "", 30, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Medium);
            UiKit.Shadow(bannerSub);
            bannerRoot.gameObject.SetActive(false);
        }

        public void Banner(string title, string sub, Color color, float life = 1.8f)
        {
            bannerRoot.gameObject.SetActive(true);
            bannerTitle.text = title;
            bannerTitle.color = color;
            bannerSub.text = sub;
            float half = bannerTitle.preferredWidth * 0.5f + 36f;
            bannerLineL.rectTransform.anchoredPosition = new Vector2(-half, 30f);
            bannerLineR.rectTransform.anchoredPosition = new Vector2(half, 30f);
            bannerLineL.color = bannerLineR.color = color;
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
            float lineK = Mathf.Clamp01(t / 0.3f);
            bannerLineL.rectTransform.sizeDelta = bannerLineR.rectTransform.sizeDelta = new Vector2(180f * lineK, 3f);
            SetAlpha(bannerLineL, a);
            SetAlpha(bannerLineR, a);
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

        void ClearTransient()
        {
            foreach (var f in floaters) f.text.gameObject.SetActive(false);
            foreach (var m in edgeMarks) m.img.gameObject.SetActive(false);
            SetTutorialChip(null);
            tutorialStep = -1;
        }

        // ---------- Пауза и итоги ----------

        void BuildPause()
        {
            UiKit.Fill(pauseRoot, new Color(0.01f, 0.015f, 0.025f, 0.78f)).raycastTarget = true;
            UiKit.Text("Title", pauseRoot, C, C, new Vector2(0f, 180f), new Vector2(800f, 130f), "ПАУЗА", 104, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Button("Resume", pauseRoot, C, C, new Vector2(0f, 40f), new Vector2(440f, 80f), "Продолжить", 30, () => gm.TogglePause(), out _);
            UiKit.Button("Restart", pauseRoot, C, C, new Vector2(0f, -56f), new Vector2(440f, 80f), "Заново", 30, () => gm.RestartRun(), out _);
            UiKit.Button("Garage", pauseRoot, C, C, new Vector2(0f, -152f), new Vector2(440f, 80f), "В гараж", 30, () => gm.EnterGarage(), out _);
            UiKit.KeyHints("Keys", pauseRoot, C, C, new Vector2(0f, -250f), new[] { ("ESC", "продолжить"), ("R", "заново") }, 20);
            pauseRoot.gameObject.SetActive(false);
        }

        public void ShowPause(bool on) => pauseRoot.gameObject.SetActive(on);

        void BuildResult()
        {
            UiKit.Fill(resultRoot, new Color(0.01f, 0.015f, 0.025f, 0.86f)).raycastTarget = true;
            resultTitle = UiKit.Text("Title", resultRoot, C, C, new Vector2(0f, 290f), new Vector2(1400f, 140f), "", 120, UiKit.Accent, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Shadow(resultTitle, 5f, 0.6f);

            resultTime = Tile("ВРЕМЯ", IconId.Clock, -300f);
            resultKills = Tile("ПОДБИТО", IconId.Target, 0f);
            resultRicochets = Tile("РИКОШЕТОМ", IconId.Ricochet, 300f);
            resultExtra = UiKit.Text("Extra", resultRoot, C, C, new Vector2(0f, -40f), new Vector2(1000f, 40f), "", 22, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Mono);
            resultRecord = UiKit.Text("Record", resultRoot, C, C, new Vector2(0f, -100f), new Vector2(1000f, 50f), "", 30, UiKit.Danger, TextAnchor.MiddleCenter, Weight.Bold);

            UiKit.Button("Again", resultRoot, C, C, new Vector2(-240f, -230f), new Vector2(440f, 92f), "Ещё раз", 36, () => gm.RestartRun(), out _, Weight.Black);
            UiKit.Button("Garage", resultRoot, C, C, new Vector2(240f, -230f), new Vector2(440f, 92f), "В гараж", 36, () => gm.EnterGarage(), out _);
            UiKit.KeyHints("Keys", resultRoot, C, C, new Vector2(0f, -320f), new[] { ("R", "ещё раз"), ("G", "в гараж") }, 20);
            resultRoot.gameObject.SetActive(false);
        }

        Text Tile(string label, IconId icon, float x)
        {
            var box = UiKit.Panel("Tile", resultRoot, C, C, new Vector2(x, 110f), new Vector2(270f, 170f));
            UiKit.Icon("Icon", box.transform, TC, TC, new Vector2(0f, -16f), 32f, icon, UiKit.Muted);
            var value = UiKit.Text("Value", box.transform, C, C, new Vector2(0f, -4f), new Vector2(260f, 70f), "", 54, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Mono);
            UiKit.Text("Label", box.transform, BC, BC, new Vector2(0f, 14f), new Vector2(260f, 26f), label, 16, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Mono);
            return value;
        }

        public void ShowResult(bool win, RunStats s, bool record, float bestTime, int bestRooms)
        {
            hudRoot.gameObject.SetActive(false);
            bannerRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(true);
            ClearTransient();
            resultTitle.text = win ? "ПОБЕДА" : "ПРОВАЛ";
            resultTitle.color = win ? UiKit.Accent : UiKit.Danger;
            resultTime.text = FormatTime(s.time);
            resultKills.text = s.kills.ToString();
            resultRicochets.text = s.ricochetKills.ToString();
            resultExtra.text = $"КОМНАТ {s.rooms}/{gm.RoomCount}   ·   СЕРИЯ {s.bestStreak}   ·   СМЕРТЕЙ {s.deaths}";
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
            UpdateEdgeMarks();
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
