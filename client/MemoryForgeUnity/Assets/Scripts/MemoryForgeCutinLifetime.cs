#nullable enable
using UnityEngine;

namespace MemoryForge.Client
{
    internal sealed class MemoryForgeCutinLifetime : MonoBehaviour
    {
        internal Sprite? Plate;
        void OnDisable() => Destroy(gameObject);
        void OnDestroy() { if (Plate != null) Destroy(Plate); }
    }
}
