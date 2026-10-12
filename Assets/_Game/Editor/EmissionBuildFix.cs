using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PulyaDura.EditorTools
{
    /// <summary>
    /// Стены и ящики светятся через вариант шейдера URP Lit с ключевым словом _EMISSION,
    /// а включает его код во время игры. При сборке Unity выбрасывает варианты шейдеров, которых нет
    /// ни в одном сохранённом материале, — и в .exe стены остаются чёрными. Редактор иногда сам снимает
    /// _EMISSION с Lit.mat при пересохранении, поэтому перед каждой сборкой (из любого меню, в том числе
    /// File → Build Profiles) включаем его заново.
    /// </summary>
    public class EmissionBuildFix : IPreprocessBuildWithReport
    {
        const string LitPath = "Assets/_Game/Materials/Lit.mat";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Apply();

        public static void Apply()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(LitPath);
            if (mat == null)
            {
                Debug.LogWarning($"[EmissionBuildFix] Не найден {LitPath}");
                return;
            }
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            // Заметно не чёрный цвет: с чёрным URP считает свечение выключенным и снимает ключевое слово.
            // В игре цвет свечения всё равно задаёт код (Mats), так что на вид это не влияет
            mat.SetColor("_EmissionColor", new Color(0.05f, 0.05f, 0.05f));
            mat.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log($"[EmissionBuildFix] _EMISSION в Lit.mat: {mat.IsKeywordEnabled("_EMISSION")}");
        }
    }
}
