// GameCore.Gameplay.Npc - NpcBubble: the name/state bubble above an NPC view (P1.3).
#nullable enable
using UnityEngine;

namespace GameCore.Gameplay.Npc
{
    /// <summary>
    /// A world-space text bubble on an NPC prefab (a TextMesh child). NpcBubbleBinder writes its text from committed
    /// state; the bubble only turns toward the main camera. Without a TextMesh it does nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcBubble : MonoBehaviour
    {
        [SerializeField] private TextMesh? text;

        [SerializeField] private float height = 2.3f;

        public TextMesh? Text => text;

        public float Height => height;

        public string Shown => text != null ? text.text : string.Empty;

        public void Configure(TextMesh? label, float bubbleHeight)
        {
            text = label;
            height = bubbleHeight;
        }

        public void Show(string value)
        {
            if (text != null && text.text != value)
            {
                text.text = value;
            }
        }

        private void LateUpdate()
        {
            Camera? main = Camera.main;
            if (text == null || main == null)
            {
                return;
            }

            Transform label = text.transform;
            label.rotation = Quaternion.LookRotation(label.position - main.transform.position, Vector3.up);
        }
    }
}
