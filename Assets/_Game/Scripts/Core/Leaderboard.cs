using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    [Serializable]
    public class RunRecord
    {
        public int score;
        public float time;
        public int rooms;
        public bool win;
        public int kills;
        public string hull;
        public string turret;
        public long ticks;

        public string Date => new DateTime(ticks).ToString("dd.MM HH:mm");
    }

    /// <summary>
    /// Таблица забегов на этом компьютере: топ-10 по очкам, хранится в PlayerPrefs как JSON.
    /// Очки: убийство 100, +50 за каждый отскок, серия +50 за каждое убийство подряд,
    /// комната +250, бонус +25, смерть −150, победа даёт бонус за скорость (10 очков за каждую секунду быстрее 5 минут).
    /// </summary>
    public static class Leaderboard
    {
        const string Key = "leaderboard_v1";
        const int Size = 10;

        [Serializable]
        class Store { public List<RunRecord> runs = new(); }

        public static List<RunRecord> Top
        {
            get
            {
                try
                {
                    var json = PlayerPrefs.GetString(Key, "");
                    var store = string.IsNullOrEmpty(json) ? new Store() : JsonUtility.FromJson<Store>(json);
                    return store?.runs ?? new List<RunRecord>();
                }
                catch (Exception)
                {
                    return new List<RunRecord>(); // повреждённые данные не должны ломать игру
                }
            }
        }

        /// <summary>Добавить забег. Возвращает место в таблице (1..10) или 0, если не попал.</summary>
        public static int Add(RunRecord record)
        {
            var runs = Top;
            runs.Add(record);
            runs.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : a.time.CompareTo(b.time));
            int place = runs.IndexOf(record) + 1;
            if (runs.Count > Size) runs.RemoveRange(Size, runs.Count - Size);
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Store { runs = runs }));
            PlayerPrefs.Save();
            return place <= Size ? place : 0;
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
