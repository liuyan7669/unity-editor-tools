using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Cowart.EditorUtilities.Editor
{
    /// <summary>批量查看、应用和还原当前选择中的 Prefab Instance Override。</summary>
    public sealed class PrefabOverrideManagerWindow : EditorWindow
    {
        private sealed class PrefabEntry
        {
            public GameObject Root;
            public string AssetPath;
            public int ObjectOverrides;
            public int AddedComponents;
            public int AddedObjects;
            public int RemovedComponents;

            public int Total => ObjectOverrides + AddedComponents + AddedObjects + RemovedComponents;
        }

        private readonly List<PrefabEntry> entries = new List<PrefabEntry>();
        private Vector2 scrollPosition;

        [MenuItem("Cowart/项目开发工具/Prefab Override 管理")]
        private static void Open()
        {
            GetWindow<PrefabOverrideManagerWindow>("Prefab Override").minSize = new Vector2(700f, 420f);
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Refresh;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Apply 会直接修改 Prefab 资源；Revert 修改场景实例。嵌套 Prefab 按最外层 Instance Root 处理。", MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新")) Refresh();
            int overrideCount = entries.Sum(entry => entry.Total);
            using (new EditorGUI.DisabledScope(overrideCount == 0))
            {
                if (GUILayout.Button("应用全部 Override")) ApplyAll();
                if (GUILayout.Button("还原全部 Override")) RevertAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Prefab 实例 " + entries.Count + "，Override 合计 " + overrideCount, EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < entries.Count; i++) DrawEntry(entries[i]);
            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            entries.Clear();
            HashSet<int> rootIds = new HashSet<int>();
            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(selected[i]);
                if (root == null || !rootIds.Add(root.GetInstanceID())) continue;

                entries.Add(new PrefabEntry
                {
                    Root = root,
                    AssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root),
                    ObjectOverrides = PrefabUtility.GetObjectOverrides(root, false).Count,
                    AddedComponents = PrefabUtility.GetAddedComponents(root).Count,
                    AddedObjects = PrefabUtility.GetAddedGameObjects(root).Count,
                    RemovedComponents = PrefabUtility.GetRemovedComponents(root).Count
                });
            }
            Repaint();
        }

        private void DrawEntry(PrefabEntry entry)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(entry.Root, typeof(GameObject), true);
            EditorGUILayout.LabelField("合计 " + entry.Total, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.SelectableLabel(entry.AssetPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField(
                "属性/对象 " + entry.ObjectOverrides +
                "    添加组件 " + entry.AddedComponents +
                "    添加物体 " + entry.AddedObjects +
                "    移除组件 " + entry.RemovedComponents);

            using (new EditorGUI.DisabledScope(entry.Root == null || entry.Total == 0))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply All")) Apply(entry);
                if (GUILayout.Button("Revert All")) Revert(entry);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void ApplyAll()
        {
            PrefabEntry[] targets = entries.Where(entry => entry.Root != null && entry.Total > 0).ToArray();
            if (targets.Length == 0 || !EditorUtility.DisplayDialog(
                    "应用全部 Prefab Override",
                    "将把 " + targets.Length + " 个 Prefab 实例的全部 Override 写入 Prefab 资源。此操作不能依赖场景 Undo 恢复，是否继续？",
                    "应用",
                    "取消"))
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++) ApplyInternal(targets[i]);
            AssetDatabase.SaveAssets();
            Refresh();
        }

        private void RevertAll()
        {
            PrefabEntry[] targets = entries.Where(entry => entry.Root != null && entry.Total > 0).ToArray();
            if (targets.Length == 0 || !EditorUtility.DisplayDialog(
                    "还原全部 Prefab Override",
                    "将还原 " + targets.Length + " 个 Prefab 实例的全部 Override，是否继续？",
                    "还原",
                    "取消"))
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++) RevertInternal(targets[i]);
            Refresh();
        }

        private void Apply(PrefabEntry entry)
        {
            if (!EditorUtility.DisplayDialog(
                    "应用 Prefab Override",
                    "确认把 “" + entry.Root.name + "” 的全部 Override 写入：\n" + entry.AssetPath,
                    "应用",
                    "取消"))
            {
                return;
            }

            ApplyInternal(entry);
            AssetDatabase.SaveAssets();
            Refresh();
        }

        private void Revert(PrefabEntry entry)
        {
            if (!EditorUtility.DisplayDialog(
                    "还原 Prefab Override",
                    "确认还原 “" + entry.Root.name + "” 的全部 Override？",
                    "还原",
                    "取消"))
            {
                return;
            }

            RevertInternal(entry);
            Refresh();
        }

        private static void ApplyInternal(PrefabEntry entry)
        {
            try
            {
                PrefabUtility.ApplyPrefabInstance(entry.Root, InteractionMode.UserAction);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, entry.Root);
                EditorUtility.DisplayDialog("Apply 失败", entry.Root.name + "：" + exception.Message, "确定");
            }
        }

        private static void RevertInternal(PrefabEntry entry)
        {
            try
            {
                PrefabUtility.RevertPrefabInstance(entry.Root, InteractionMode.UserAction);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, entry.Root);
                EditorUtility.DisplayDialog("Revert 失败", entry.Root.name + "：" + exception.Message, "确定");
            }
        }
    }
}
