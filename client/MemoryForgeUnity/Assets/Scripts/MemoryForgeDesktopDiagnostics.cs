using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MemoryForge.Client
{
    // Development-player startup evidence; never records account data or credentials.
    public sealed class MemoryForgeDesktopDiagnostics : MonoBehaviour
    {
#if DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (Application.platform == RuntimePlatform.OSXPlayer)
                new GameObject("DesktopStartupDiagnostics").AddComponent<MemoryForgeDesktopDiagnostics>();
        }
        IEnumerator Start()
        {
            yield return null; yield return null;
            Debug.Log($"MF_PC_BOOT scene={SceneManager.GetActiveScene().name} focus={Application.isFocused} background={Application.runInBackground} " +
                $"frame={Time.frameCount} lobby={FindObjectsByType<MemoryForgeLobbyClient>(FindObjectsSortMode.None).Length} canvases={FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length} screen={Screen.width}x{Screen.height}");
            yield return new WaitForSecondsRealtime(600);
            Destroy(gameObject);
        }
        void OnApplicationFocus(bool focused) => Debug.Log("MF_PC_FOCUS " + focused);
        void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            var system = EventSystem.current;
            var hits = new List<RaycastResult>();
            if (system != null) system.RaycastAll(new PointerEventData(system) { position = Input.mousePosition }, hits);
            Debug.Log($"MF_PC_POINTER focus={Application.isFocused} at={Input.mousePosition} ui={(hits.Count == 0 ? "none" : hits[0].gameObject.name)} events={(system == null ? "none" : system.currentInputModule?.GetType().Name)}");
        }
#endif
    }
}
