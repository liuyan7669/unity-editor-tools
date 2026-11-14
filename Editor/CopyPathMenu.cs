using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Cowart.EditorUtilities.Editor
{
    /// <summary>复制完整场景层级路径或资源的磁盘绝对路径。</summary>
    public static class CopyPathMenu
    {
        [MenuItem("GameObject/Cowart/复制路径", false, -10000)]
        private static void CopyHierarchyPath(MenuCommand command)
        {
            GameObject target = command.context as GameObject ?? Selection.activeGameObject;
            if (target != null && target.scene.IsValid())
                EditorGUIUtility.systemCopyBuffer = GetHierarchyPath(target.transform);
        }

        [MenuItem("GameObject/Cowart/复制路径", true)]
        private static bool CanCopyHierarchyPath() => Selection.activeGameObject != null &&
            Selection.activeGameObject.scene.IsValid();

        [MenuItem("Assets/Cowart/复制路径", false, 20)]
        private static void CopyAssetPath()
        {
            var paths = new List<string>();
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) paths.Add(GetAbsoluteAssetPath(path));
            }
            if (paths.Count == 0) paths.Add(GetAbsoluteAssetPath(GetActiveFolderPath()));
            EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, paths);
        }

        internal static string GetHierarchyPath(Transform target)
        {
            var names = new Stack<string>();
            for (; target != null; target = target.parent) names.Push(target.name);
            return "/" + string.Join("/", names);
        }

        internal static string GetAbsoluteAssetPath(string assetPath)
        {
            if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (package != null)
                    return Path.GetFullPath(Path.Combine(package.resolvedPath,
                        assetPath.Substring(("Packages/" + package.name).Length).TrimStart('/')));
            }
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static string GetActiveFolderPath()
        {
            // Unity 2021/2022 的 Project 浏览目录入口为内部 API；不读取其他包。
            var method = typeof(ProjectWindowUtil).GetMethod("GetActiveFolderPath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            string path = method?.Invoke(null, null) as string;
            return string.IsNullOrEmpty(path) ? "Assets" : path;
        }
    }
}
