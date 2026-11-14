using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cowart.EditorUtilities.Editor
{
    [InitializeOnLoad]
    internal static class SelectionHistoryService
    {
        internal sealed class Entry
        {
            public string Key;
            public string Label;
            public bool Current;
        }

        private const int Capacity = 30;
        private static readonly List<string> Keys = new List<string>();
        private static readonly List<string> Labels = new List<string>();
        private static int currentIndex = -1;
        private static bool suppressSelectionChanged;

        static SelectionHistoryService()
        {
            Selection.selectionChanged += RecordSelection;
            EditorApplication.delayCall += RecordSelection;
        }

        public static bool CanMoveBack => currentIndex > 0;
        public static bool CanMoveForward => currentIndex >= 0 && currentIndex < Keys.Count - 1;

        public static Entry[] GetEntries()
        {
            Entry[] entries = new Entry[Keys.Count];
            for (int i = 0; i < Keys.Count; i++)
            {
                entries[i] = new Entry { Key = Keys[i], Label = Labels[i], Current = i == currentIndex };
            }
            return entries;
        }

        public static void MoveBack()
        {
            if (CanMoveBack) SelectIndex(currentIndex - 1);
        }

        public static void MoveForward()
        {
            if (CanMoveForward) SelectIndex(currentIndex + 1);
        }

        public static void Select(string key)
        {
            int index = Keys.IndexOf(key);
            if (index >= 0) SelectIndex(index);
        }

        private static void RecordSelection()
        {
            if (suppressSelectionChanged) return;
            Object[] selected = Selection.objects.Where(item => item != null).ToArray();
            if (selected.Length == 0) return;

            string key = SelectionObjectUtility.CreateSelectionKey(selected);
            if (string.IsNullOrEmpty(key) || (currentIndex >= 0 && Keys[currentIndex] == key)) return;

            if (currentIndex < Keys.Count - 1)
            {
                Keys.RemoveRange(currentIndex + 1, Keys.Count - currentIndex - 1);
                Labels.RemoveRange(currentIndex + 1, Labels.Count - currentIndex - 1);
            }

            Keys.Add(key);
            Labels.Add(SelectionObjectUtility.CreateSelectionLabel(selected));
            if (Keys.Count > Capacity)
            {
                Keys.RemoveAt(0);
                Labels.RemoveAt(0);
            }
            currentIndex = Keys.Count - 1;
        }

        private static void SelectIndex(int index)
        {
            Object[] objects = SelectionObjectUtility.ResolveSelectionKey(Keys[index]);
            if (objects.Length == 0) return;
            suppressSelectionChanged = true;
            Selection.objects = objects;
            currentIndex = index;
            suppressSelectionChanged = false;
        }
    }

    [FilePath("ProjectSettings/CowartSelectionBookmarks.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class SelectionBookmarkState : ScriptableSingleton<SelectionBookmarkState>
    {
        [Serializable]
        internal sealed class Bookmark
        {
            public string Label;
            public string SelectionKey;
        }

        [SerializeField] private List<Bookmark> bookmarks = new List<Bookmark>();
        public IReadOnlyList<Bookmark> Bookmarks => bookmarks;

        public void Add(string label, string selectionKey)
        {
            bookmarks.Add(new Bookmark { Label = label, SelectionKey = selectionKey });
            Save(true);
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= bookmarks.Count) return;
            bookmarks.RemoveAt(index);
            Save(true);
        }
    }

    /// <summary>提供会话选择历史，以及保存在 ProjectSettings 中的选择收藏夹。</summary>
    public sealed class SelectionHistoryWindow : EditorWindow
    {
        private string bookmarkName = string.Empty;
        private Vector2 historyScroll;
        private Vector2 bookmarkScroll;

        [MenuItem("Cowart/项目/选择历史与收藏")]
        private static void Open()
        {
            GetWindow<SelectionHistoryWindow>("选择历史与收藏").minSize = new Vector2(560f, 430f);
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("选择历史（当前 Editor 会话）", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!SelectionHistoryService.CanMoveBack))
            {
                if (GUILayout.Button("← 后退")) SelectionHistoryService.MoveBack();
            }
            using (new EditorGUI.DisabledScope(!SelectionHistoryService.CanMoveForward))
            {
                if (GUILayout.Button("前进 →")) SelectionHistoryService.MoveForward();
            }
            EditorGUILayout.EndHorizontal();

            historyScroll = EditorGUILayout.BeginScrollView(historyScroll, GUILayout.Height(150f));
            SelectionHistoryService.Entry[] entries = SelectionHistoryService.GetEntries();
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                SelectionHistoryService.Entry entry = entries[i];
                GUIStyle style = entry.Current ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button((entry.Current ? "● " : "  ") + entry.Label, style))
                {
                    SelectionHistoryService.Select(entry.Key);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("收藏夹（项目持久化）", EditorStyles.boldLabel);
            bookmarkName = EditorGUILayout.TextField("收藏名称", bookmarkName);
            using (new EditorGUI.DisabledScope(Selection.objects.Length == 0))
            {
                if (GUILayout.Button("收藏当前选择")) AddBookmark();
            }

            bookmarkScroll = EditorGUILayout.BeginScrollView(bookmarkScroll);
            IReadOnlyList<SelectionBookmarkState.Bookmark> bookmarks = SelectionBookmarkState.instance.Bookmarks;
            for (int i = 0; i < bookmarks.Count; i++)
            {
                int bookmarkIndex = i;
                SelectionBookmarkState.Bookmark bookmark = bookmarks[i];
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(bookmark.Label))
                {
                    Selection.objects = SelectionObjectUtility.ResolveSelectionKey(bookmark.SelectionKey);
                }
                if (GUILayout.Button("删除", GUILayout.Width(52f)))
                {
                    SelectionBookmarkState.instance.RemoveAt(bookmarkIndex);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void AddBookmark()
        {
            Object[] selected = Selection.objects.Where(item => item != null).ToArray();
            string key = SelectionObjectUtility.CreateSelectionKey(selected);
            if (string.IsNullOrEmpty(key)) return;
            string label = string.IsNullOrWhiteSpace(bookmarkName)
                ? SelectionObjectUtility.CreateSelectionLabel(selected)
                : bookmarkName.Trim();
            SelectionBookmarkState.instance.Add(label, key);
            bookmarkName = string.Empty;
        }
    }

    /// <summary>显示并复制当前对象的资源、层级、类型与程序集信息。</summary>
    public sealed class AssetInfoWindow : EditorWindow
    {
        private Object target;

        [MenuItem("Cowart/项目/查看与复制对象信息")]
        [MenuItem("Assets/Cowart/项目/查看与复制对象信息", false, 20)]
        [MenuItem("GameObject/Cowart/项目/查看与复制对象信息", false, -10000)]
        private static void Open()
        {
            GetWindow<AssetInfoWindow>("资源信息").minSize = new Vector2(620f, 330f);
        }

        private void OnEnable()
        {
            target = Selection.activeObject;
            Selection.selectionChanged += FollowSelection;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= FollowSelection;
        }

        private void FollowSelection()
        {
            target = Selection.activeObject;
            Repaint();
        }

        private void OnGUI()
        {
            target = EditorGUILayout.ObjectField("目标", target, typeof(Object), true);
            if (target == null)
            {
                EditorGUILayout.HelpBox("请选择场景物体、组件或 Project 资源。", MessageType.Info);
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(target);
            string guid = string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
            string hierarchyPath = GetHierarchyPath(target);
            string typeName = target.GetType().FullName;
            string assemblyName = target.GetType().Assembly.GetName().Name;

            DrawCopyRow("资源路径", assetPath);
            DrawCopyRow("资源 GUID", guid);
            DrawCopyRow("Hierarchy 路径", hierarchyPath);
            DrawCopyRow("类型", typeName);
            DrawCopyRow("程序集", assemblyName);

            if (GUILayout.Button("复制全部信息", GUILayout.Height(30f)))
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("Name: " + target.name);
                if (!string.IsNullOrEmpty(assetPath)) builder.AppendLine("AssetPath: " + assetPath);
                if (!string.IsNullOrEmpty(guid)) builder.AppendLine("GUID: " + guid);
                if (!string.IsNullOrEmpty(hierarchyPath)) builder.AppendLine("HierarchyPath: " + hierarchyPath);
                builder.AppendLine("Type: " + typeName);
                builder.Append("Assembly: " + assemblyName);
                EditorGUIUtility.systemCopyBuffer = builder.ToString();
                ShowNotification(new GUIContent("已复制全部信息"));
            }
        }

        private void DrawCopyRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(value ?? string.Empty, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(value)))
            {
                if (GUILayout.Button("复制", GUILayout.Width(52f)))
                {
                    EditorGUIUtility.systemCopyBuffer = value;
                    ShowNotification(new GUIContent("已复制" + label));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string GetHierarchyPath(Object value)
        {
            Transform transform = value is GameObject gameObject
                ? gameObject.transform
                : value is Component component ? component.transform : null;
            if (transform == null) return string.Empty;

            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }

    internal static class SelectionObjectUtility
    {
        private const char Separator = '|';

        public static string CreateSelectionKey(Object[] objects)
        {
            List<string> ids = new List<string>(objects.Length);
            for (int i = 0; i < objects.Length; i++)
            {
                GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(objects[i]);
                string value = id.ToString();
                if (!string.IsNullOrEmpty(value)) ids.Add(value);
            }
            return string.Join(Separator.ToString(), ids);
        }

        public static Object[] ResolveSelectionKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return Array.Empty<Object>();
            string[] values = key.Split(Separator);
            List<Object> results = new List<Object>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                if (!GlobalObjectId.TryParse(values[i], out GlobalObjectId id)) continue;
                Object target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                if (target != null) results.Add(target);
            }
            return results.ToArray();
        }

        public static string CreateSelectionLabel(Object[] objects)
        {
            if (objects == null || objects.Length == 0) return "空选择";
            if (objects.Length == 1) return objects[0].name;
            return objects[0].name + " 等 " + objects.Length + " 个对象";
        }
    }
}
