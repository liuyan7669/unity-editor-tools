using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Cowart.EditorUtilities.Editor
{
    /// <summary>扫描已加载场景、Prefab 和 ScriptableObject 中的丢失脚本与对象引用。</summary>
    public sealed class MissingReferenceScannerWindow : EditorWindow
    {
        private enum IssueKind
        {
            MissingScript,
            MissingObjectReference
        }

        private sealed class ScanIssue
        {
            public IssueKind Kind;
            public string AssetPath;
            public string HierarchyPath;
            public string ComponentName;
            public string PropertyPath;
            public GameObject SceneObject;
            public int[] PrefabSiblingPath;

            public string Description => Kind == IssueKind.MissingScript
                ? "Missing Script"
                : ComponentName + "." + PropertyPath;
        }

        private bool scanLoadedScenes = true;
        private bool scanPrefabs = true;
        private bool scanScriptableObjects = true;
        private Vector2 scrollPosition;
        private readonly List<ScanIssue> issues = new List<ScanIssue>();
        private string status = "尚未扫描。";

        [MenuItem("Cowart/场景/丢失引用扫描")]
        private static void Open()
        {
            GetWindow<MissingReferenceScannerWindow>("丢失引用扫描").minSize = new Vector2(760f, 460f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("扫描不会修改资源；只有“移除 Missing Script”会写入场景或 Prefab，并会再次确认。", MessageType.Info);
            scanLoadedScenes = EditorGUILayout.ToggleLeft("扫描当前已加载场景", scanLoadedScenes);
            scanPrefabs = EditorGUILayout.ToggleLeft("扫描 Assets 中的 Prefab", scanPrefabs);
            scanScriptableObjects = EditorGUILayout.ToggleLeft("扫描 Assets 中的 ScriptableObject", scanScriptableObjects);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("开始扫描", GUILayout.Height(28f))) Scan();
            int missingScriptCount = issues.Count(issue => issue.Kind == IssueKind.MissingScript);
            using (new EditorGUI.DisabledScope(missingScriptCount == 0))
            {
                if (GUILayout.Button("移除全部 Missing Script（" + missingScriptCount + "）", GUILayout.Height(28f)))
                {
                    RemoveMissingScripts(missingScriptCount);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(status, EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < issues.Count; i++) DrawIssue(issues[i]);
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            issues.Clear();
            try
            {
                if (scanLoadedScenes) ScanScenes();
                if (scanPrefabs) ScanProjectPrefabs();
                if (scanScriptableObjects) ScanProjectScriptableObjects();
                status = "扫描完成：发现 " + issues.Count + " 个问题，其中 Missing Script " +
                         issues.Count(issue => issue.Kind == IssueKind.MissingScript) + " 个。";
            }
            catch (OperationCanceledException)
            {
                status = "扫描已取消，保留当前结果。";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ScanScenes()
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    ScanGameObjectTree(roots[i], scene.path, false, roots[i].transform);
                }
            }
        }

        private void ScanProjectPrefabs()
        {
            string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "扫描 Prefab 丢失引用",
                        path,
                        paths.Length == 0 ? 1f : (float)i / paths.Length))
                {
                    throw new OperationCanceledException();
                }

                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    ScanGameObjectTree(root, path, true, root.transform);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("无法扫描 Prefab：" + path + "\n" + exception.Message);
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private void ScanProjectScriptableObjects()
        {
            string[] paths = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "扫描 ScriptableObject 丢失引用",
                        path,
                        paths.Length == 0 ? 1f : (float)i / paths.Length))
                {
                    throw new OperationCanceledException();
                }

                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    Object asset = assets[assetIndex];
                    if (!(asset is ScriptableObject)) continue;
                    ScanSerializedReferences(asset, path, asset.name, null, null);
                }
            }
        }

        private void ScanGameObjectTree(
            GameObject root,
            string assetPath,
            bool prefab,
            Transform prefabRoot)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject target = transforms[i].gameObject;
                string hierarchyPath = GetHierarchyPath(target.transform, root.transform);
                int[] siblingPath = prefab ? GetSiblingPath(target.transform, prefabRoot) : null;
                int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(target);
                for (int missingIndex = 0; missingIndex < missingScriptCount; missingIndex++)
                {
                    issues.Add(new ScanIssue
                    {
                        Kind = IssueKind.MissingScript,
                        AssetPath = assetPath,
                        HierarchyPath = hierarchyPath,
                        SceneObject = prefab ? null : target,
                        PrefabSiblingPath = siblingPath
                    });
                }

                Component[] components = target.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null) continue;
                    ScanSerializedReferences(
                        component,
                        assetPath,
                        hierarchyPath,
                        prefab ? null : target,
                        siblingPath);
                }
            }
        }

        private void ScanSerializedReferences(
            Object target,
            string assetPath,
            string hierarchyPath,
            GameObject sceneObject,
            int[] siblingPath)
        {
            try
            {
                SerializedObject serializedObject = new SerializedObject(target);
                SerializedProperty property = serializedObject.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        property.propertyPath == "m_Script" ||
                        property.objectReferenceValue != null ||
                        property.objectReferenceInstanceIDValue == 0)
                    {
                        continue;
                    }

                    issues.Add(new ScanIssue
                    {
                        Kind = IssueKind.MissingObjectReference,
                        AssetPath = assetPath,
                        HierarchyPath = hierarchyPath,
                        ComponentName = target.GetType().Name,
                        PropertyPath = property.propertyPath,
                        SceneObject = sceneObject,
                        PrefabSiblingPath = siblingPath
                    });
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("无法检查对象序列化引用：" + target.name + "\n" + exception.Message, target);
            }
        }

        private void DrawIssue(ScanIssue issue)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(issue.Kind == IssueKind.MissingScript ? "Missing Script" : "Missing 引用", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("位置", string.IsNullOrEmpty(issue.AssetPath) ? "未保存场景" : issue.AssetPath);
            EditorGUILayout.LabelField("层级", issue.HierarchyPath);
            if (issue.Kind == IssueKind.MissingObjectReference)
            {
                EditorGUILayout.LabelField("属性", issue.Description);
            }

            if (GUILayout.Button("定位", GUILayout.Width(70f)))
            {
                if (issue.SceneObject != null)
                {
                    Selection.activeObject = issue.SceneObject;
                    EditorGUIUtility.PingObject(issue.SceneObject);
                }
                else if (!string.IsNullOrEmpty(issue.AssetPath))
                {
                    Object asset = AssetDatabase.LoadMainAssetAtPath(issue.AssetPath);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void RemoveMissingScripts(int count)
        {
            if (!EditorUtility.DisplayDialog(
                    "移除 Missing Script",
                    "即将从场景和 Prefab 中移除 " + count + " 个 Missing Script。\n场景操作支持 Undo，Prefab 资源会直接保存。是否继续？",
                    "移除",
                    "取消"))
            {
                return;
            }

            RemoveSceneMissingScripts();
            RemovePrefabMissingScripts();
            AssetDatabase.SaveAssets();
            Scan();
        }

        private void RemoveSceneMissingScripts()
        {
            GameObject[] targets = issues
                .Where(issue => issue.Kind == IssueKind.MissingScript && issue.SceneObject != null)
                .Select(issue => issue.SceneObject)
                .Distinct()
                .ToArray();

            HashSet<Scene> scenes = new HashSet<Scene>();
            for (int i = 0; i < targets.Length; i++)
            {
                GameObject target = targets[i];
                Undo.RegisterCompleteObjectUndo(target, "移除 Missing Script");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target);
                EditorUtility.SetDirty(target);
                if (target.scene.IsValid()) scenes.Add(target.scene);
            }
            foreach (Scene scene in scenes) EditorSceneManager.MarkSceneDirty(scene);
        }

        private void RemovePrefabMissingScripts()
        {
            IGrouping<string, ScanIssue>[] groups = issues
                .Where(issue => issue.Kind == IssueKind.MissingScript && issue.SceneObject == null &&
                                !string.IsNullOrEmpty(issue.AssetPath) && issue.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .GroupBy(issue => issue.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int i = 0; i < groups.Length; i++)
            {
                string path = groups[i].Key;
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    foreach (int[] siblingPath in groups[i].Select(issue => issue.PrefabSiblingPath).Distinct(new IntArrayComparer()))
                    {
                        Transform target = ResolveBySiblingPath(root.transform, siblingPath);
                        if (target != null) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target.gameObject);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static string GetHierarchyPath(Transform target, Transform root)
        {
            string path = target.name;
            while (target != root && target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }

        private static int[] GetSiblingPath(Transform target, Transform root)
        {
            List<int> path = new List<int>();
            while (target != null && target != root)
            {
                path.Add(target.GetSiblingIndex());
                target = target.parent;
            }
            path.Reverse();
            return path.ToArray();
        }

        private static Transform ResolveBySiblingPath(Transform root, int[] path)
        {
            Transform current = root;
            if (path == null) return current;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] < 0 || path[i] >= current.childCount) return null;
                current = current.GetChild(path[i]);
            }
            return current;
        }

        private sealed class IntArrayComparer : IEqualityComparer<int[]>
        {
            public bool Equals(int[] left, int[] right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left == null || right == null || left.Length != right.Length) return false;
                for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
                return true;
            }

            public int GetHashCode(int[] value)
            {
                if (value == null) return 0;
                unchecked
                {
                    int hash = 17;
                    for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                    return hash;
                }
            }
        }
    }
}
