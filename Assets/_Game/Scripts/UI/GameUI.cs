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
        static readonly Vector2 BL = Vector2.zero, BC = new(0.5f, 0f), C = new(0.5f, 0.5f), ML = new(0f, 0.5f), MR = new(1f, 0.5f);

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
        Image abilityGlow;
        Text abilitySeconds;
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
        Text scoreText, garageBoard, resultBoard, enemyCounter, resultKeys;
        Button continueButton;
        readonly List<Button> modeButtons = new();
        readonly List<(Image icon, Text count)> buffSlots = new();

        // слово во весь экран (распев «ПУЛЯ. ДУРА.»)
        RectTransform slamRoot;
        Text slamWord, slamSub;
        Image slamFlash, slamDim;
        float slamTime;
        public bool SlamVisible => slamRoot.gameObject.activeSelf;

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
            slamRoot = UiKit.Stretch("Slam", canvas);
            BuildSlam();
        }

        // ---------- Слово во весь экран ----------

        void BuildSlam()
        {
            slamDim = UiKit.Fill(slamRoot, new Color(0f, 0f, 0f, 0.55f));
            slamFlash = UiKit.Fill(UiKit.Stretch("Flash", slamRoot), new Color(1f, 1f, 1f, 0f));
            slamWord = UiKit.Text("Word", slamRoot, C, C, new Vector2(0f, 30f), new Vector2(1900f, 360f), "", 300, Color.white, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Shadow(slamWord, 8f, 0.5f);
            slamSub = UiKit.Text("Sub", slamRoot, BC, BC, new Vector2(0f, 120f), new Vector2(1400f, 50f), "", 30, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Mono);
            slamRoot.gameObject.SetActive(false);
        }

        /// <summary>Одно слово распева: вспышка, удар масштабом, чередование цвета.</summary>
        public void Slam(string word, Color color, string subtitle)
        {
            slamRoot.gameObject.SetActive(true);
            bannerRoot.gameObject.SetActive(false);
            slamWord.text = word;
            slamWord.color = color;
            slamSub.text = subtitle;
            slamTime = Time.unscaledTime;
        }

        public void EndSlam() => slamRoot.gameObject.SetActive(false);

        void UpdateSlam()
        {
            if (!slamRoot.gameObject.activeSelf) return;
            float t = Time.unscaledTime - slamTime;
            slamWord.rectTransform.localScale = Vector3.one * (t < 0.07f ? Mathf.Lerp(1.35f, 1f, t / 0.07f) : 1f + Mathf.Max(0f, 0.25f - t) * 0.04f);
            slamFlash.color = new Color(1f, 1f, 1f, Mathf.Max(0f, 0.35f - t * 2.5f));
        }

        void OnEnable() => GameEvents.ShotFired += OnShot;
        void OnDisable() => GameEvents.ShotFired -= OnShot;

        // ---------- Гараж ----------

        void BuildGarage()
        {
            // Шапка: эмблема, название, слоган — одним блоком в левом верхнем углу
            var logo = UiKit.Icon("Logo", garageRoot, TL, TL, new Vector2(32f, -24f), 96f, IconId.Logo, UiKit.Accent);
            UiKit.Shadow(logo, 3f, 0.5f);
            var title = UiKit.Text("Title", garageRoot, TL, TL, new Vector2(142f, -18f), new Vector2(900f, 90f), "ПУЛЯ — ДУРА", 72, UiKit.Accent, TextAnchor.UpperLeft, Weight.Black);
            UiKit.Shadow(title, 4f, 0.6f);
            UiKit.Text("Tagline", garageRoot, TL, TL, new Vector2(148f, -104f), new Vector2(800f, 34f), "Стреляй, чтобы увидеть", 24, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Bold);

            // Правила — в своей панели
            var rulesPanel = UiKit.Panel("Rules", garageRoot, TL, TL, new Vector2(32f, -160f), new Vector2(720f, 236f));
            UiKit.Glow(rulesPanel, UiKit.Accent, 0.35f);
            (IconId icon, Color color, string text)[] rules =
            {
                (IconId.Tank, UiKit.Accent, "Уничтожь всех врагов в комнате"),
                (IconId.Ricochet, UiKit.Accent, "Стены невидимы — отскок их подсвечивает"),
                (IconId.Target, UiKit.Danger, "После отскока пуля бьёт и тебя"),
                (IconId.Sound, UiKit.Danger, "Враги слепы, но слышат каждый выстрел"),
            };
            for (int i = 0; i < rules.Length; i++)
            {
                float y = -40f - i * 52f;
                UiKit.Icon("Mark", rulesPanel.transform, TL, C, new Vector2(42f, y), 30f, rules[i].icon, rules[i].color);
                UiKit.Text("Rule", rulesPanel.transform, TL, ML, new Vector2(78f, y), new Vector2(630f, 40f), rules[i].text, 24, UiKit.TextColor, TextAnchor.MiddleLeft, Weight.Bold);
            }

            // Лучшие забеги: рекорд в шапке, колонки и строки одним кеглем — моноширинные колонки совпадают
            var board = UiKit.Panel("Board", garageRoot, TL, TL, new Vector2(32f, -420f), new Vector2(820f, 262f));
            UiKit.Glow(board, UiKit.Accent, 0.35f);
            UiKit.Icon("Trophy", board.transform, TL, C, new Vector2(46f, -36f), 40f, IconId.Trophy, UiKit.Accent);
            UiKit.Text("Head", board.transform, TL, ML, new Vector2(84f, -36f), new Vector2(400f, 34f), "Лучшие забеги", 28, UiKit.TextColor, TextAnchor.MiddleLeft, Weight.Bold);
            recordText = UiKit.Text("Record", board.transform, TR, MR, new Vector2(-32f, -36f), new Vector2(360f, 34f), "", 28, UiKit.Accent, TextAnchor.MiddleRight, Weight.Mono);
            UiKit.Text("Columns", board.transform, TL, TL, new Vector2(32f, -74f), new Vector2(760f, 28f), " #    ОЧКИ     ВРЕМЯ  ИТОГ", 24, UiKit.Muted, TextAnchor.UpperLeft, Weight.Mono);
            garageBoard = UiKit.Text("Rows", board.transform, TL, TL, new Vector2(32f, -106f), new Vector2(760f, 150f), "", 24, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Mono);
            garageBoard.lineSpacing = 0.92f;

            // Карточка выбора справа
            const float cardW = 640f;
            var card = UiKit.Panel("Card", garageRoot, TR, TR, new Vector2(-36f, -36f), new Vector2(cardW, 880f));
            UiKit.Glow(card, UiKit.Accent, 0.4f);
            var ct = card.transform;
            const float pad = 32f;
            float inner = cardW - pad * 2f;

            Section(ct, "КОРПУС", IconId.Tank, -34f, pad);
            float hw = (inner - (cfg.hulls.Count - 1) * 12f) / cfg.hulls.Count;
            for (int i = 0; i < cfg.hulls.Count; i++)
            {
                int index = i;
                hullButtons.Add(UiKit.Button("Hull" + i, ct, TL, TL, new Vector2(pad + i * (hw + 12f), -62f), new Vector2(hw, 86f),
                    cfg.hulls[i].title, 24, () => gm.SelectHull(index), out _, Weight.Bold, IconOf(cfg.hulls[i].ability)));
            }
            hullInfo = UiKit.Text("HullInfo", ct, TL, TL, new Vector2(pad, -162f), new Vector2(inner, 34f), "", 24, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Bold);
            statArmor = Stat(ct, "БРОНЯ", -212f, pad, inner);
            statSpeed = Stat(ct, "СКОРОСТЬ", -240f, pad, inner);

            UiKit.Box("Divider", ct, TL, TL, new Vector2(pad, -270f), new Vector2(inner, 1f), UiKit.Line);
            Section(ct, "ПУШКА", IconId.Cannon, -296f, pad);
            float tw = (inner - (cfg.turrets.Count - 1) * 12f) / cfg.turrets.Count;
            for (int i = 0; i < cfg.turrets.Count; i++)
            {
                int index = i;
                turretButtons.Add(UiKit.Button("Turret" + i, ct, TL, TL, new Vector2(pad + i * (tw + 12f), -324f), new Vector2(tw, 86f),
                    cfg.turrets[i].title, 20, () => gm.SelectTurret(index), out _, Weight.Bold, IconOf(cfg.turrets[i].style)));
            }
            turretInfo = UiKit.Text("TurretInfo", ct, TL, TL, new Vector2(pad, -424f), new Vector2(inner, 34f), "", 24, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Bold);
            statDamage = Stat(ct, "УРОН", -474f, pad, inner);
            statRate = Stat(ct, "ТЕМП", -502f, pad, inner);
            statBounces = Stat(ct, "ОТСКОКИ", -530f, pad, inner);

            // Режим: 8 комнат или бесконечный (Tab)
            float mw = (inner - 12f) / 2f;
            UiKit.Box("Divider2", ct, TL, TL, new Vector2(pad, -560f), new Vector2(inner, 1f), UiKit.Line);
            Section(ct, "РЕЖИМ", IconId.Ricochet, -586f, pad);
            UiKit.Text("TabHint", ct, TR, MR, new Vector2(-pad, -586f), new Vector2(200f, 26f), "TAB", 18, UiKit.Muted, TextAnchor.MiddleRight, Weight.Mono);
            modeButtons.Add(UiKit.Button("ModeStory", ct, TL, TL, new Vector2(pad, -614f), new Vector2(mw, 70f),
                "8 комнат", 22, () => gm.SetEndless(false), out _, Weight.Bold, IconId.Target));
            modeButtons.Add(UiKit.Button("ModeEndless", ct, TL, TL, new Vector2(pad + mw + 12f, -614f), new Vector2(mw, 70f),
                "Бесконечно", 22, () => gm.SetEndless(true), out _, Weight.Bold, IconId.Infinity));

            var start = UiKit.Button("Start", ct, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, pad), new Vector2(inner, 124f),
                "В БОЙ", 56, () => gm.StartRun(), out var startLabel, Weight.Black);
            start.GetComponent<Image>().color = new Color(0.09f, 0.5f, 0.66f, 1f);
            UiKit.FrameOf(start).color = UiKit.Accent;
            UiKit.Glow(start, UiKit.Accent, 0.8f);
            startLabel.rectTransform.offsetMin = new Vector2(60f, 2f);
            UiKit.Icon("Aim", start.transform, C, C, new Vector2(-132f, 0f), 64f, IconId.Target, Color.white);
            UiKit.Text("Enter", start.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(120f, 30f), "ENTER", 20, new Color(0.7f, 0.95f, 1f, 0.75f), TextAnchor.MiddleRight, Weight.Mono);

            // Управление — в панели-полосе под таблицей; «W A S D» — четыре клавиши
            var keysBar = UiKit.Panel("ControlsBar", garageRoot, TL, TL, new Vector2(32f, -716f), new Vector2(100f, 66f));
            UiKit.Glow(keysBar, UiKit.Accent, 0.3f);
            var row = UiKit.KeyHints("Controls", keysBar.transform, ML, ML, new Vector2(28f, 0f), new[]
            {
                ("W A S D", "ехать"), ("SHIFT", "крадучись"), ("МЫШЬ", "целиться"), ("ЛКМ", "огонь"), ("ПРОБЕЛ", "скилл"),
            }, 18);
            keysBar.rectTransform.sizeDelta = new Vector2(row.sizeDelta.x + 56f, 66f);
            // Авторы музыки: лицензия CC BY требует указать автора там, где звучит трек
            UiKit.Text("MusicCredits", garageRoot, TL, TL, new Vector2(36f, -796f), new Vector2(1200f, 22f),
                "Музыка: Marsel Minga — Drum Machine Battle (CC BY-NC-ND 4.0) · Monplaisir — Turbo Rallye Clicker 4000 (CC0)", 14, new Color(UiKit.Muted.r, UiKit.Muted.g, UiKit.Muted.b, 0.75f), TextAnchor.UpperLeft, Weight.Mono);
        }

        /// <summary>Заголовок секции карточки: иконка и подпись.</summary>
        void Section(Transform parent, string text, IconId icon, float y, float pad)
        {
            UiKit.Icon("SectionIcon", parent, TL, ML, new Vector2(pad, y), 24f, icon, UiKit.Accent);
            UiKit.Text("Section", parent, TL, ML, new Vector2(pad + 34f, y), new Vector2(300f, 28f), text, 24, UiKit.TextColor, TextAnchor.MiddleLeft, Weight.Bold);
        }

        Image Stat(Transform parent, string name, float y, float pad, float inner)
        {
            UiKit.Text(name, parent, TL, ML, new Vector2(pad, y), new Vector2(160f, 24f), name, 18, UiKit.Muted, TextAnchor.MiddleLeft, Weight.Mono);
            return UiKit.Bar(name + "Bar", parent, TL, ML, new Vector2(pad + 160f, y), new Vector2(inner - 160f, 8f), UiKit.Accent);
        }

        public void RefreshGarage()
        {
            var h = cfg.hulls[gm.HullIndex];
            var t = cfg.turrets[gm.TurretIndex];
            Highlight(hullButtons, gm.HullIndex);
            Highlight(turretButtons, gm.TurretIndex);

            hullInfo.text = $"<color=#5CE0FF>{h.abilityTitle}</color> · {h.abilityDescription}";
            turretInfo.text = t.description;
            statArmor.fillAmount = h.maxHp / 8f;
            statSpeed.fillAmount = h.speed / 6.6f;
            statDamage.fillAmount = Mathf.Clamp01(t.damage * t.pellets / 6f);
            statRate.fillAmount = Mathf.Clamp01(Mathf.Sqrt(0.17f / (t.reload + t.chargeTime)));
            statBounces.fillAmount = t.bounces / 3f;

            Highlight(modeButtons, gm.Endless ? 1 : 0);
            garageBoard.text = BoardText(5, -1);
            // Рекорд — первая строка таблицы, чтобы цифры не расходились
            var top = Leaderboard.Top;
            recordText.text = top.Count > 0 ? $"РЕКОРД  {top[0].score}" : "";
        }

        static void Highlight(List<Button> buttons, int selected)
        {
            for (int i = 0; i < buttons.Count; i++) UiKit.SetSelected(buttons[i], i == selected);
        }

        public void ShowGarage()
        {
            EndSlam();
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
            damageFlash = UiKit.Fill(UiKit.Stretch("DamageFlash", hudRoot), new Color(1f, 0.15f, 0.12f, 0f));

            roomNumber = UiKit.Text("RoomNumber", hudRoot, TL, TL, new Vector2(48f, -30f), new Vector2(600f, 30f), "", 24, UiKit.Accent, TextAnchor.UpperLeft, Weight.Mono);
            roomTitle = UiKit.Text("RoomTitle", hudRoot, TL, TL, new Vector2(48f, -60f), new Vector2(800f, 50f), "", 40, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Black);
            UiKit.Shadow(roomTitle);
            enemyCounter = UiKit.Text("Enemies", hudRoot, TL, TL, new Vector2(48f, -106f), new Vector2(400f, 30f), "", 22, UiKit.Enemy, TextAnchor.UpperLeft, Weight.Mono);

            hintPanel = UiKit.Panel("HintPanel", hudRoot, TC, TC, new Vector2(0f, -30f), new Vector2(600f, 56f));
            hint = UiKit.Text("Hint", hintPanel.transform, C, C, Vector2.zero, new Vector2(1200f, 50f), "", 26, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Medium);

            UiKit.Icon("Clock", hudRoot, TR, TR, new Vector2(-218f, -36f), 30f, IconId.Clock, UiKit.Muted);
            timer = UiKit.Text("Timer", hudRoot, TR, TR, new Vector2(-48f, -30f), new Vector2(170f, 44f), "", 34, UiKit.TextColor, TextAnchor.UpperRight, Weight.Mono);
            UiKit.Icon("Trophy", hudRoot, TR, TR, new Vector2(-218f, -86f), 28f, IconId.Trophy, UiKit.Muted);
            scoreText = UiKit.Text("Score", hudRoot, TR, TR, new Vector2(-48f, -80f), new Vector2(170f, 40f), "0", 30, UiKit.Accent, TextAnchor.UpperRight, Weight.Mono);

            // Активные бонусы: щит, ваншот, метка — иконка и число
            (IconId icon, Color color)[] buffs = { (IconId.Armor, new Color(0.4f, 0.8f, 1f)), (IconId.Bolt, UiKit.Danger), (IconId.Eye, UiKit.TextColor) };
            for (int i = 0; i < buffs.Length; i++)
            {
                var slot = UiKit.Panel("Buff", hudRoot, BL, BL, new Vector2(40f + i * 92f, 192f), new Vector2(84f, 46f));
                var icon = UiKit.Icon("Icon", slot.transform, ML, ML, new Vector2(10f, 0f), 30f, buffs[i].icon, buffs[i].color);
                var count = UiKit.Text("Count", slot.transform, ML, ML, new Vector2(46f, 0f), new Vector2(40f, 30f), "", 22, UiKit.TextColor, TextAnchor.MiddleLeft, Weight.Mono);
                buffSlots.Add((icon, count));
                slot.gameObject.SetActive(false);
            }

            // Приборная панель слева внизу
            var dashPanel = UiKit.Panel("Dashboard", hudRoot, BL, BL, new Vector2(40f, 40f), new Vector2(430f, 140f));
            UiKit.Glow(dashPanel, UiKit.Accent, 0.35f);
            var dash = dashPanel.transform;
            for (int i = 0; i < GameManager.MaxLives; i++)
                lifeIcons.Add(UiKit.Icon("Life", dash, TL, TL, new Vector2(18f + i * 40f, -12f), 38f, IconId.Tank, UiKit.Danger));
            noiseIcon = UiKit.Icon("Noise", dash, TR, TR, new Vector2(-132f, -18f), 26f, IconId.Quiet, UiKit.Muted);
            noiseLabel = UiKit.Text("NoiseLabel", dash, TR, TR, new Vector2(-20f, -18f), new Vector2(110f, 26f), "", 20, UiKit.Muted, TextAnchor.MiddleRight, Weight.Mono);

            UiKit.Icon("Armor", dash, TL, ML, new Vector2(20f, -72f), 26f, IconId.Armor, UiKit.Accent);
            hpRow = UiKit.Rect("Hp", dash, TL, ML, new Vector2(60f, -72f), new Vector2(350f, 14f));

            weaponIcon = UiKit.Icon("Weapon", dash, TL, ML, new Vector2(20f, -112f), 26f, IconId.Cannon, UiKit.TextColor);
            weaponLabel = UiKit.Text("WeaponLabel", dash, TL, ML, new Vector2(60f, -106f), new Vector2(300f, 24f), "", 20, UiKit.Muted, TextAnchor.MiddleLeft, Weight.Mono);
            weaponBar = UiKit.Bar("Reload", dash, TL, ML, new Vector2(60f, -122f), new Vector2(350f, 4f), UiKit.TextColor);

            // Плитка скилла с круговой перезарядкой
            abilityTile = UiKit.Panel("Ability", hudRoot, BL, BL, new Vector2(486f, 40f), new Vector2(140f, 140f));
            abilityGlow = UiKit.Glow(abilityTile, UiKit.Accent, 0.7f);
            // Перезарядка затемняет плитку: пустая и светлая — значит готово
            abilityFill = UiKit.Fill(UiKit.Stretch("Cooldown", abilityTile.transform), new Color(0f, 0f, 0f, 0.65f));
            abilityFill.sprite = Icons.Panel;
            abilityFill.type = Image.Type.Filled;
            abilityFill.fillMethod = Image.FillMethod.Radial360;
            abilityFill.fillOrigin = (int)Image.Origin360.Top;
            abilityFill.fillClockwise = false;
            abilityIcon = UiKit.Icon("Icon", abilityTile.transform, C, C, new Vector2(0f, 14f), 62f, IconId.Mirror, UiKit.TextColor);
            abilityIcon.rectTransform.anchoredPosition = new Vector2(0f, 22f);
            abilityKey = UiKit.Text("Key", abilityTile.transform, BC, BC, new Vector2(0f, 8f), new Vector2(136f, 42f), "ПРОБЕЛ", 18, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Mono);
            abilityKey.lineSpacing = 0.85f;
            abilitySeconds = UiKit.Text("Seconds", abilityTile.transform, C, C, new Vector2(0f, 22f), new Vector2(120f, 60f), "", 40, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Mono);
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
            roomNumber.text = count > 0 ? $"{index + 1:00} / {count:00}" : "БЕСКОНЕЧНЫЙ РЕЖИМ";
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
            scoreText.text = gm.Stats.score.ToString();
            var pl = gm.Player;
            int[] buffValues = pl != null && !pl.IsDead ? new[] { pl.ShieldCharges, pl.Weapon.PowerShots, pl.Weapon.MarkShots } : new[] { 0, 0, 0 };
            for (int i = 0; i < buffSlots.Count; i++)
            {
                var slotGo = buffSlots[i].icon.transform.parent.gameObject;
                if (slotGo.activeSelf != buffValues[i] > 0) slotGo.SetActive(buffValues[i] > 0);
                buffSlots[i].count.text = "×" + buffValues[i];
            }
            // Жизни белые, последняя — красная. Пустые слоты показываем только до стартового числа
            int slots = Mathf.Max(gm.Lives, cfg.lives);
            for (int i = 0; i < lifeIcons.Count; i++)
            {
                lifeIcons[i].gameObject.SetActive(i < slots);
                lifeIcons[i].color = i < gm.Lives ? (gm.Lives == 1 ? UiKit.Danger : UiKit.TextColor) : UiKit.Faint;
            }
            enemyCounter.text = gm.EnemiesTotal > 0 ? $"ВРАГИ  {gm.EnemiesLeft} / {gm.EnemiesTotal}" : "";

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
                abilityIcon.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.15f);
                // Перезарядка — крупные секунды поверх иконки; готовность — свечение, которое вспыхивает в момент готовности
                float left = (1f - p.Ability.Cooldown01) * p.Hull.abilityCooldown;
                abilitySeconds.text = ready ? "" : left.ToString(left < 1f ? "0.0" : "0");
                float flash = Mathf.Clamp01(1f - (Time.time - p.Ability.ReadySince) / 0.6f);
                abilityGlow.gameObject.SetActive(ready);
                abilityGlow.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.45f + 0.5f * flash);
                abilityTile.rectTransform.localScale = Vector3.one * (1f + 0.08f * flash);
                abilityKey.text = p.Hull.abilityTitle.ToUpper() + "\n<size=13>ПРОБЕЛ</size>";
                abilityKey.color = ready ? UiKit.Accent : UiKit.Muted;
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
            f.text.fontSize = UiKit.Snap(size);
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
            UiKit.Fill(pauseRoot, new Color(0.005f, 0.01f, 0.02f, 0.82f)).raycastTarget = true;
            var panel = UiKit.Panel("PausePanel", pauseRoot, C, C, new Vector2(0f, -10f), new Vector2(640f, 620f));
            UiKit.Glow(panel, UiKit.Accent, 0.55f);
            var tab = UiKit.Panel("Tab", panel.transform, TC, C, Vector2.zero, new Vector2(120f, 70f), new Color(0.03f, 0.08f, 0.12f, 1f), UiKit.Accent);
            UiKit.Glow(tab, UiKit.Accent, 0.5f);
            UiKit.Icon("PauseIcon", tab.transform, C, C, Vector2.zero, 40f, IconId.Pause, UiKit.Accent);
            var title = UiKit.Text("Title", panel.transform, TC, TC, new Vector2(0f, -54f), new Vector2(600f, 100f), "ПАУЗА", 96, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Shadow(title, 4f, 0.6f);
            (string text, IconId icon, System.Action action)[] items =
            {
                ("Продолжить", IconId.Play, () => gm.TogglePause()),
                ("Комната заново", IconId.Restart, () => gm.RestartRoom()),
                ("Забег заново", IconId.Loop, () => gm.RestartRun()),
                ("В гараж", IconId.Home, () => gm.EnterGarage()),
            };
            for (int i = 0; i < items.Length; i++)
            {
                var b = UiKit.MenuButton("Item" + i, panel.transform, TC, TC, new Vector2(0f, -170f - i * 84f), new Vector2(540f, 72f), items[i].text, items[i].icon, items[i].action);
                if (i == 0) UiKit.SetSelected(b, true);
            }
            UiKit.KeyHints("Keys", panel.transform, BC, BC, new Vector2(0f, 26f), new[] { ("ESC", "продолжить"), ("R", "забег заново") }, 20);
            pauseRoot.gameObject.SetActive(false);
        }

        public void ShowPause(bool on) => pauseRoot.gameObject.SetActive(on);

        void BuildResult()
        {
            UiKit.Fill(resultRoot, new Color(0.01f, 0.015f, 0.025f, 0.94f)).raycastTarget = true;
            resultTitle = UiKit.Text("Title", resultRoot, C, C, new Vector2(0f, 268f), new Vector2(1400f, 140f), "", 112, UiKit.Accent, TextAnchor.MiddleCenter, Weight.Black);
            UiKit.Shadow(resultTitle, 5f, 0.6f);

            resultKills = Tile("ОЧКИ", IconId.Trophy, -300f);
            resultTime = Tile("ВРЕМЯ", IconId.Clock, 0f);
            resultRicochets = Tile("УБИТО РИКОШЕТОМ", IconId.Ricochet, 300f);
            // У правого края: на 4:3 не наезжает на плитки
            var board = UiKit.Panel("Board", resultRoot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 40f), new Vector2(420f, 330f));
            UiKit.Glow(board, UiKit.Accent, 0.3f);
            UiKit.Text("Head", board.transform, TL, TL, new Vector2(22f, -20f), new Vector2(380f, 26f), "ТАБЛИЦА ЗАБЕГОВ", 19, UiKit.Muted, TextAnchor.UpperLeft, Weight.Mono);
            UiKit.Text("Columns", board.transform, TL, TL, new Vector2(22f, -48f), new Vector2(390f, 22f), " #    ОЧКИ     ВРЕМЯ  ИТОГ", 18, UiKit.Muted, TextAnchor.UpperLeft, Weight.Mono);
            resultBoard = UiKit.Text("Rows", board.transform, TL, TL, new Vector2(22f, -70f), new Vector2(390f, 256f), "", 18, UiKit.TextColor, TextAnchor.UpperLeft, Weight.Mono);
            resultExtra = UiKit.Text("Extra", resultRoot, C, C, new Vector2(0f, -2f), new Vector2(1000f, 36f), "", 21, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Mono);
            resultRecord = UiKit.Text("Record", resultRoot, C, C, new Vector2(0f, -46f), new Vector2(1000f, 46f), "", 30, UiKit.Danger, TextAnchor.MiddleCenter, Weight.Bold);

            var again = UiKit.MenuButton("Again", resultRoot, C, C, new Vector2(-230f, -150f), new Vector2(440f, 84f), "Ещё раз", IconId.Loop, () => gm.RestartRun(), 34);
            UiKit.SetSelected(again, true);
            UiKit.MenuButton("Garage", resultRoot, C, C, new Vector2(230f, -150f), new Vector2(440f, 84f), "В гараж", IconId.Home, () => gm.EnterGarage(), 34);
            continueButton = UiKit.MenuButton("Continue", resultRoot, C, C, new Vector2(0f, -240f), new Vector2(900f, 66f), "Продолжить с комнаты", IconId.Play, () => gm.ContinueRun(), 24);
            resultKeys = UiKit.Text("Keys", resultRoot, C, C, new Vector2(0f, -300f), new Vector2(1000f, 30f), "", 19, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Mono);
            resultRoot.gameObject.SetActive(false);
        }

        Text Tile(string label, IconId icon, float x)
        {
            var box = UiKit.Panel("Tile", resultRoot, C, C, new Vector2(x, 110f), new Vector2(270f, 170f));
            UiKit.Glow(box, UiKit.Accent, 0.35f);
            UiKit.Icon("Icon", box.transform, TC, TC, new Vector2(0f, -16f), 32f, icon, UiKit.Accent);
            var value = UiKit.Text("Value", box.transform, C, C, new Vector2(0f, -4f), new Vector2(260f, 70f), "", 54, UiKit.TextColor, TextAnchor.MiddleCenter, Weight.Mono);
            UiKit.Text("Label", box.transform, BC, BC, new Vector2(0f, 14f), new Vector2(260f, 26f), label, 20, UiKit.Muted, TextAnchor.MiddleCenter, Weight.Mono);
            return value;
        }

        public void ShowResult(bool win, RunStats s, bool record, float bestTime, int bestRooms, int place = 0)
        {
            EndSlam();
            hudRoot.gameObject.SetActive(false);
            bannerRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(true);
            ClearTransient();
            resultTitle.text = win ? "ПОБЕДА" : gm.Endless ? $"ГЛУБИНА {s.rooms + 1}" : "ПРОВАЛ";
            resultTitle.color = win || gm.Endless ? UiKit.Accent : UiKit.Danger;
            bool canContinue = gm.ContinueRoom >= 0;
            continueButton.gameObject.SetActive(canContinue);
            // Подсказка клавиш — сразу под последней кнопкой
            resultKeys.rectTransform.anchoredPosition = new Vector2(0f, canContinue ? -296f : -222f);
            if (canContinue) continueButton.GetComponentInChildren<Text>().text = $"Продолжить с комнаты {gm.ContinueRoom + 1} · без таблицы";
            resultKeys.text = canContinue ? "R — ещё раз   ·   G — в гараж   ·   C — продолжить" : "R — ещё раз   ·   G — в гараж";
            resultTime.text = FormatTime(s.time);
            resultKills.text = s.score.ToString();
            resultRicochets.text = s.ricochetKills.ToString();
            resultExtra.text = $"КОМНАТ {(gm.Endless ? s.rooms.ToString() : s.rooms + "/" + gm.RoomCount)}   ·   ПОДБИТО {s.kills}   ·   СЕРИЯ {s.bestStreak}   ·   СМЕРТЕЙ {s.deaths}";
            resultBoard.text = BoardText(10, place);
            // Лучшее достижение: время полного прохождения важнее числа комнат
            resultRecord.text = record ? "НОВЫЙ РЕКОРД!"
                : bestTime > 0f ? $"Рекорд: {FormatTime(bestTime)}" : $"Рекорд: {bestRooms} из {gm.RoomCount} комнат";
            resultRecord.color = record ? UiKit.Danger : UiKit.Muted;
            if (place > 0)
            {
                resultRecord.text = place == 1 ? "ЛУЧШИЙ ЗАБЕГ В ТАБЛИЦЕ!" : $"МЕСТО #{place} В ТАБЛИЦЕ";
                resultRecord.color = place <= 3 ? UiKit.Danger : UiKit.TextColor;
            }
        }

        /// <summary>Строки таблицы забегов. Текущий забег (place) подсвечен.</summary>
        static string BoardText(int rows, int place)
        {
            var top = Leaderboard.Top;
            if (top.Count == 0) return "<color=#8A96A6>Пока пусто. Сыграй первый забег!</color>";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(rows, top.Count); i++)
            {
                var r = top[i];
                string result = r.endless ? $"∞ {r.rooms}" : r.win ? "ПОБЕДА" : $"{r.rooms}/{GameManager.I.RoomCount}";
                string line = $"{i + 1,2}  {r.score,6}  {FormatTime(r.time),8}  {result}";
                sb.AppendLine(i + 1 == place ? $"<color=#5CE0FF>{line}  ◄</color>" : line);
            }
            return sb.ToString();
        }

        // ---------- Общее ----------

        void Update()
        {
            if (hudRoot.gameObject.activeSelf) UpdateHud();
            UpdateBanner();
            UpdateFloaters();
            UpdateEdgeMarks();
            UpdateSlam();
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
