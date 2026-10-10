using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public enum AbilityType { Dash, Mirror, Wall }
    public enum FireMode { Projectile, Rail }
    public enum EnemyBehaviour { Dummy, Static, Hunter }

    [Serializable]
    public class HullConfig
    {
        public string id;
        public string title;
        [TextArea] public string description;
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
    }

    [Serializable]
    public class TurretConfig
    {
        public string id;
        public string title;
        [TextArea] public string description;
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
        [Tooltip("0 = как у корпуса")] public int hpOverride;
        public float fireInterval = 1.6f;
        [Tooltip("Ошибка прицела в градусах")] public float aimError = 4f;
        public float turretTurnSpeed = 150f;
        [Tooltip("Умеет стрелять через рикошет")] public bool bankShots;
        [Tooltip("Чувствует игрока вплотную, без звука")] public float senseRadius = 3.2f;
        [Tooltip("Сколько секунд помнит, откуда был звук")] public float memory = 4f;
        public float speedMultiplier = 0.75f;
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

        [Header("Правила")]
        public int lives = 3;
        public float cellSize = 1.5f;
        public float wallHeight = 1.2f;

        [Header("Эхо")]
        public float waveSpeed = 18f;
        [Tooltip("За сколько секунд гаснет подсвеченная стена")] public float revealFade = 2.6f;
        [Tooltip("Радиус, в котором танк «нащупывает» стены")] public float touchRadius = 2.3f;
        public float startRevealSpeed = 26f;

        [Header("Свет")]
        public Color playerLightColor = new(1f, 0.9f, 0.78f);
        public float playerLightRange = 6.5f;
        public float playerLightIntensity = 3.2f;
        public Color ambient = new(0.025f, 0.03f, 0.045f);

        [Header("Цвета мира")]
        public Color floorColor = new(0.07f, 0.075f, 0.09f);
        [ColorUsage(false, true)] public Color floorGridGlow = new(0.012f, 0.02f, 0.035f);
        public Color wallColor = new(0.08f, 0.085f, 0.1f);
        public Color crateColor = new(0.17f, 0.11f, 0.06f);
        [ColorUsage(false, true)] public Color echoColor = new(0.25f, 0.85f, 1.5f);
        [ColorUsage(false, true)] public Color enemyEchoColor = new(1.5f, 0.22f, 0.3f);
        [ColorUsage(false, true)] public Color tempWallColor = new(1.6f, 0.9f, 0.2f);

        [Header("Цвета танков")]
        public Color playerBody = new(0.16f, 0.4f, 0.36f);
        [ColorUsage(false, true)] public Color playerAccent = new(0.3f, 2.4f, 1.7f);
        public Color enemyBody = new(0.3f, 0.11f, 0.1f);
        [ColorUsage(false, true)] public Color enemyAccent = new(3f, 0.35f, 0.2f);
        [ColorUsage(false, true)] public Color enemyReveal = new(1.4f, 0.16f, 0.1f);
        public Color trackColor = new(0.05f, 0.05f, 0.06f);

        [Header("Цвета снарядов")]
        [ColorUsage(false, true)] public Color bulletPlayer = new(0.45f, 2.2f, 3f);
        [Tooltip("Пуля игрока после отскока: теперь бьёт и его")]
        [ColorUsage(false, true)] public Color bulletDanger = new(3f, 1.2f, 0.2f);
        [ColorUsage(false, true)] public Color bulletEnemy = new(3f, 0.3f, 0.25f);

        public HullConfig Hull(string id) => hulls.Find(h => h.id == id) ?? hulls[0];
        public TurretConfig Turret(string id) => turrets.Find(t => t.id == id) ?? turrets[0];
        public EnemyConfig Enemy(char symbol) => enemies.Find(e => e.symbol.Length > 0 && e.symbol[0] == symbol);

        static List<HullConfig> DefaultHulls() => new()
        {
            new HullConfig
            {
                id = "strizh", title = "Стриж", description = "Лёгкий и быстрый. Мало брони.",
                maxHp = 3, speed = 6.6f, size = new Vector2(0.95f, 1.15f), height = 0.26f,
                ability = AbilityType.Dash, abilityTitle = "Рывок",
                abilityDescription = "Резкий рывок. Пока летишь, пули проходят насквозь.",
                abilityCooldown = 2.5f, abilityDuration = 0.25f
            },
            new HullConfig
            {
                id = "barsuk", title = "Барсук", description = "Средний. Баланс скорости и брони.",
                maxHp = 5, speed = 5f, size = new Vector2(1.1f, 1.3f), height = 0.32f,
                ability = AbilityType.Mirror, abilityTitle = "Зеркало",
                abilityDescription = "2,5 с броня отражает пули. Отражённая пуля становится твоей.",
                abilityCooldown = 7f, abilityDuration = 2.5f
            },
            new HullConfig
            {
                id = "begemot", title = "Бегемот", description = "Тяжёлый и медленный. Держит много попаданий.",
                maxHp = 8, speed = 3.8f, size = new Vector2(1.3f, 1.5f), height = 0.4f,
                ability = AbilityType.Wall, abilityTitle = "Стенка",
                abilityDescription = "Ставит светящуюся стену на 6 с: укрытие и новый борт для рикошета.",
                abilityCooldown = 8f, abilityDuration = 6f
            },
        };

        static List<TurretConfig> DefaultTurrets() => new()
        {
            new TurretConfig
            {
                id = "cannon", title = "Пушка", description = "Тяжёлый снаряд, 2 отскока. Каждый отскок даёт большую волну эха.",
                mode = FireMode.Projectile, damage = 2, reload = 0.55f, projectileSpeed = 14f, projectileRadius = 0.14f,
                bounces = 2, range = 45f, echoRadius = 6.5f, noiseRadius = 14f,
                barrels = 1, barrelLength = 0.85f, barrelWidth = 0.16f, turretSize = 0.62f
            },
            new TurretConfig
            {
                id = "plasma", title = "Плазма", description = "Очередь мелких шаров, 3 отскока. Много маленьких вспышек.",
                mode = FireMode.Projectile, damage = 1, reload = 0.17f, projectileSpeed = 11f, projectileRadius = 0.1f,
                bounces = 3, range = 30f, echoRadius = 3.2f, noiseRadius = 9f,
                barrels = 2, barrelLength = 0.7f, barrelWidth = 0.09f, barrelSpacing = 0.18f, turretSize = 0.58f
            },
            new TurretConfig
            {
                id = "shotgun", title = "Дробовик", description = "Веер из 6 дробин, 1 отскок. Освещает всё перед тобой.",
                mode = FireMode.Projectile, damage = 1, reload = 0.9f, pellets = 6, spread = 34f,
                projectileSpeed = 16f, projectileRadius = 0.09f, bounces = 1, range = 11f, echoRadius = 2.6f, noiseRadius = 12f,
                barrels = 1, barrelLength = 0.55f, barrelWidth = 0.26f, turretSize = 0.66f
            },
            new TurretConfig
            {
                id = "rail", title = "Рельса", description = "Заряд 0,5 с, мгновенный луч с 3 отскоками. Пробивает танки насквозь.",
                mode = FireMode.Rail, damage = 4, reload = 1.8f, chargeTime = 0.5f, projectileRadius = 0.12f,
                bounces = 3, range = 40f, echoRadius = 5f, noiseRadius = 18f,
                barrels = 2, barrelLength = 1.05f, barrelWidth = 0.07f, barrelSpacing = 0.12f, turretSize = 0.6f
            },
        };

        static List<EnemyConfig> DefaultEnemies() => new()
        {
            new EnemyConfig { symbol = "d", title = "Мишень", hullId = "barsuk", turretId = "cannon", behaviour = EnemyBehaviour.Dummy, hpOverride = 2 },
            new EnemyConfig { symbol = "t", title = "Турель", hullId = "begemot", turretId = "cannon", behaviour = EnemyBehaviour.Static, hpOverride = 4, fireInterval = 1.8f, aimError = 5f, turretTurnSpeed = 120f },
            new EnemyConfig { symbol = "h", title = "Охотник", hullId = "strizh", turretId = "plasma", behaviour = EnemyBehaviour.Hunter, fireInterval = 0.9f, aimError = 7f, turretTurnSpeed = 200f, speedMultiplier = 0.7f },
            new EnemyConfig { symbol = "g", title = "Громила", hullId = "barsuk", turretId = "shotgun", behaviour = EnemyBehaviour.Hunter, fireInterval = 1.6f, aimError = 4f, turretTurnSpeed = 160f, speedMultiplier = 0.8f },
            new EnemyConfig { symbol = "s", title = "Снайпер", hullId = "barsuk", turretId = "cannon", behaviour = EnemyBehaviour.Static, bankShots = true, fireInterval = 2.4f, aimError = 1.5f, turretTurnSpeed = 110f },
            new EnemyConfig { symbol = "B", title = "Бегемот", hullId = "begemot", turretId = "rail", behaviour = EnemyBehaviour.Hunter, hpOverride = 14, bankShots = true, fireInterval = 2.6f, aimError = 2f, turretTurnSpeed = 90f, speedMultiplier = 0.85f, memory = 6f },
        };
    }
}
