#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

// The existing UnityWindowCapture captures this namespace's actual editor windows only.
// This evidence-only view displays AssetDatabase's real imported image, never substitute pixels.
namespace GameCore.Studio.UI
{
    public sealed class P42hImportedImageWindow : EditorWindow
    {
        [SerializeField] private string assetPath = string.Empty;

        public static void Open(string path)
        {
            var window = GetWindow<P42hImportedImageWindow>("P4.2h Imported Image");
            window.assetPath = path;
            window.minSize = new Vector2(340, 380);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Actual imported asset", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(assetPath, EditorStyles.wordWrappedLabel);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            Rect region = GUILayoutUtility.GetRect(300, 300, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            if (texture == null)
            {
                GUI.Label(region, File.Exists(assetPath) ? "Asset file exists; Texture2D not loaded" : "Asset absent (not generated or undone)");
                return;
            }
            GUI.DrawTexture(region, texture, ScaleMode.ScaleToFit, true);
        }
    }
}
