using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public enum AbilityType { Dash, Mirror, Wall }
    public enum FireMode { Projectile, Rail }
    public enum EnemyBehaviour { Dummy, Static, Hunter }
    public enum HullStyle { Light, Medium, Heavy }
    public enum TurretStyle { Cannon, Plasma, Shotgun, Rail }

    [Serializable]
    public class HullConfig
    {
        public string id;
        public string title;
        [TextArea] public string description;
        public HullStyle style = HullStyle.Medium;
        public int maxHp = 5;
        public float speed = 5f;
        public float turnSpeed = 540f;
        [Tooltip("Ширина и длина корпуса")] public Vector2 size = new(1.1f, 1.3f);
        public float height = 0.32f;
        public AbilityType ability;
        public string abilityTitle;
        [TextArea] public string abilityDescription;
        public float abilityCooldown = 6f;
        public float abilityDuration = 0f;
        [Tooltip("Сколько раз скилл можно применить за комнату; 0 — без ограничения")] public int abilityCharges;
    }

    [Serializable]
    public class TurretConfig
    {
        public string id;
        public string title;
        [TextArea] public string description;
        public TurretStyle style = TurretStyle.Cannon;
        public FireMode mode = FireMode.Projectile;
        public int damage = 2;
        public float reload = 0.5f;
        public int pellets = 1;
        [Tooltip("Разброс дроби в градусах")] public float spread;
        public float projectileSpeed = 14f;
        public float projectileRadius = 0.14f;
        public int bounces = 2;
        public float range = 40f;
        [Tooltip("Только для рельсы: задержка перед выстрелом")] public float chargeTime;
        [Tooltip("Выстрелов в очереди (у игрока); 1 — одиночные")] public int burst = 1;
        [Tooltip("Пауза между выстрелами очереди")] public float burstInterval = 0.09f;
        [Tooltip("Радиус эхо-волны при отскоке")] public float echoRadius = 6f;
        [Tooltip("Как далеко враги слышат выстрел")] public float noiseRadius = 14f;
        [Header("Внешний вид")]
        public int barrels = 1;
        public float barrelLength = 0.8f;
        public float barrelWidth = 0.14f;
        public float barrelSpacing = 0.16f;
        public float turretSize = 0.62f;
    }

    [Serializable]
    public class EnemyConfig
    {
        [Tooltip("Символ в файле комнат")] public string symbol = "t";
        public string title;
        public string hullId;
        public string turretId;
        public EnemyBehaviour behaviour = EnemyBehaviour.Static;
        [Tooltip("Цвет фар и сенсора: тип врага узнаётся по цвету")]
        [ColorUsage(false, true)] public Color accent;
        [Tooltip("0 = как у корпуса")] public int hpOverride;
        [Tooltip("Урон пули этого врага; 0 — как у пушки")] public int damageOverride;
        [Tooltip("Босс: крупнее, имя при входе, полоска здоровья, ярость на половине здоровья")] public bool boss;
        [Tooltip("Масштаб танка")] public float scale = 1f;
        public float fireInterval = 1.6f;
        [Tooltip("Ошибка прицела в градусах")] public float aimError = 4f;
        public float turretTurnSpeed = 150f;
        [Tooltip("Умеет стрелять через рикошет")] public bool bankShots;
        [Tooltip("Чувствует игрока вплотную, без звука")] public float senseRadius = 3.2f;
        [Tooltip("Множитель дальности слуха")] public float hearing = 1f;
        [Tooltip("Сколько секунд помнит, откуда был звук")] public float memory = 4f;
        public float speedMultiplier = 0.75f;
        [Tooltip("Видит игрока по свету его фар на этой дистанции (при тихом ходе — вдвое ближе)")] public float sight = 7f;
        [Tooltip("Стреляет на звук, даже не видя игрока (подавляющий огонь)")] public bool blindFire = true;
        [Tooltip("Охотник держит эту дистанцию и стрейфит")] public float preferRange = 6f;
        [Tooltip("Упреждение по движущейся цели: 0 — нет, 1 — полное")] public float lead = 0.8f;
        [Tooltip("Щит на башне отражает пули, летящие спереди: бить надо сбоку, через отскок")] public bool frontShield;
    }

    /// <summary>
    /// Все настройки игры в одном ассете: корпуса, пушки, враги, правила, цвета.
    /// Значения по умолчанию заданы здесь, а меняются в инспекторе (Assets/_Game/Configs/GameConfig.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "Пуля — дура/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Контент")]
        public TextAsset rooms;
        public List<HullConfig> hulls = DefaultHulls();
        public List<TurretConfig> turrets = DefaultTurrets();
        public List<EnemyConfig> enemies = DefaultEnemies();

        [Header("Материалы (создаёт билдер)")]
        public Material litMaterial;
        public Material unlitMaterial;
        public Material fxMaterial;

        [Header("Объекты на карте")]
        [Tooltip("Шрапнель красной бочки: разлетается кольцом и тоже отскакивает")]
        public TurretConfig shrapnel = new()
        {
            id = "shrapnel", title = "Шрапнель", damage = 1, projectileSpeed = 13f, projectileRadius = 0.08f,
            bounces = 1, range = 9f, echoRadius = 2.5f, noiseRadius = 10f,
        };
        public int shrapnelCount = 10;
        public float barrelRadius = 2.4f;
        public int barrelDamage = 3;
        [Tooltip("Радиус, на который ловушка «зовёт» врагов")] public float trapNoiseRadius = 26f;
        [Tooltip("Сколько секунд лежит выпавший бонус")] public float pickupLifetime = 18f;
        [Tooltip("Сколько секунд виден враг, помеченный пулей-меткой")] public float markDuration = 2f;

        [Header("Камера")]
        [Tooltip("1 — вся комната в кадре, меньше — ближе к танку")] public float cameraZoom = 0.68f;
        [Tooltip("Насколько камера смещается к прицелу")] public float cameraLean = 1.5f;

        [Header("Правила")]
        public int lives = 3;
        public float cellSize = 1.5f;
        public float wallHeight = 1.2f;

        [Header("Эхо")]
        public float waveSpeed = 18f;
        [Tooltip("За сколько секунд гаснет подсвеченная стена")] public float revealFade = 2.6f;
        [Tooltip("Радиус, в котором танк «нащупывает» стены")] public float touchRadius = 2.3f;
        public float startRevealSpeed = 26f;
        [Tooltip("Яркость контура стены, которую уже видели (0 — память выключена)")] public float memoryGlow = 0.11f;

        [Header("Шум двигателя")]
        [Tooltip("Доля скорости, выше которой танк шумит")] public float loudSpeed = 0.7f;
        [Tooltip("Скорость при тихом ходе (Shift)")] public float sneakSpeed = 0.5f;
        public float engineNoiseRadius = 7f;
        public float engineNoiseInterval = 0.5f;

        [Header("Награда за отскоки")]
        [Tooltip("На сколько растёт радиус эха с каждым отскоком")] public float echoGrowthPerBounce = 0.3f;
        [Tooltip("Яркость пули растёт с каждым отскоком")] public float glowGrowthPerBounce = 0.45f;

        [Header("Свет")]
        public Color playerLightColor = new(0.78f, 0.86f, 1f);
        public float playerLightRange = 6.5f;
        public float playerLightIntensity = 3.2f;
        public Color ambient = new(0.012f, 0.015f, 0.022f);

        [Header("Цвета мира")]
        public Color floorColor = new(0.07f, 0.075f, 0.09f);
        [ColorUsage(false, true)] public Color floorGridGlow = new(0.0025f, 0.0035f, 0.0055f);
        public Color wallColor = new(0.08f, 0.085f, 0.1f);
        public Color crateColor = new(0.12f, 0.12f, 0.14f);
        [ColorUsage(false, true)] public Color echoColor = new(0.5f, 0.72f, 1.1f);
        [ColorUsage(false, true)] public Color enemyEchoColor = new(0.95f, 0.08f, 0.1f);
        [ColorUsage(false, true)] public Color tempWallColor = new(0.35f, 0.6f, 1.1f);

        [Header("Цвета танков")]
        public Color playerBody = new(0.1f, 0.14f, 0.2f);
        [ColorUsage(false, true)] public Color playerAccent = new(0.42f, 0.7f, 1.3f);
        public Color enemyBody = new(0.16f, 0.07f, 0.07f);
        [ColorUsage(false, true)] public Color enemyAccent = new(1.6f, 0.15f, 0.1f);
        [ColorUsage(false, true)] public Color enemyReveal = new(0.9f, 0.08f, 0.06f);
        public Color trackColor = new(0.05f, 0.05f, 0.06f);

        [Header("Цвета снарядов")]
        [ColorUsage(false, true)] public Color bulletPlayer = new(0.6f, 1.05f, 1.8f);
        [Tooltip("Пуля игрока после отскока: теперь бьёт и его")]
        [ColorUsage(false, true)] public Color bulletDanger = new(1.7f, 0.18f, 0.28f);
        [ColorUsage(false, true)] public Color bulletEnemy = new(2.1f, 0.12f, 0.1f);

        public HullConfig Hull(string id) => hulls.Find(h => h.id == id) ?? hulls[0];
        public TurretConfig Turret(string id) => turrets.Find(t => t.id == id) ?? turrets[0];
        public EnemyConfig Enemy(char symbol) => enemies.Find(e => e.symbol.Length > 0 && e.symbol[0] == symbol);

        static List<HullConfig> DefaultHulls() => new()
        {
            new HullConfig
            {
                id = "strizh", title = "Стриж", style = HullStyle.Light, description = "Быстрый, 3 брони",
                maxHp = 4, speed = 6.6f, size = new Vector2(0.95f, 1.15f), height = 0.26f,
                ability = AbilityType.Dash, abilityTitle = "Рывок",
                abilityDescription = "таранит врагов и сбивает пули",
                abilityCooldown = 3f, abilityDuration = 0.35f
            },
            new HullConfig
            {
                id = "barsuk", title = "Барсук", style = HullStyle.Medium, description = "Средний, 5 брони",
                maxHp = 5, speed = 5f, size = new Vector2(1.1f, 1.3f), height = 0.32f,
                ability = AbilityType.Mirror, abilityTitle = "Зеркало",
                abilityDescription = "отражает пули · 2 за комнату",
                abilityCooldown = 6f, abilityDuration = 3f, abilityCharges = 2
            },
            new HullConfig
            {
                id = "begemot", title = "Бегемот", style = HullStyle.Heavy, description = "Медленный, 8 брони",
                maxHp = 8, speed = 3.8f, size = new Vector2(1.3f, 1.5f), height = 0.4f,
                ability = AbilityType.Wall, abilityTitle = "Стенка",
                abilityDescription = "укрытие · 2 за комнату",
                abilityCooldown = 8f, abilityDuration = 6f, abilityCharges = 2
            },
        };

        static List<TurretConfig> DefaultTurrets() => new()
        {
            new TurretConfig
            {
                id = "cannon", title = "Пушка", style = TurretStyle.Cannon, description = "2 отскока · большое эхо",
                mode = FireMode.Projectile, damage = 2, reload = 0.55f, projectileSpeed = 14f, projectileRadius = 0.14f,
                bounces = 2, range = 45f, echoRadius = 6.5f, noiseRadius = 14f,
                barrels = 1, barrelLength = 0.85f, barrelWidth = 0.16f, turretSize = 0.62f
            },
            new TurretConfig
            {
                id = "plasma", title = "Плазма", style = TurretStyle.Plasma, description = "очередь из 3 · 3 отскока",
                mode = FireMode.Projectile, damage = 1, reload = 0.62f, burst = 3, burstInterval = 0.09f, projectileSpeed = 11f, projectileRadius = 0.1f,
                bounces = 3, range = 30f, echoRadius = 3.2f, noiseRadius = 9f,
                barrels = 2, barrelLength = 0.7f, barrelWidth = 0.09f, barrelSpacing = 0.18f, turretSize = 0.58f
            },
            new TurretConfig
            {
                id = "shotgun", title = "Дробовик", style = TurretStyle.Shotgun, description = "веер из 6 дробин",
                mode = FireMode.Projectile, damage = 1, reload = 0.9f, pellets = 6, spread = 34f,
                projectileSpeed = 16f, projectileRadius = 0.09f, bounces = 1, range = 11f, echoRadius = 2.6f, noiseRadius = 12f,
                barrels = 1, barrelLength = 0.55f, barrelWidth = 0.26f, turretSize = 0.66f
            },
            new TurretConfig
            {
                id = "rail", title = "Рельса", style = TurretStyle.Rail, description = "заряд · луч прошивает насквозь",
                mode = FireMode.Rail, damage = 3, reload = 2.4f, chargeTime = 0.7f, projectileRadius = 0.12f,
                bounces = 2, range = 32f, echoRadius = 7f, noiseRadius = 22f,
                barrels = 2, barrelLength = 1.05f, barrelWidth = 0.07f, barrelSpacing = 0.12f, turretSize = 0.6f
            },
        };

        static List<EnemyConfig> DefaultEnemies() => new()
        {
            new EnemyConfig { symbol = "d", title = "Мишень", accent = new Color(0.81f, 0.81f, 0.81f), hullId = "barsuk", turretId = "cannon", behaviour = EnemyBehaviour.Dummy, hpOverride = 2 },
            new EnemyConfig { symbol = "r", title = "Новичок", accent = new Color(1.61f, 1.43f, 0.19f), hullId = "strizh", turretId = "cannon", behaviour = EnemyBehaviour.Static, hpOverride = 2, damageOverride = 1, fireInterval = 3f, aimError = 9f, turretTurnSpeed = 90f, sight = 8f, lead = 0f },
            new EnemyConfig { symbol = "t", title = "Турель", accent = new Color(1.86f, 0.74f, 0.09f), hullId = "begemot", turretId = "cannon", behaviour = EnemyBehaviour.Static, hpOverride = 4, damageOverride = 1, fireInterval = 1.8f, aimError = 5f, turretTurnSpeed = 120f, hearing = 1.3f, sight = 9f, lead = 0.5f },
            new EnemyConfig { symbol = "h", title = "Охотник", accent = new Color(1.86f, 0.19f, 0.12f), hullId = "strizh", turretId = "plasma", behaviour = EnemyBehaviour.Hunter, fireInterval = 0.9f, aimError = 7f, turretTurnSpeed = 200f, speedMultiplier = 0.7f, preferRange = 6f },
            new EnemyConfig { symbol = "g", title = "Громила", accent = new Color(0.99f, 0.25f, 1.86f), hullId = "barsuk", turretId = "shotgun", behaviour = EnemyBehaviour.Hunter, fireInterval = 1.6f, aimError = 4f, turretTurnSpeed = 160f, speedMultiplier = 0.8f, preferRange = 3.5f, sight = 6f },
            new EnemyConfig { symbol = "s", title = "Снайпер", accent = new Color(1.86f, 0.25f, 1.12f), hullId = "barsuk", turretId = "cannon", behaviour = EnemyBehaviour.Hunter, preferRange = 10f, speedMultiplier = 0.5f, bankShots = true, fireInterval = 2.4f, aimError = 1.5f, turretTurnSpeed = 110f, hearing = 1.7f, sight = 12f, blindFire = false, lead = 1f },
            new EnemyConfig { symbol = "q", title = "Щитоносец", accent = new Color(0.19f, 1.61f, 1.36f), hullId = "barsuk", turretId = "cannon", behaviour = EnemyBehaviour.Hunter, hpOverride = 5, frontShield = true, fireInterval = 1.9f, aimError = 4f, turretTurnSpeed = 130f, speedMultiplier = 0.7f, preferRange = 5f, sight = 8f },
            new EnemyConfig { symbol = "B", title = "Мамонт", accent = new Color(1.86f, 0.06f, 0.15f), hullId = "begemot", turretId = "rail", behaviour = EnemyBehaviour.Hunter, hpOverride = 14, boss = true, scale = 1.4f, bankShots = true, fireInterval = 2.6f, aimError = 2f, turretTurnSpeed = 90f, speedMultiplier = 0.85f, memory = 6f, preferRange = 7f, sight = 9f },
        };
    }
}
