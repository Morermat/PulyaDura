using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Генератор комнат для бесконечного режима. Выдаёт ту же текстовую карту, что и rooms.txt,
    /// поэтому строится обычным LevelBuilder. Гарантии:
    /// проходы не уже двух клеток, вся комната достижима, враги не видят точку старта напрямую.
    /// Сложность растёт с глубиной: больше врагов, новые типы, бочки, сирены, порталы, босс каждые 6 комнат.
    /// </summary>
    public static class RoomGenerator
    {
        // Заготовки препятствий (клетки стены относительно угла)
        static readonly Vector2Int[][] Stamps =
        {
            new[] { V(0, 0) },                                                      // колонна
            new[] { V(0, 0), V(1, 0), V(0, 1), V(1, 1) },                           // большая круглая колонна
            new[] { V(0, 0), V(1, 0), V(2, 0) },                                    // короткая стенка
            new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0) },                  // длинная стенка
            new[] { V(0, 0), V(1, 0), V(2, 0), V(0, 1), V(0, 2) },                  // угол
            new[] { V(0, 0), V(1, 0), V(2, 0), V(1, 1), V(1, 2) },                  // буква Т
            new[] { V(0, 0), V(1, 0), V(2, 0), V(0, 1), V(1, 1), V(2, 1) },         // блок
        };

        static readonly AudioReverbPreset[] Reverbs =
        {
            AudioReverbPreset.Room, AudioReverbPreset.StoneCorridor, AudioReverbPreset.Cave,
            AudioReverbPreset.Hangar, AudioReverbPreset.Hallway, AudioReverbPreset.Arena, AudioReverbPreset.Auditorium,
        };

        // Подсказки: общие и те, что про предмет или врага (символ), — только если он есть в комнате
        static readonly (char need, string text)[] Tips =
        {
            ('.', "Отскок подсвечивает стены"), ('.', "Shift — тихий ход"), ('.', "Своя пуля после отскока бьёт и тебя"),
            ('y', "Белый ящик — бонус"), ('x', "Бочка взрывается от пули"), ('1', "Портал переносит и пули"),
            ('n', "Сирена зовёт врагов к себе"), ('=', "Решётка пропускает пули, но не танки"), ('q', "Щит спереди — бей в бок"), ('s', "Снайпер бьёт через отскок"),
        };

        // Враги: символ, с какой глубины, стоимость
        static readonly (char symbol, int depth, float cost)[] Roster =
        {
            ('r', 1, 1f), ('t', 1, 1.5f), ('h', 2, 2f), ('g', 3, 2f), ('s', 4, 2.5f), ('q', 5, 3f),
        };

        static Vector2Int V(int x, int y) => new(x, y);

        /// <summary>Мешает проехать: стена или решётка.</summary>
        static bool Solid(char ch) => ch == '#' || ch == '=';

        /// <summary>Комната на глубине depth (с 1). Одинаковые seed и depth дают одинаковую комнату.</summary>
        public static RoomData Generate(int depth, int seed)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var rng = new System.Random(seed * 7919 + depth * 104729 + attempt);
                var room = TryGenerate(depth, rng);
                if (room != null) return room;
            }
            FallbackCount++;
            return Fallback(depth);
        }

        /// <summary>Сколько раз пришлось отдать запасную комнату (для автотеста).</summary>
        public static int FallbackCount { get; private set; }

        static RoomData TryGenerate(int depth, System.Random rng)
        {
            int grow = Mathf.Min(depth / 6, 2);
            int w = 21 + grow * 4, h = 13 + grow * 2;
            var g = new char[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    g[x, y] = x == 0 || y == 0 || x == w - 1 || y == h - 1 ? '#' : '.';

            // 1. Препятствия: каждая заготовка либо прилегает к стене вплотную, либо отстоит от всего минимум на 2 клетки
            int targetWalls = Mathf.RoundToInt((w - 2) * (h - 2) * (0.1f + 0.06f * (float)rng.NextDouble()));
            int placed = 0;
            for (int tries = 0; tries < 400 && placed < targetWalls; tries++)
            {
                var stamp = Transform(Stamps[rng.Next(Stamps.Length)], rng.Next(8));
                int ox = rng.Next(1, w - 1), oy = rng.Next(1, h - 1);
                if (!CanPlace(g, w, h, stamp, ox, oy)) continue;
                foreach (var c in stamp) g[ox + c.x, oy + c.y] = '#';
                if (HasPinch(g, w, h))
                {
                    foreach (var c in stamp) g[ox + c.x, oy + c.y] = '.';
                    continue;
                }
                placed += stamp.Length;
            }

            // Решётки: прямые отрезки, через которые можно стрелять, но нельзя проехать
            if (depth >= 2)
            {
                int fences = 1 + rng.Next(Mathf.Min(3, depth / 3 + 1));
                for (int tries = 0, done = 0; tries < 120 && done < fences; tries++)
                {
                    int length = 2 + rng.Next(3);
                    bool vertical = rng.Next(2) == 0;
                    var line = new Vector2Int[length];
                    for (int i = 0; i < length; i++) line[i] = vertical ? V(0, i) : V(i, 0);
                    int ox = rng.Next(1, w - 1), oy = rng.Next(1, h - 1);
                    if (!CanPlace(g, w, h, line, ox, oy)) continue;
                    foreach (var c in line) g[ox + c.x, oy + c.y] = '=';
                    if (HasPinch(g, w, h))
                    {
                        foreach (var c in line) g[ox + c.x, oy + c.y] = '.';
                        continue;
                    }
                    done++;
                }
            }

            // 2. Старт игрока: левая четверть, вокруг пусто
            var open = new List<Vector2Int>();
            for (int x = 1; x < w - 1; x++)
                for (int y = 1; y < h - 1; y++)
                    if (FreeAround(g, w, h, x, y, 1)) open.Add(V(x, y));
            var starts = open.FindAll(c => c.x <= w / 4);
            if (starts.Count == 0) return null;
            var player = starts[rng.Next(starts.Count)];

            // Всё свободное должно быть достижимо
            var reach = Flood(g, w, h, player);
            for (int x = 1; x < w - 1; x++)
                for (int y = 1; y < h - 1; y++)
                    if (g[x, y] == '.' && !reach[x, y]) return null;
            g[player.x, player.y] = 'P';

            // 3. Враги по бюджету. Не ближе 8 клеток к старту и без прямой видимости, если ближе 13
            float budget = Mathf.Min(2f + depth * 0.9f, 15f);
            int maxEnemies = Mathf.Min(3 + depth / 2, 9);
            var spots = open.FindAll(c => (c - player).magnitude >= 8f && (!Visible(g, w, h, c, player) || (c - player).magnitude >= 13f));
            Shuffle(spots, rng);
            var enemyCells = new List<Vector2Int>();
            void PutEnemy(char symbol)
            {
                foreach (var c in spots)
                {
                    if (g[c.x, c.y] != '.' || enemyCells.Exists(e => (e - c).sqrMagnitude < 9)) continue;
                    g[c.x, c.y] = symbol;
                    enemyCells.Add(c);
                    return;
                }
            }
            if (depth % 6 == 0) { PutEnemy('B'); budget -= 6f; }
            var pool = new List<(char symbol, int depth, float cost)>();
            foreach (var e in Roster) if (e.depth <= depth) pool.Add(e);
            int guard = 0;
            while (budget > 0.9f && enemyCells.Count < maxEnemies && guard++ < 50)
            {
                var pick = pool[rng.Next(pool.Count)];
                if (pick.cost > budget + 0.5f) continue;
                int before = enemyCells.Count;
                PutEnemy(pick.symbol);
                if (enemyCells.Count == before) break;
                budget -= pick.cost;
            }
            if (enemyCells.Count == 0) return null;

            // 4. Предметы — только на открытых местах (5×5 без стен), чтобы не сужать проходы
            var roomy = open.FindAll(c => FreeAround(g, w, h, c.x, c.y, 2) && (c - player).sqrMagnitude > 4);
            Shuffle(roomy, rng);
            int next = 0;
            Vector2Int? Take(System.Func<Vector2Int, bool> ok = null)
            {
                for (; next < roomy.Count; next++)
                {
                    var c = roomy[next];
                    if (!FreeOfItems(g, c) || (ok != null && !ok(c))) continue;
                    next++;
                    return c;
                }
                return null;
            }
            void Put(Vector2Int? c, char symbol) { if (c.HasValue) g[c.Value.x, c.Value.y] = symbol; }

            int crates = 2 + rng.Next(3);
            for (int i = 0; i < crates; i++) Put(Take(), 'c');
            Put(Take(), 'y');
            if (depth >= 4) Put(Take(), 'y');
            if (depth >= 2)
            {
                int barrels = 1 + rng.Next(Mathf.Min(3, depth / 2 + 1));
                for (int i = 0; i < barrels; i++) Put(Take(c => enemyCells.Exists(e => (e - c).sqrMagnitude <= 18)) ?? Take(), 'x');
            }
            // Скрытые ловушки — вплотную к стенам, там, где отскоки чаще всего дают эхо
            if (depth >= 2)
            {
                var nearWalls = new List<Vector2Int>();
                for (int x = 1; x < w - 1; x++)
                    for (int y = 1; y < h - 1; y++)
                    {
                        if (g[x, y] != '.' || (V(x, y) - player).sqrMagnitude < 16) continue;
                        bool wall = g[x + 1, y] == '#' || g[x - 1, y] == '#' || g[x, y + 1] == '#' || g[x, y - 1] == '#';
                        if (wall) nearWalls.Add(V(x, y));
                    }
                Shuffle(nearWalls, rng);
                int traps = Mathf.Min(1 + depth / 3, 4), done = 0;
                foreach (var c in nearWalls)
                {
                    if (done >= traps) break;
                    bool clear = true;
                    for (int dx = -1; dx <= 1 && clear; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            if (g[c.x + dx, c.y + dy] != '.' && g[c.x + dx, c.y + dy] != '#') { clear = false; break; }
                    if (!clear) continue;
                    g[c.x, c.y] = 'n';
                    done++;
                }
            }
            if (depth >= 3 && rng.NextDouble() < 0.4)
            {
                var a = Take(c => c.x < w / 2);
                var b = a.HasValue ? Take(c => c.x >= w / 2 && (c - a.Value).magnitude >= 10f) : null;
                if (a.HasValue && b.HasValue) { Put(a, '1'); Put(b, '1'); }
            }

            var rows = new string[h];
            var sb = new System.Text.StringBuilder();
            for (int y = 0; y < h; y++)
            {
                sb.Clear();
                for (int x = 0; x < w; x++) sb.Append(g[x, y]);
                rows[y] = sb.ToString();
            }
            return new RoomData
            {
                title = $"Глубина {depth}",
                // В бесконечном режиме подсказки не мешают: только предупреждение о боссе
                hint = depth % 6 == 0 ? "Босс: красная линия — сейчас выстрел" : "",
                rows = rows,
                width = w,
                height = h,
                reverb = Reverbs[rng.Next(Reverbs.Length)],
            };
        }

        /// <summary>Запасная комната, если генератор не справился: пустой зал с колоннами.</summary>
        static RoomData Fallback(int depth)
        {
            string[] rows =
            {
                "#####################",
                "#...................#",
                "#...................#",
                "#....#....#....#....#",
                "#...................#",
                "#.............t.....#",
                "#.P.......#......h..#",
                "#.............r.....#",
                "#...................#",
                "#....#....#....#....#",
                "#...................#",
                "#...................#",
                "#####################",
            };
            return new RoomData { title = $"Глубина {depth}", hint = "Отскок подсвечивает стены", rows = rows, width = 21, height = 13 };
        }

        /// <summary>Клетка и её соседи пусты: предметы не слипаются в кучу и не стоят вплотную к врагам.</summary>
        static bool FreeOfItems(char[,] g, Vector2Int c)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (g[c.x + dx, c.y + dy] != '.') return false;
            return true;
        }

        static string PickTip(string[] rows, System.Random rng)
        {
            var options = new List<string>();
            foreach (var (need, text) in Tips)
                if (need == '.' || System.Array.Exists(rows, r => r.IndexOf(need) >= 0)) options.Add(text);
            return options[rng.Next(options.Count)];
        }

        static Vector2Int[] Transform(Vector2Int[] stamp, int variant)
        {
            var result = new Vector2Int[stamp.Length];
            for (int i = 0; i < stamp.Length; i++)
            {
                var c = stamp[i];
                if ((variant & 1) != 0) c = V(c.y, c.x);
                if ((variant & 2) != 0) c = V(-c.x, c.y);
                if ((variant & 4) != 0) c = V(c.x, -c.y);
                result[i] = c;
            }
            return result;
        }

        static bool CanPlace(char[,] g, int w, int h, Vector2Int[] stamp, int ox, int oy)
        {
            bool touchesBorder = false;
            foreach (var s in stamp)
            {
                int x = ox + s.x, y = oy + s.y;
                if (x < 1 || y < 1 || x > w - 2 || y > h - 2) return false;
                int border = Mathf.Min(Mathf.Min(x, y), Mathf.Min(w - 1 - x, h - 1 - y));
                if (border == 1) touchesBorder = true;
            }
            foreach (var s in stamp)
            {
                int x = ox + s.x, y = oy + s.y;
                int border = Mathf.Min(Mathf.Min(x, y), Mathf.Min(w - 1 - x, h - 1 - y));
                if (border == 2 && !touchesBorder) return false; // щель в клетку у внешней стены
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx <= 0 || ny <= 0 || nx >= w - 1 || ny >= h - 1) continue;
                        if (Solid(g[nx, ny])) return false; // до других препятствий не меньше двух клеток
                    }
            }
            return true;
        }

        /// <summary>Щель в одну клетку: две стены через клетку, которые не соединены стеной поблизости.</summary>
        public static bool HasPinch(char[,] g, int w, int h)
        {
            bool Wall(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && Solid(g[x, y]);
            var seen = new HashSet<Vector2Int>();
            var stack = new Stack<Vector2Int>();
            for (int ax = 0; ax < w; ax++)
                for (int ay = 0; ay < h; ay++)
                {
                    if (!Wall(ax, ay)) continue;
                    for (int dx = -2; dx <= 2; dx++)
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != 2 || !Wall(ax + dx, ay + dy)) continue;
                            int x0 = Mathf.Min(ax, ax + dx) - 1, x1 = Mathf.Max(ax, ax + dx) + 1;
                            int y0 = Mathf.Min(ay, ay + dy) - 1, y1 = Mathf.Max(ay, ay + dy) + 1;
                            seen.Clear();
                            stack.Clear();
                            var a = V(ax, ay);
                            seen.Add(a);
                            stack.Push(a);
                            bool linked = false;
                            while (stack.Count > 0 && !linked)
                            {
                                var c = stack.Pop();
                                for (int ex = -1; ex <= 1; ex++)
                                    for (int ey = -1; ey <= 1; ey++)
                                    {
                                        var n = V(c.x + ex, c.y + ey);
                                        if (n.x < x0 || n.x > x1 || n.y < y0 || n.y > y1 || !Wall(n.x, n.y) || !seen.Add(n)) continue;
                                        if (n.x == ax + dx && n.y == ay + dy) linked = true;
                                        stack.Push(n);
                                    }
                            }
                            if (!linked) return true;
                        }
                }
            return false;
        }

        static bool FreeAround(char[,] g, int w, int h, int x, int y, int r)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || Solid(g[nx, ny])) return false;
                }
            return true;
        }

        static bool[,] Flood(char[,] g, int w, int h, Vector2Int start)
        {
            var seen = new bool[w, h];
            var queue = new Queue<Vector2Int>();
            seen[start.x, start.y] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in new[] { V(1, 0), V(-1, 0), V(0, 1), V(0, -1) })
                {
                    var n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h || seen[n.x, n.y] || Solid(g[n.x, n.y])) continue;
                    seen[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        /// <summary>Прямая видимость между центрами клеток (шаг по 0.25 клетки).</summary>
        static bool Visible(char[,] g, int w, int h, Vector2Int a, Vector2Int b)
        {
            Vector2 d = b - a;
            int steps = Mathf.CeilToInt(d.magnitude * 4f);
            for (int i = 1; i < steps; i++)
            {
                Vector2 p = (Vector2)a + d * (i / (float)steps);
                int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                if (g[x, y] == '#') return false;
            }
            return true;
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
