using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cowart.EditorUtilities.Editor
{
    /// <summary>查找资源的直接/递归依赖，以及项目中反向引用目标资源的资源。</summary>
    public sealed class ReferenceFinderWindow : EditorWindow
    {
        private enum SearchMode
        {
            ReverseReferences,
            Dependencies
        }

        private Object target;
        private SearchMode searchMode;
        private bool recursiveDependencies = true;
        private bool includePackages;
        private Vector2 scrollPosition;
        private readonly List<string> results = new List<string>();
        private string status = "请选择 Project 中的资源。";

        [MenuItem("Cowart/项目开发工具/引用查找")]
        private static void Open()
        {
            GetWindow<ReferenceFinderWindow>("引用查找").minSize = new Vector2(620f, 420f);
        }

        private void OnEnable()
        {
            if (target == null && Selection.activeObject != null &&
                !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Selection.activeObject)))
            {
                target = Selection.activeObject;
            }
        }

        private void OnGUI()
        {
            target = EditorGUILayout.ObjectField("目标资源", target, typeof(Object), false);
            searchMode = (SearchMode)EditorGUILayout.EnumPopup("查找方式", searchMode);
            if (searchMode == SearchMode.Dependencies)
            {
                recursiveDependencies = EditorGUILayout.Toggle("递归依赖", recursiveDependencies);
            }
            else
            {
                includePackages = EditorGUILayout.Toggle("同时扫描 Packages", includePackages);
            }

            string targetPath = target != null ? AssetDatabase.GetAssetPath(target) : string.Empty;
            if (target != null && string.IsNullOrEmpty(targetPath))
            {
                EditorGUILayout.HelpBox("目标必须是 Project 中有资源路径的对象，不能是场景实例。", MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(targetPath)))
            {
                if (GUILayout.Button("开始查找", GUILayout.Height(30f)))
                {
                    Search(targetPath);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(status, EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < results.Count; i++)
            {
                DrawResult(results[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        private void Search(string targetPath)
        {
            results.Clear();
            try
            {
                if (searchMode == SearchMode.Dependencies)
                {
                    string[] dependencies = AssetDatabase.GetDependencies(targetPath, recursiveDependencies);
                    results.AddRange(dependencies
                        .Where(path => !string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                }
                else
                {
                    FindReverseReferences(targetPath);
                }

                status = "找到 " + results.Count + " 个结果。";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void FindReverseReferences(string targetPath)
        {
            string[] allPaths = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) ||
                               (includePackages && path.StartsWith("Packages/", StringComparison.Ordinal)))
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .Where(path => !string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            for (int i = 0; i < allPaths.Length; i++)
            {
                string candidatePath = allPaths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "查找资源引用",
                        candidatePath,
                        allPaths.Length == 0 ? 1f : (float)i / allPaths.Length))
                {
                    status = "查找已取消，保留当前结果。";
                    break;
                }

                string[] dependencies = AssetDatabase.GetDependencies(candidatePath, false);
                if (dependencies.Any(path =>
                        string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(candidatePath);
                }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static void DrawResult(string path)
        {
            EditorGUILayout.BeginHorizontal();
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            EditorGUILayout.ObjectField(asset, typeof(Object), false, GUILayout.Width(180f));
            EditorGUILayout.SelectableLabel(path, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("定位", GUILayout.Width(52f)))
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
