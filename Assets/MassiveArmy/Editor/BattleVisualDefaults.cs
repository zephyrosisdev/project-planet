using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    [InitializeOnLoad] public static class BattleVisualDefaults
    {
        static BattleVisualDefaults(){EditorApplication.delayCall+=Ensure;}
        static void Ensure()
        {
            const string path="Assets/MassiveArmy/Resources/MassiveArmy/BattleVisualSettings.asset";
            if(AssetDatabase.LoadAssetAtPath<BattleVisualSettings>(path)!=null)return;
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BattleVisualSettings>(),path);AssetDatabase.SaveAssets();
        }
        [MenuItem("Massive Army/Art/Battle Atmosphere and Fog Settings")]
        public static void SelectSettings(){Ensure();Selection.activeObject=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");EditorGUIUtility.PingObject(Selection.activeObject);}
    }
}