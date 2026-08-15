using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cowart.EditorTools.Editor
{
    /// <summary>在选中层级或指定项目目录的 Prefab 中批量替换 Renderer 材质槽。</summary>
    public sealed class MaterialReplacerWindow : EditorWindow
    {
        private enum ReplaceScope
        {
            SelectedHierarchy,
            ProjectPrefabs
        }

        private ReplaceScope scope;
        private Material sourceMaterial;
        private Material replacementMaterial;
        private bool includeChildren = true;
        private bool includeInactive = true;
        private DefaultAsset projectFolder;
        private int previewRenderers;
        private int previewSlots;
        private string previewStatus = "尚未扫描。";

        [MenuItem("Cowart/项目开发工具/材质批量替换")]
        private static void Open()
        {
            GetWindow<MaterialReplacerWindow>("材质批量替换").minSize = new Vector2(520f, 360f);
        }

        private void OnGUI()
        {
            scope = (ReplaceScope)EditorGUILayout.EnumPopup("处理范围", scope);
            sourceMaterial = (Material)EditorGUILayout.ObjectField("原材质", sourceMaterial, typeof(Material), false);
            replacementMaterial = (Material)EditorGUILayout.ObjectField("替换材质", replacementMaterial, typeof(Material), false);
            includeChildren = EditorGUILayout.Toggle("包含子物体", includeChildren);
            includeInactive = EditorGUILayout.Toggle("包含未激活物体", includeInactive);

            if (scope == ReplaceScope.ProjectPrefabs)
            {
                projectFolder = (DefaultAsset)EditorGUILayout.ObjectField("Prefab 目录", projectFolder, typeof(DefaultAsset), false);
                string folderPath = projectFolder != null ? AssetDatabase.GetAssetPath(projectFolder) : string.Empty;
                if (projectFolder != null && !AssetDatabase.IsValidFolder(folderPath))
                {
                    EditorGUILayout.HelpBox("请选择 Assets 下的文件夹。", MessageType.Error);
                }
                if (projectFolder == null)
                {
                    EditorGUILayout.HelpBox("未指定目录时扫描整个 Assets。", MessageType.Info);
                }
                EditorGUILayout.HelpBox("Prefab 资源修改会直接保存，不进入场景 Undo。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("选中层级修改支持 Undo，并会记录 Prefab Instance Override。", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(sourceMaterial == null))
            {
                if (GUILayout.Button("扫描预览")) RefreshPreview();
            }
            EditorGUILayout.LabelField(previewStatus, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("命中 Renderer", previewRenderers.ToString());
            EditorGUILayout.LabelField("命中材质槽", previewSlots.ToString());

            bool canApply = sourceMaterial != null && replacementMaterial != null &&
                            sourceMaterial != replacementMaterial && previewSlots > 0;
            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button("执行材质替换", GUILayout.Height(32f))) Apply();
            }
        }

        private void RefreshPreview()
        {
            previewRenderers = 0;
            previewSlots = 0;
            if (scope == ReplaceScope.SelectedHierarchy)
            {
                Renderer[] renderers = GetSelectedRenderers();
                CountMatches(renderers);
            }
            else
            {
                string[] paths = GetPrefabPaths();
                try
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "扫描 Prefab 材质",
                                paths[i],
                                paths.Length == 0 ? 1f : (float)i / paths.Length))
                        {
                            previewStatus = "扫描已取消。";
                            return;
                        }

                        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                        if (prefab == null) continue;
                        Renderer[] renderers = includeChildren
                            ? prefab.GetComponentsInChildren<Renderer>(includeInactive)
                            : prefab.GetComponents<Renderer>();
                        CountMatches(renderers);
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            previewStatus = "扫描完成。";
        }

        private void CountMatches(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                int rendererMatches = 0;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] == sourceMaterial) rendererMatches++;
                }
                if (rendererMatches > 0)
                {
                    previewRenderers++;
                    previewSlots += rendererMatches;
                }
            }
        }

        private void Apply()
        {
            if (!EditorUtility.DisplayDialog(
                    "材质批量替换",
                    "确认把 " + previewSlots + " 个材质槽中的 “" + sourceMaterial.name + "” 替换为 “" + replacementMaterial.name + "”吗？",
                    "替换",
                    "取消"))
            {
                return;
            }

            if (scope == ReplaceScope.SelectedHierarchy) ApplyToSelection();
            else ApplyToPrefabs();
            RefreshPreview();
        }

        private void ApplyToSelection()
        {
            Renderer[] renderers = GetSelectedRenderers();
            List<Renderer> modified = new List<Renderer>();
            Undo.SetCurrentGroupName("批量替换材质");
            int undoGroup = Undo.GetCurrentGroup();
            HashSet<Scene> dirtyScenes = new HashSet<Scene>();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] != sourceMaterial) continue;
                    materials[materialIndex] = replacementMaterial;
                    changed = true;
                }
                if (!changed) continue;

                Undo.RecordObject(renderer, "批量替换材质");
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
                modified.Add(renderer);
                if (renderer.gameObject.scene.IsValid()) dirtyScenes.Add(renderer.gameObject.scene);
            }

            foreach (Scene scene in dirtyScenes) EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("材质替换完成：修改 " + modified.Count + " 个 Renderer。 ");
        }

        private void ApplyToPrefabs()
        {
            string[] paths = GetPrefabPaths();
            int modifiedPrefabs = 0;
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "替换 Prefab 材质",
                            path,
                            paths.Length == 0 ? 1f : (float)i / paths.Length))
                    {
                        break;
                    }

                    GameObject root = null;
                    try
                    {
                        root = PrefabUtility.LoadPrefabContents(path);
                        Renderer[] renderers = includeChildren
                            ? root.GetComponentsInChildren<Renderer>(includeInactive)
                            : root.GetComponents<Renderer>();
                        bool prefabChanged = false;
                        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                        {
                            Renderer renderer = renderers[rendererIndex];
                            Material[] materials = renderer.sharedMaterials;
                            bool changed = false;
                            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                            {
                                if (materials[materialIndex] != sourceMaterial) continue;
                                materials[materialIndex] = replacementMaterial;
                                changed = true;
                            }
                            if (!changed) continue;
                            renderer.sharedMaterials = materials;
                            prefabChanged = true;
                        }

                        if (prefabChanged)
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, path);
                            modifiedPrefabs++;
                        }
                    }
                    finally
                    {
                        if (root != null) PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            Debug.Log("材质替换完成：修改 " + modifiedPrefabs + " 个 Prefab。 ");
        }

        private Renderer[] GetSelectedRenderers()
        {
            HashSet<int> ids = new HashSet<int>();
            List<Renderer> results = new List<Renderer>();
            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                Renderer[] renderers = includeChildren
                    ? selected[i].GetComponentsInChildren<Renderer>(includeInactive)
                    : selected[i].GetComponents<Renderer>();
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (ids.Add(renderer.GetInstanceID())) results.Add(renderer);
                }
            }
            return results.ToArray();
        }

        private string[] GetPrefabPaths()
        {
            string folderPath = projectFolder != null ? AssetDatabase.GetAssetPath(projectFolder) : "Assets";
            if (!AssetDatabase.IsValidFolder(folderPath) || !folderPath.StartsWith("Assets", StringComparison.Ordinal))
            {
                return Array.Empty<string>();
            }

            return AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
