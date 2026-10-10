using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PulyaDura
{
    /// <summary>Весь интерфейс: гараж, HUD, баннеры, всплывающий текст, пауза, итоги.</summary>
    public class GameUI : MonoBehaviour
    {
        GameManager gm;
        GameConfig cfg;

        RectTransform garageRoot, hudRoot, pauseRoot, resultRoot, floatLayer;
        // гараж
        readonly List<Image> hullButtons = new();
        readonly List<Image> turretButtons = new();
        Text hullInfo, turretInfo, recordText;
        Image statArmor, statSpeed, statDamage, statRate, statBounces, statEcho;
        // HUD
        Text roomNumber, roomTitle, hint, timer, kills, lives, hpLabel, weaponLabel, abilityLabel;
        Image hpBar, weaponBar, abilityBar, damageFlash;
        float hintTime;
        // баннер
        RectTransform bannerRoot;
        Text bannerTitle, bannerSub;
        float bannerTime, bannerLife;
        // итоги
        Text resultTitle, resultStats;

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
            var tl = new Vector2(0f, 1f);
            var tr = new Vector2(1f, 1f);
            var t = UiKit.Text("Title", garageRoot, tl, tl, new Vector2(80f, -60f), new Vector2(760f, 110f), "ПУЛЯ — ДУРА", 92, UiKit.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Shadow(t, 4f);
            UiKit.Text("Subtitle", garageRoot, tl, tl, new Vector2(84f, -170f), new Vector2(740f, 40f), "Ты ничего не видишь. Стреляй — и смотри, что вспыхнет.", 28, UiKit.Muted, TextAnchor.UpperLeft);

            var rules = UiKit.Box("Rules", garageRoot, tl, tl, new Vector2(80f, -240f), new Vector2(700f, 360f), UiKit.Panel);
            UiKit.Border(rules, new Color(1f, 1f, 1f, 0.06f));
            UiKit.Text("Head", rules.transform, tl, tl, new Vector2(28f, -22f), new Vector2(640f, 30f), "КАК ИГРАТЬ", 22, UiKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Text("Body", rules.transform, tl, tl, new Vector2(28f, -64f), new Vector2(650f, 290f),
                "<color=#5BE6FF><b>1.</b></color>  Стен не видно. Отскок пули — это вспышка эха: она подсвечивает стены и врагов.\n\n" +
                "<color=#FF8C33><b>2.</b></color>  Пуля отскакивает. После отскока она бьёт и тебя.\n\n" +
                "<color=#FF4D40><b>3.</b></color>  Враги тоже слепые, но слышат каждый выстрел. Уничтожь всех в комнате.",
                27, UiKit.TextColor, TextAnchor.UpperLeft);

            var controls = UiKit.Box("Controls", garageRoot, tl, tl, new Vector2(80f, -620f), new Vector2(700f, 170f), UiKit.Panel);
            UiKit.Border(controls, new Color(1f, 1f, 1f, 0.06f));
            UiKit.Text("Head", controls.transform, tl, tl, new Vector2(28f, -22f), new Vector2(640f, 30f), "УПРАВЛЕНИЕ", 22, UiKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Text("Body", controls.transform, tl, tl, new Vector2(28f, -60f), new Vector2(650f, 110f),
                "<b>WASD</b> — ехать   <b>Мышь</b> — целиться   <b>ЛКМ</b> — огонь\n<b>Пробел / ПКМ</b> — скилл корпуса   <b>R</b> — заново   <b>Esc</b> — пауза",
                24, UiKit.TextColor, TextAnchor.UpperLeft);

            recordText = UiKit.Text("Record", garageRoot, Vector2.zero, Vector2.zero, new Vector2(84f, 60f), new Vector2(700f, 40f), "", 24, UiKit.Muted, TextAnchor.LowerLeft);

            // Правая колонка: корпус и пушка
            UiKit.Text("HullHead", garageRoot, tr, tr, new Vector2(-80f, -60f), new Vector2(720f, 30f), "КОРПУС", 22, UiKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            for (int i = 0; i < cfg.hulls.Count; i++)
            {
                int index = i;
                var h = cfg.hulls[i];
                float w = (720f - (cfg.hulls.Count - 1) * 14f) / cfg.hulls.Count;
                float x = -80f - (cfg.hulls.Count - 1 - i) * (w + 14f);
                var b = UiKit.Button("Hull" + i, garageRoot, tr, tr, new Vector2(x, -98f), new Vector2(w, 92f),
                    $"{h.title}\n<size=20><color=#9AA3B2>{h.abilityTitle}</color></size>", 30, () => gm.SelectHull(index), out _);
                hullButtons.Add(b.GetComponent<Image>());
            }
            hullInfo = UiKit.Text("HullInfo", garageRoot, tr, tr, new Vector2(-80f, -206f), new Vector2(720f, 80f), "", 23, UiKit.TextColor, TextAnchor.UpperLeft);
            statArmor = Stat("Броня", -300f);
            statSpeed = Stat("Скорость", -336f);

            UiKit.Text("TurretHead", garageRoot, tr, tr, new Vector2(-80f, -400f), new Vector2(720f, 30f), "ПУШКА", 22, UiKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            for (int i = 0; i < cfg.turrets.Count; i++)
            {
                int index = i;
                var tc = cfg.turrets[i];
                float w = (720f - (cfg.turrets.Count - 1) * 12f) / cfg.turrets.Count;
                float x = -80f - (cfg.turrets.Count - 1 - i) * (w + 12f);
                var b = UiKit.Button("Turret" + i, garageRoot, tr, tr, new Vector2(x, -438f), new Vector2(w, 76f), tc.title, 26, () => gm.SelectTurret(index), out _);
                turretButtons.Add(b.GetComponent<Image>());
            }
            turretInfo = UiKit.Text("TurretInfo", garageRoot, tr, tr, new Vector2(-80f, -530f), new Vector2(720f, 80f), "", 23, UiKit.TextColor, TextAnchor.UpperLeft);
            statDamage = Stat("Урон", -620f);
            statRate = Stat("Скорострельность", -656f);
            statBounces = Stat("Отскоки", -692f);
            statEcho = Stat("Эхо", -728f);

            var start = UiKit.Button("Start", garageRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-80f, 60f), new Vector2(720f, 110f),
                "В БОЙ   <size=26><color=#9AA3B2>Enter</color></size>", 46, () => gm.StartRun(), out _);
            start.GetComponent<Image>().color = new Color(0.1f, 0.35f, 0.4f, 0.95f);
            UiKit.Border(start.GetComponent<Image>(), UiKit.Accent, 2f);
        }

        Image Stat(string name, float y)
        {
            var tr = new Vector2(1f, 1f);
            UiKit.Text(name, garageRoot, tr, tr, new Vector2(-80f - 500f, y), new Vector2(220f, 28f), name, 20, UiKit.Muted, TextAnchor.MiddleLeft);
            return UiKit.Bar(name + "Bar", garageRoot, tr, tr, new Vector2(-80f, y - 9f), new Vector2(490f, 10f), UiKit.Accent);
        }

        public void RefreshGarage()
        {
            var h = cfg.hulls[gm.HullIndex];
            var t = cfg.turrets[gm.TurretIndex];
            for (int i = 0; i < hullButtons.Count; i++)
                hullButtons[i].color = i == gm.HullIndex ? new Color(0.12f, 0.42f, 0.48f, 0.95f) : UiKit.Panel;
            for (int i = 0; i < turretButtons.Count; i++)
                turretButtons[i].color = i == gm.TurretIndex ? new Color(0.12f, 0.42f, 0.48f, 0.95f) : UiKit.Panel;

            hullInfo.text = $"{h.description}\n<color=#FF8C33><b>{h.abilityTitle}:</b></color> {h.abilityDescription}";
            turretInfo.text = t.description;
            statArmor.fillAmount = h.maxHp / 8f;
            statSpeed.fillAmount = h.speed / 6.6f;
            statDamage.fillAmount = Mathf.Clamp01(t.damage * t.pellets / 6f);
            statRate.fillAmount = Mathf.Clamp01(Mathf.Sqrt(0.17f / (t.reload + t.chargeTime)));
            statBounces.fillAmount = t.bounces / 3f;
            statEcho.fillAmount = t.echoRadius / 6.5f;

            float bestTime = PlayerPrefs.GetFloat("best_time", 0f);
            int bestRooms = PlayerPrefs.GetInt("best_rooms", 0);
            recordText.text = bestTime > 0f ? $"Рекорд: все комнаты за {FormatTime(bestTime)}"
                : bestRooms > 0 ? $"Рекорд: {bestRooms} из {gm.RoomCount} комнат" : "";
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
            var tl = new Vector2(0f, 1f);
            var tr = new Vector2(1f, 1f);
            var tc = new Vector2(0.5f, 1f);
            var bl = Vector2.zero;

            damageFlash = UiKit.Image(UiKit.Stretch("DamageFlash", hudRoot), new Color(1f, 0.2f, 0.05f, 0f));

            roomNumber = UiKit.Text("RoomNumber", hudRoot, tl, tl, new Vector2(40f, -28f), new Vector2(600f, 30f), "", 22, UiKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            roomTitle = UiKit.Text("RoomTitle", hudRoot, tl, tl, new Vector2(40f, -56f), new Vector2(700f, 50f), "", 38, UiKit.TextColor, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Shadow(roomTitle);
            hint = UiKit.Text("Hint", hudRoot, tc, tc, new Vector2(0f, -36f), new Vector2(1000f, 90f), "", 30, UiKit.Accent, TextAnchor.UpperCenter);
            UiKit.Shadow(hint);
            timer = UiKit.Text("Timer", hudRoot, tr, tr, new Vector2(-40f, -28f), new Vector2(300f, 50f), "", 40, UiKit.TextColor, TextAnchor.UpperRight, FontStyle.Bold);
            kills = UiKit.Text("Kills", hudRoot, tr, tr, new Vector2(-40f, -80f), new Vector2(300f, 30f), "", 22, UiKit.Muted, TextAnchor.UpperRight);

            lives = UiKit.Text("Lives", hudRoot, bl, bl, new Vector2(40f, 196f), new Vector2(400f, 40f), "", 32, UiKit.Danger, TextAnchor.LowerLeft, FontStyle.Bold);
            hpLabel = UiKit.Text("HpLabel", hudRoot, bl, bl, new Vector2(40f, 160f), new Vector2(400f, 26f), "", 20, UiKit.Muted, TextAnchor.LowerLeft, FontStyle.Bold);
            hpBar = UiKit.Bar("Hp", hudRoot, bl, bl, new Vector2(40f, 136f), new Vector2(380f, 18f), UiKit.Accent);
            weaponLabel = UiKit.Text("WeaponLabel", hudRoot, bl, bl, new Vector2(40f, 98f), new Vector2(400f, 26f), "", 20, UiKit.Muted, TextAnchor.LowerLeft, FontStyle.Bold);
            weaponBar = UiKit.Bar("Weapon", hudRoot, bl, bl, new Vector2(40f, 82f), new Vector2(380f, 10f), UiKit.TextColor);
            abilityLabel = UiKit.Text("AbilityLabel", hudRoot, bl, bl, new Vector2(40f, 44f), new Vector2(400f, 26f), "", 20, UiKit.Muted, TextAnchor.LowerLeft, FontStyle.Bold);
            abilityBar = UiKit.Bar("Ability", hudRoot, bl, bl, new Vector2(40f, 28f), new Vector2(380f, 10f), UiKit.Danger);
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
            var s = gm.Stats;
            timer.text = FormatTime(s.time);
            kills.text = $"Подбито: {s.kills}   Рикошетом: {s.ricochetKills}";
            lives.text = new string('♥', Mathf.Max(0, gm.Lives)).Replace("♥", "♥ ").TrimEnd();

            float age = Time.unscaledTime - hintTime;
            var hc = hint.color;
            hc.a = age < 7f ? 1f : Mathf.Clamp01(1f - (age - 7f));
            hint.color = hc;

            var p = gm.Player;
            if (p != null && !p.IsDead)
            {
                hpLabel.text = $"БРОНЯ {p.Hp} / {p.MaxHp}";
                hpBar.fillAmount = (float)p.Hp / p.MaxHp;
                hpBar.color = p.Hp <= 1 ? UiKit.Danger : UiKit.Accent;
                weaponLabel.text = p.Weapon.Charging ? $"{p.Turret.title.ToUpper()} — ЗАРЯД" : p.Turret.title.ToUpper();
                weaponBar.fillAmount = p.Weapon.Charging ? p.Weapon.Charge01 : p.Weapon.Reload01;
                abilityLabel.text = $"{p.Ability.Title.ToUpper()}  <color=#9AA3B2>Пробел / ПКМ</color>" + (p.Ability.Ready ? "  <color=#FF8C33>ГОТОВ</color>" : "");
                abilityBar.fillAmount = p.Ability.Cooldown01;
            }
            else
            {
                hpBar.fillAmount = 0f;
                hpLabel.text = "БРОНЯ 0";
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
            var c = new Vector2(0.5f, 0.5f);
            bannerRoot = UiKit.Rect("Banner", canvas, c, c, c, new Vector2(0f, 140f), new Vector2(1400f, 220f));
            bannerTitle = UiKit.Text("Title", bannerRoot, c, c, new Vector2(0f, 30f), new Vector2(1400f, 130f), "", 110, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Shadow(bannerTitle, 5f);
            bannerSub = UiKit.Text("Sub", bannerRoot, c, c, new Vector2(0f, -60f), new Vector2(1400f, 50f), "", 32, UiKit.TextColor, TextAnchor.MiddleCenter);
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
            float pop = t < 0.15f ? Mathf.Lerp(1.6f, 0.95f, t / 0.15f) : t < 0.25f ? Mathf.Lerp(0.95f, 1f, (t - 0.15f) / 0.1f) : 1f;
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
                var c = new Vector2(0.5f, 0.5f);
                var t = UiKit.Text("Float", floatLayer, Vector2.zero, c, Vector2.zero, new Vector2(700f, 80f), "", size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
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
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = local + new Vector2(0f, f.t * 90f);
            float pop = f.t < 0.1f ? Mathf.Lerp(1.5f, 1f, f.t / 0.1f) : 1f;
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
            UiKit.Image(pauseRoot, new Color(0f, 0f, 0f, 0.7f)).raycastTarget = true;
            var c = new Vector2(0.5f, 0.5f);
            UiKit.Text("Title", pauseRoot, c, c, new Vector2(0f, 170f), new Vector2(800f, 120f), "ПАУЗА", 96, UiKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Button("Resume", pauseRoot, c, c, new Vector2(0f, 30f), new Vector2(460f, 84f), "Продолжить   <size=22><color=#9AA3B2>Esc</color></size>", 34, () => gm.TogglePause(), out _);
            UiKit.Button("Restart", pauseRoot, c, c, new Vector2(0f, -70f), new Vector2(460f, 84f), "Заново   <size=22><color=#9AA3B2>R</color></size>", 34, () => gm.RestartRun(), out _);
            UiKit.Button("Garage", pauseRoot, c, c, new Vector2(0f, -170f), new Vector2(460f, 84f), "В гараж", 34, () => gm.EnterGarage(), out _);
            pauseRoot.gameObject.SetActive(false);
        }

        public void ShowPause(bool on) => pauseRoot.gameObject.SetActive(on);

        void BuildResult()
        {
            UiKit.Image(resultRoot, new Color(0.01f, 0.015f, 0.02f, 0.86f)).raycastTarget = true;
            var c = new Vector2(0.5f, 0.5f);
            resultTitle = UiKit.Text("Title", resultRoot, c, c, new Vector2(0f, 270f), new Vector2(1400f, 140f), "", 110, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Shadow(resultTitle, 5f);
            resultStats = UiKit.Text("Stats", resultRoot, c, c, new Vector2(0f, 20f), new Vector2(900f, 330f), "", 32, UiKit.TextColor, TextAnchor.MiddleCenter);
            UiKit.Button("Again", resultRoot, c, c, new Vector2(-250f, -250f), new Vector2(460f, 96f), "Ещё раз   <size=24><color=#9AA3B2>R</color></size>", 38, () => gm.RestartRun(), out _);
            UiKit.Button("Garage", resultRoot, c, c, new Vector2(250f, -250f), new Vector2(460f, 96f), "В гараж   <size=24><color=#9AA3B2>G</color></size>", 38, () => gm.EnterGarage(), out _);
            resultRoot.gameObject.SetActive(false);
        }

        public void ShowResult(bool win, RunStats s, bool record, float bestTime, int bestRooms)
        {
            hudRoot.gameObject.SetActive(false);
            bannerRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(true);
            ClearFloaters();
            resultTitle.text = win ? "ПОБЕДА" : "ПРОВАЛ";
            resultTitle.color = win ? UiKit.Accent : UiKit.Danger;
            float accuracy = s.shots > 0 ? (float)s.kills / s.shots * 100f : 0f;
            string recordLine = record ? "<color=#FF8C33><b>НОВЫЙ РЕКОРД!</b></color>"
                : win ? $"Рекорд: {FormatTime(bestTime)}" : $"Рекорд: {bestRooms} из {gm.RoomCount} комнат";
            resultStats.text =
                $"Комнат пройдено: <b>{s.rooms} / {gm.RoomCount}</b>\n" +
                $"Время: <b>{FormatTime(s.time)}</b>\n" +
                $"Подбито: <b>{s.kills}</b>   рикошетом: <b>{s.ricochetKills}</b>\n" +
                $"Лучшая серия: <b>{s.bestStreak}</b>   выстрелов: <b>{s.shots}</b>   смертей: <b>{s.deaths}</b>\n\n" +
                recordLine;
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
