using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MemoryForge.EditorTests
{
    public class EditorBridgeTests
    {
        private const string PrefabPath = "Assets/Prefabs/MemoryForgeConnectionCanvas.prefab";
        private const string ScenePath = "Assets/Scenes/MemoryForgeMcpSmoke.unity";

        [Test]
        public void UniversalPipelineIncludesTheTwoDimensionalRenderer()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(pipeline.GetType().Name, Is.EqualTo("UniversalRenderPipelineAsset"));
            var renderers = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            Assert.That(renderers, Is.Not.Null);
            Assert.That(Enumerable.Range(0, renderers.arraySize).Any(index =>
                renderers.GetArrayElementAtIndex(index).objectReferenceValue?.GetType().Name == "Renderer2DData"), Is.True);
        }

        [Test]
        public void SerializedConnectionPrefabCanRenderAnOverlay()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(asset, Is.Not.Null);
            var instance = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Assert.That(instance.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(instance.GetComponent<CanvasScaler>().uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(instance.GetComponent<GraphicRaycaster>(), Is.Not.Null);
                var text = instance.GetComponentInChildren<Text>(true);
                Assert.That(text, Is.Not.Null);
                Assert.That(text.font, Is.Not.Null);
                Assert.That(text.text, Is.Not.Empty);
                Assert.That(text.rectTransform.rect.width, Is.GreaterThan(0));
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
        }

        [Test]
        public void SavedSceneKeepsItsLinkedCanvasAndCamera()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.isLoaded;
            try
            {
                if (openedForTest) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                var canvas = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).Single();
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(canvas), Is.EqualTo(PrefabPath));
                Assert.That(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                    .Any(camera => camera.CompareTag("MainCamera")), Is.True);
            }
            finally { if (openedForTest) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
