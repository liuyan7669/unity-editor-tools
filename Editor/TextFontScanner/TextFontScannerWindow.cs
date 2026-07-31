using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 列举已打开场景和项目 Prefab 中的 Text/TMP 组件，并支持修改字体。
/// </summary>
public sealed class TextFontScannerWindow : EditorWindow
{
    private const float RowHeight = 48f;

    private readonly List<TextInfo> hierarchyTexts = new List<TextInfo>();
    private readonly List<TextInfo> prefabTexts = new List<TextInfo>();
    private readonly string[] tabs = { "已打开场景", "项目 Prefab" };

    private Vector2 scrollPosition;
    private string searchText = string.Empty;
    private int selectedTab;
    [SerializeField] private Font findFont;
    [SerializeField] private Font batchFont;
    [SerializeField] private TMP_FontAsset findTmpFont;
    [SerializeField] private TMP_FontAsset batchTmpFont;
    [SerializeField] private List<string> selectedKeys = new List<string>();
    private bool sceneTextsExpanded = true;
    private bool sceneTmpTextsExpanded = true;
    private bool prefabTextsExpanded = true;
    private bool prefabTmpTextsExpanded = true;
    private bool prefabScanCanceled;
    private readonly List<string> scanErrors = new List<string>();
    [NonSerialized] private HashSet<string> selectedKeySet;

    private sealed class TextInfo
    {
        public Component Component;
        public GameObject PrefabAsset;
        public string SourcePath;
        public string ScenePath;
        public string ObjectPath;
        public UnityEngine.Object FontAsset;
        public UnityEngine.Object PendingFontAsset;
        public bool IsSceneText;
        public bool IsTmp;
        public GlobalObjectId GlobalObjectId;
        public string SelectionKey;
    }

    [MenuItem("Tools/Cowart/Text 字体检查")]
    private static void OpenWindow()
    {
        TextFontScannerWindow window = GetWindow<TextFontScannerWindow>();
        window.titleContent = new GUIContent("Text 字体检查");
        window.minSize = new Vector2(780f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureSelectionCache();
        RefreshAll();
    }

    private static class Styles
    {
        public static GUIStyle HeaderTitle;
        public static GUIStyle StatValue;
        public static GUIStyle StatLabel;
        public static GUIStyle BadgeScene;
        public static GUIStyle BadgePrefab;
        public static GUIStyle BadgePending;
        public static GUIStyle SectionHeader;
        private static bool isInitialized;

        public static void InitIfNeeded()
        {
            if (isInitialized && HeaderTitle != null) return;
            isInitialized = true;

            bool isDark = EditorGUIUtility.isProSkin;

            HeaderTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = isDark ? new Color(0.9f, 0.92f, 0.95f) : new Color(0.15f, 0.15f, 0.15f) }
            };

            StatValue = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isDark ? new Color(0.4f, 0.8f, 1f) : new Color(0.1f, 0.4f, 0.8f) }
            };

            StatLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isDark ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.4f, 0.4f, 0.4f) }
            };

            BadgeScene = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = isDark ? new Color(0.4f, 0.9f, 0.55f) : new Color(0.1f, 0.5f, 0.25f) }
            };

            BadgePrefab = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = isDark ? new Color(0.45f, 0.75f, 1f) : new Color(0.15f, 0.4f, 0.8f) }
            };

            BadgePending = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = isDark ? new Color(1f, 0.75f, 0.3f) : new Color(0.85f, 0.45f, 0f) }
            };

            SectionHeader = new GUIStyle(EditorStyles.foldoutHeader)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                fixedHeight = 24f
            };
        }
    }

    private void OnGUI()
    {
        Styles.InitIfNeeded();
        DrawToolbar();
        DrawSummary();
        DrawTextList();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24f));

        int newTab = GUILayout.Toolbar(selectedTab, tabs, EditorStyles.toolbarButton, GUILayout.Width(200f));
        if (newTab != selectedTab)
        {
            selectedTab = newTab;
            scrollPosition = Vector2.zero;
        }

        GUILayout.FlexibleSpace();

        GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField;
        searchText = EditorGUILayout.TextField(
            searchText,
            searchStyle,
            GUILayout.MinWidth(180f),
            GUILayout.MaxWidth(320f));

        if (GUILayout.Button(" 刷新 ", EditorStyles.toolbarButton, GUILayout.Width(58f)))
        {
            RefreshAll();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSummary()
    {
        List<TextInfo> currentTexts = GetCurrentTexts();
        List<TextInfo> visibleTexts = GetVisibleTexts(currentTexts);
        List<TextInfo> selectedTexts = GetSelectedTexts(currentTexts);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 统计面板 Badges
        EditorGUILayout.BeginHorizontal();
        DrawStatCard("已加载场景组件", hierarchyTexts.Count.ToString(), "📍");
        DrawStatCard("项目 Prefab 组件", prefabTexts.Count.ToString(), "📦");
        DrawStatCard("当前显示结果", visibleTexts.Count.ToString(), "👁️");
        DrawStatCard("当前已勾选", selectedTexts.Count.ToString(), "☑️");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);

        string scope = selectedTab == 0
            ? "💡 提示：当前正在检查 Hierarchy 中所有已打开场景的 Text 与 TMP 组件。"
            : "💡 提示：当前正在检查 Assets 目录下所有 Prefab 预制体中的 Text 与 TMP 组件。";
        EditorGUILayout.LabelField(scope, EditorStyles.miniLabel);

        DrawBatchTools(visibleTexts, selectedTexts);

        if (prefabScanCanceled)
        {
            EditorGUILayout.HelpBox("⚠️ 扫描已取消，当前结果可能不完整。点击“刷新”可重新扫描。", MessageType.Warning);
        }

        if (scanErrors.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"⚠️ 有 {scanErrors.Count} 个资源扫描失败，详细信息已输出到 Console。",
                MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawStatCard(string label, string value, string icon)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField($"{icon} {value}", Styles.StatValue);
        EditorGUILayout.LabelField(label, Styles.StatLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawBatchTools(List<TextInfo> visibleTexts, List<TextInfo> selectedTexts)
    {
        EditorGUILayout.Space(2f);

        // 控制栏：全选/清空
        EditorGUILayout.BeginHorizontal();
        bool allVisibleSelected = visibleTexts.Count > 0 && visibleTexts.TrueForAll(IsTextSelected);
        using (new EditorGUI.DisabledScope(visibleTexts.Count == 0))
        {
            string selectButtonText = allVisibleSelected ? "取消全选当前结果" : $"全选当前结果（{visibleTexts.Count}）";
            if (GUILayout.Button(selectButtonText, GUILayout.Height(22f), GUILayout.Width(160f)))
            {
                SetTextsSelected(visibleTexts, !allVisibleSelected);
            }
        }

        if (selectedKeySet.Count > 0)
        {
            if (GUILayout.Button($"清空已选（{selectedKeySet.Count}）", GUILayout.Height(22f), GUILayout.Width(110f)))
            {
                selectedKeySet.Clear();
                selectedKeys.Clear();
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);

        // 批量替换工具卡片
        EditorGUILayout.BeginHorizontal();

        // Legacy Text 批量卡片
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("🔤 Text 批量字体替换", EditorStyles.boldLabel);

        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 68f;

        findFont = EditorGUILayout.ObjectField("查找字体", findFont, typeof(Font), false) as Font;
        batchFont = EditorGUILayout.ObjectField("替换为", batchFont, typeof(Font), false) as Font;

        List<TextInfo> selectedLegacyTexts = selectedTexts.FindAll(info => !info.IsTmp);
        List<TextInfo> matchingLegacyTexts = selectedLegacyTexts.FindAll(info =>
            info.FontAsset == findFont && info.FontAsset != batchFont);

        bool canReplaceLegacy = findFont != null && batchFont != null && findFont != batchFont && matchingLegacyTexts.Count > 0;
        Color oldBg = GUI.backgroundColor;
        if (canReplaceLegacy)
        {
            GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.2f, 0.65f, 0.4f) : new Color(0.3f, 0.8f, 0.4f);
        }

        using (new EditorGUI.DisabledScope(!canReplaceLegacy))
        {
            if (GUILayout.Button($"替换匹配 Text（{matchingLegacyTexts.Count}）", GUILayout.Height(24f)))
            {
                ReplaceSelectedFonts(selectedLegacyTexts, findFont, batchFont, false);
            }
        }
        GUI.backgroundColor = oldBg;
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4f);

        // TMP 批量卡片
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("🔤 TMP 批量字体替换", EditorStyles.boldLabel);

        findTmpFont = EditorGUILayout.ObjectField("查找字体", findTmpFont, typeof(TMP_FontAsset), false) as TMP_FontAsset;
        batchTmpFont = EditorGUILayout.ObjectField("替换为", batchTmpFont, typeof(TMP_FontAsset), false) as TMP_FontAsset;

        EditorGUIUtility.labelWidth = oldLabelWidth;

        List<TextInfo> selectedTmpTexts = selectedTexts.FindAll(info => info.IsTmp);
        List<TextInfo> matchingTmpTexts = selectedTmpTexts.FindAll(info =>
            info.FontAsset == findTmpFont && info.FontAsset != batchTmpFont);

        bool canReplaceTmp = findTmpFont != null && batchTmpFont != null && findTmpFont != batchTmpFont && matchingTmpTexts.Count > 0;
        if (canReplaceTmp)
        {
            GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.2f, 0.65f, 0.4f) : new Color(0.3f, 0.8f, 0.4f);
        }

        using (new EditorGUI.DisabledScope(!canReplaceTmp))
        {
            if (GUILayout.Button($"替换匹配 TMP（{matchingTmpTexts.Count}）", GUILayout.Height(24f)))
            {
                ReplaceSelectedFonts(selectedTmpTexts, findTmpFont, batchTmpFont, true);
            }
        }
        GUI.backgroundColor = oldBg;
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawTextList()
    {
        List<TextInfo> currentTexts = GetCurrentTexts();
        List<TextInfo> legacyTexts = currentTexts.FindAll(info => !info.IsTmp);
        List<TextInfo> tmpTexts = currentTexts.FindAll(info => info.IsTmp);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

        if (selectedTab == 0)
        {
            DrawTextSection("Text 组件", legacyTexts, ref sceneTextsExpanded);
            EditorGUILayout.Space(8f);
            DrawTextSection("TMP 组件", tmpTexts, ref sceneTmpTextsExpanded);
        }
        else
        {
            DrawTextSection("Text 组件", legacyTexts, ref prefabTextsExpanded);
            EditorGUILayout.Space(8f);
            DrawTextSection("TMP 组件", tmpTexts, ref prefabTmpTextsExpanded);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawTextSection(string title, List<TextInfo> texts, ref bool expanded)
    {
        List<TextInfo> visibleTexts = GetVisibleTexts(texts);

        EditorGUILayout.BeginVertical();

        Rect headerRect = EditorGUILayout.GetControlRect(false, 26f);
        bool isDark = EditorGUIUtility.isProSkin;
        Color headerBg = isDark ? new Color(0.18f, 0.18f, 0.18f, 0.9f) : new Color(0.88f, 0.88f, 0.88f, 0.9f);
        EditorGUI.DrawRect(headerRect, headerBg);

        Color headerBorder = isDark ? new Color(0.1f, 0.1f, 0.1f, 0.5f) : new Color(0.75f, 0.75f, 0.75f, 0.5f);
        EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1f, headerRect.width, 1f), headerBorder);

        Rect foldoutRect = new Rect(headerRect.x + 6f, headerRect.y + 3f, headerRect.width - 12f, 20f);
        expanded = EditorGUI.Foldout(
            foldoutRect,
            expanded,
            $"{title}   ({visibleTexts.Count} / {texts.Count})",
            true,
            Styles.SectionHeader);

        if (expanded)
        {
            EditorGUILayout.Space(2f);
            DrawTextRows(visibleTexts, texts.Count, title);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawTextRows(List<TextInfo> visibleTexts, int totalCount, string componentName)
    {
        if (visibleTexts.Count == 0)
        {
            EditorGUILayout.HelpBox(
                totalCount == 0
                    ? $"当前区域没有找到 {componentName}。"
                    : $"当前区域没有符合搜索条件的 {componentName}。",
                MessageType.Info);
            return;
        }

        for (int i = 0; i < visibleTexts.Count; i++)
        {
            DrawTextRow(visibleTexts[i], i);
        }
    }

    private void DrawTextRow(TextInfo textInfo, int index)
    {
        float rowHeight = 52f;
        Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);

        rowRect.y += 2f;
        rowRect.height -= 4f;

        bool isDark = EditorGUIUtility.isProSkin;
        bool selected = IsTextSelected(textInfo);
        bool isPending = textInfo.PendingFontAsset != textInfo.FontAsset;
        bool isHover = rowRect.Contains(Event.current.mousePosition);

        Color bgColor;
        if (selected)
        {
            bgColor = isDark ? new Color(0.18f, 0.38f, 0.65f, 0.3f) : new Color(0.7f, 0.85f, 1f, 0.45f);
        }
        else if (isPending)
        {
            bgColor = isDark ? new Color(0.45f, 0.3f, 0.1f, 0.2f) : new Color(1f, 0.92f, 0.8f, 0.55f);
        }
        else if (isHover)
        {
            bgColor = isDark ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.05f);
        }
        else if ((index & 1) == 1)
        {
            bgColor = isDark ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.025f);
        }
        else
        {
            bgColor = isDark ? new Color(0f, 0f, 0f, 0.08f) : new Color(1f, 1f, 1f, 0.4f);
        }

        EditorGUI.DrawRect(rowRect, bgColor);

        if (selected)
        {
            Color borderColor = isDark ? new Color(0.3f, 0.6f, 0.95f, 0.6f) : new Color(0.2f, 0.5f, 0.9f, 0.7f);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), borderColor);
        }
        else if (isPending)
        {
            Color pendingBorder = isDark ? new Color(0.95f, 0.65f, 0.2f, 0.8f) : new Color(0.9f, 0.5f, 0.1f, 0.85f);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), pendingBorder);
        }

        const float buttonWidth = 52f;
        float locateButtonX = rowRect.xMax - buttonWidth - 6f;
        float applyButtonX = locateButtonX - buttonWidth - 6f;
        float actionSectionWidth = buttonWidth * 2f + 18f;

        float availableWidth = rowRect.width - 34f - actionSectionWidth;
        float fontWidth = Mathf.Clamp(availableWidth * 0.38f, 160f, 260f);
        float fontX = applyButtonX - fontWidth - 12f;
        float objectWidth = Mathf.Max(120f, fontX - (rowRect.x + 34f) - 10f);

        Rect toggleRect = new Rect(rowRect.x + 8f, rowRect.y + 15f, 18f, 18f);

        Rect fontFieldRect = new Rect(fontX, rowRect.y + 5f, fontWidth, 18f);
        Rect applyButtonRect = new Rect(applyButtonX, rowRect.y + 11f, buttonWidth, 24f);
        Rect locateButtonRect = new Rect(locateButtonX, rowRect.y + 11f, buttonWidth, 24f);

        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseDown
            && currentEvent.button == 0
            && rowRect.Contains(currentEvent.mousePosition)
            && !toggleRect.Contains(currentEvent.mousePosition)
            && !fontFieldRect.Contains(currentEvent.mousePosition)
            && !applyButtonRect.Contains(currentEvent.mousePosition)
            && !locateButtonRect.Contains(currentEvent.mousePosition))
        {
            selected = !selected;
            SetTextSelected(textInfo, selected);
            currentEvent.Use();
        }

        bool newSelected = EditorGUI.Toggle(toggleRect, selected);
        if (newSelected != selected)
        {
            SetTextSelected(textInfo, newSelected);
        }

        float leftX = rowRect.x + 32f;
        float badgeWidth = textInfo.IsSceneText ? 76f : 56f;
        Rect badgeRect = new Rect(leftX, rowRect.y + 5f, badgeWidth, 16f);
        Color badgeBg = textInfo.IsSceneText
            ? (isDark ? new Color(0.15f, 0.45f, 0.25f, 0.35f) : new Color(0.8f, 0.95f, 0.85f, 0.9f))
            : (isDark ? new Color(0.15f, 0.35f, 0.6f, 0.35f) : new Color(0.8f, 0.9f, 1f, 0.9f));
        EditorGUI.DrawRect(badgeRect, badgeBg);
        GUI.Label(badgeRect, textInfo.IsSceneText ? "已打开场景" : "Prefab", textInfo.IsSceneText ? Styles.BadgeScene : Styles.BadgePrefab);

        Rect sourceRect = new Rect(leftX + badgeWidth + 6f, rowRect.y + 4f, objectWidth - badgeWidth - 6f, 18f);
        Rect objectRect = new Rect(leftX, rowRect.y + 24f, objectWidth, 18f);

        EditorGUI.LabelField(
            sourceRect,
            new GUIContent(textInfo.SourcePath, textInfo.SourcePath),
            EditorStyles.boldLabel);
        EditorGUI.LabelField(
            objectRect,
            new GUIContent(textInfo.ObjectPath, textInfo.ObjectPath),
            EditorStyles.miniLabel);

        Type fontType = textInfo.IsTmp ? typeof(TMP_FontAsset) : typeof(Font);
        UnityEngine.Object newFont = EditorGUI.ObjectField(
            fontFieldRect,
            textInfo.PendingFontAsset,
            fontType,
            false);
        if (newFont != textInfo.PendingFontAsset)
        {
            textInfo.PendingFontAsset = newFont;
        }

        Rect fontPathRect = new Rect(fontX, rowRect.y + 24f, fontWidth, 18f);
        if (isPending)
        {
            string fontDiff = $"待应用: {GetFontName(textInfo.FontAsset)} → {GetFontName(textInfo.PendingFontAsset)}";
            EditorGUI.LabelField(fontPathRect, new GUIContent(fontDiff, fontDiff), Styles.BadgePending);
        }
        else
        {
            string fontPath = textInfo.PendingFontAsset != null
                ? AssetDatabase.GetAssetPath(textInfo.PendingFontAsset)
                : "未指定字体";
            EditorGUI.LabelField(fontPathRect, new GUIContent(fontPath, fontPath), EditorStyles.miniLabel);
        }

        Color oldBg = GUI.backgroundColor;
        if (isPending)
        {
            GUI.backgroundColor = isDark ? new Color(0.2f, 0.65f, 1f) : new Color(0.3f, 0.7f, 1f);
        }

        using (new EditorGUI.DisabledScope(!isPending))
        {
            if (GUI.Button(applyButtonRect, "应用"))
            {
                ApplyFont(textInfo);
            }
        }
        GUI.backgroundColor = oldBg;

        if (GUI.Button(locateButtonRect, textInfo.IsSceneText ? "定位" : "打开"))
        {
            LocateText(textInfo);
        }
    }

    private void RefreshAll()
    {
        scanErrors.Clear();
        ScanHierarchyTexts();
        ScanPrefabTexts();
        Repaint();
    }

    private void ScanHierarchyTexts()
    {
        hierarchyTexts.Clear();

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                continue;
            }

            try
            {
                AddSceneTexts(scene);
            }
            catch (Exception exception)
            {
                string scenePath = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
                RecordScanError(scenePath, exception);
            }
        }

        hierarchyTexts.Sort(CompareTextInfo);
    }

    private void AddSceneTexts(Scene scene)
    {
        string sourcePath = string.IsNullOrEmpty(scene.path) ? scene.name + "（未保存）" : scene.path;
        GameObject[] rootObjects = scene.GetRootGameObjects();

        for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
        {
            Text[] texts = rootObjects[rootIndex].GetComponentsInChildren<Text>(true);
            for (int textIndex = 0; textIndex < texts.Length; textIndex++)
            {
                hierarchyTexts.Add(CreateTextInfo(
                    texts[textIndex],
                    texts[textIndex].font,
                    null,
                    sourcePath,
                    scene.path,
                    true,
                    false));
            }

            TMP_Text[] tmpTexts = rootObjects[rootIndex].GetComponentsInChildren<TMP_Text>(true);
            for (int textIndex = 0; textIndex < tmpTexts.Length; textIndex++)
            {
                hierarchyTexts.Add(CreateTextInfo(
                    tmpTexts[textIndex],
                    tmpTexts[textIndex].font,
                    null,
                    sourcePath,
                    scene.path,
                    true,
                    true));
            }
        }
    }

    private void ScanPrefabTexts()
    {
        prefabTexts.Clear();
        prefabScanCanceled = false;

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        Array.Sort(prefabGuids, (left, right) => StringComparer.OrdinalIgnoreCase.Compare(
            AssetDatabase.GUIDToAssetPath(left),
            AssetDatabase.GUIDToAssetPath(right)));

        try
        {
            for (int prefabIndex = 0; prefabIndex < prefabGuids.Length; prefabIndex++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[prefabIndex]);
                if (EditorUtility.DisplayCancelableProgressBar(
                        "正在扫描 Prefab Text",
                        prefabPath,
                        prefabGuids.Length == 0 ? 1f : (float)prefabIndex / prefabGuids.Length))
                {
                    prefabScanCanceled = true;
                    break;
                }

                try
                {
                    GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefabAsset == null)
                    {
                        continue;
                    }

                    Text[] texts = prefabAsset.GetComponentsInChildren<Text>(true);
                    for (int textIndex = 0; textIndex < texts.Length; textIndex++)
                    {
                        prefabTexts.Add(CreateTextInfo(
                            texts[textIndex],
                            texts[textIndex].font,
                            prefabAsset,
                            prefabPath,
                            string.Empty,
                            false,
                            false));
                    }

                    TMP_Text[] tmpTexts = prefabAsset.GetComponentsInChildren<TMP_Text>(true);
                    for (int textIndex = 0; textIndex < tmpTexts.Length; textIndex++)
                    {
                        prefabTexts.Add(CreateTextInfo(
                            tmpTexts[textIndex],
                            tmpTexts[textIndex].font,
                            prefabAsset,
                            prefabPath,
                            string.Empty,
                            false,
                            true));
                    }
                }
                catch (Exception exception)
                {
                    RecordScanError(prefabPath, exception);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        prefabTexts.Sort(CompareTextInfo);
    }

    private static TextInfo CreateTextInfo(
        Component component,
        UnityEngine.Object fontAsset,
        GameObject prefabAsset,
        string sourcePath,
        string scenePath,
        bool isSceneText,
        bool isTmp)
    {
        GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(component);
        string ownerKey = sourcePath;
        if (isSceneText && string.IsNullOrEmpty(scenePath))
        {
            ownerKey += "#" + component.gameObject.scene.handle;
        }
        string componentType = isTmp ? "TMP:" : "Text:";
        string selectionKey = (isSceneText ? "Scene:" : "Prefab:")
            + componentType
            + ownerKey
            + "|"
            + globalObjectId;
        return new TextInfo
        {
            Component = component,
            PrefabAsset = prefabAsset,
            SourcePath = sourcePath,
            ScenePath = scenePath,
            ObjectPath = GetHierarchyPath(component.transform),
            FontAsset = fontAsset,
            PendingFontAsset = fontAsset,
            IsSceneText = isSceneText,
            IsTmp = isTmp,
            GlobalObjectId = globalObjectId,
            SelectionKey = selectionKey
        };
    }

    private void EnsureSelectionCache()
    {
        if (selectedKeys == null)
        {
            selectedKeys = new List<string>();
        }

        if (selectedKeySet == null)
        {
            selectedKeySet = new HashSet<string>(selectedKeys, StringComparer.Ordinal);
        }
    }

    private bool IsTextSelected(TextInfo textInfo)
    {
        EnsureSelectionCache();
        return selectedKeySet.Contains(textInfo.SelectionKey);
    }

    private void SetTextSelected(TextInfo textInfo, bool selected)
    {
        EnsureSelectionCache();
        if (selected)
        {
            if (selectedKeySet.Add(textInfo.SelectionKey))
            {
                selectedKeys.Add(textInfo.SelectionKey);
            }
        }
        else if (selectedKeySet.Remove(textInfo.SelectionKey))
        {
            selectedKeys.Remove(textInfo.SelectionKey);
        }
    }

    private void SetTextsSelected(List<TextInfo> texts, bool selected)
    {
        for (int i = 0; i < texts.Count; i++)
        {
            SetTextSelected(texts[i], selected);
        }
    }

    private List<TextInfo> GetSelectedTexts(List<TextInfo> source)
    {
        EnsureSelectionCache();
        var selectedTexts = new List<TextInfo>();
        AddSelectedTexts(source, selectedTexts);
        return selectedTexts;
    }

    private void AddSelectedTexts(List<TextInfo> source, List<TextInfo> destination)
    {
        for (int i = 0; i < source.Count; i++)
        {
            if (selectedKeySet.Contains(source[i].SelectionKey))
            {
                destination.Add(source[i]);
            }
        }
    }

    private void ReplaceSelectedFonts(
        List<TextInfo> selectedTexts,
        UnityEngine.Object sourceFont,
        UnityEngine.Object targetFont,
        bool isTmp)
    {
        List<TextInfo> textsToReplace = selectedTexts.FindAll(info =>
            info.IsTmp == isTmp
            && info.FontAsset == sourceFont
            && info.FontAsset != targetFont);
        string componentName = isTmp ? "TMP" : "Text";
        if (textsToReplace.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "一键替换字体",
                $"已勾选的 {componentName} 组件中没有使用查找字体的项目。",
                "确定");
            return;
        }

        bool isScenePage = selectedTab == 0;
        string scope = isScenePage
            ? "当前场景只标记为未保存并支持 Undo。"
            : "选中的 Prefab 会直接保存。";
        if (!EditorUtility.DisplayDialog(
                "一键替换字体",
                $"组件类型：{componentName}\n查找字体：{sourceFont.name}\n替换字体：{targetFont.name}\n替换数量：{textsToReplace.Count}\n\n{scope}",
                "开始替换",
                "取消"))
        {
            return;
        }

        Dictionary<string, List<TextInfo>> groups = GroupTextsBySource(textsToReplace, isScenePage);
        int processedGroups = 0;
        int replacedCount = 0;
        var errors = new List<string>();

        try
        {
            foreach (KeyValuePair<string, List<TextInfo>> group in groups)
            {
                EditorUtility.DisplayProgressBar(
                    "正在一键替换字体",
                    group.Key,
                    groups.Count == 0 ? 1f : (float)processedGroups / groups.Count);

                try
                {
                    replacedCount += isScenePage
                        ? ReplaceOpenedSceneFonts(group.Value, targetFont, isTmp)
                        : ReplacePrefabFonts(group.Key, group.Value, targetFont, isTmp);
                }
                catch (Exception exception)
                {
                    errors.Add(group.Key + ": " + exception.Message);
                }

                processedGroups++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (errors.Count > 0)
        {
            Debug.LogError("以下字体批量替换失败：\n" + string.Join("\n", errors));
        }

        EditorUtility.DisplayDialog(
            "一键替换完成",
            $"成功替换 {replacedCount} 个 {componentName} 组件，失败 {errors.Count} 组。"
            + (errors.Count > 0 ? "\n详细信息已输出到 Console。" : string.Empty),
            "确定");
        Repaint();
    }

    private static Dictionary<string, List<TextInfo>> GroupTextsBySource(
        List<TextInfo> source,
        bool sceneTexts)
    {
        var groups = new Dictionary<string, List<TextInfo>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < source.Count; i++)
        {
            TextInfo textInfo = source[i];
            if (textInfo.IsSceneText != sceneTexts)
            {
                continue;
            }

            string key = textInfo.SourcePath;
            if (sceneTexts
                && string.IsNullOrEmpty(textInfo.ScenePath)
                && textInfo.Component != null)
            {
                key += " [Scene " + textInfo.Component.gameObject.scene.handle + "]";
            }
            if (!groups.TryGetValue(key, out List<TextInfo> group))
            {
                group = new List<TextInfo>();
                groups.Add(key, group);
            }

            group.Add(textInfo);
        }

        return groups;
    }

    private static int ReplaceOpenedSceneFonts(
        List<TextInfo> textInfos,
        UnityEngine.Object targetFont,
        bool isTmp)
    {
        if (textInfos.Count == 0 || !IsSceneLoaded(textInfos[0]))
        {
            throw new InvalidOperationException("场景已经关闭，请刷新列表后重试。");
        }

        Scene scene = textInfos[0].Component != null
            ? textInfos[0].Component.gameObject.scene
            : SceneManager.GetSceneByPath(textInfos[0].ScenePath);
        var components = new List<Component>(textInfos.Count);

        for (int i = 0; i < textInfos.Count; i++)
        {
            Component component = ResolveSceneComponent(textInfos[i], scene);
            if (!IsExpectedComponent(component, isTmp))
            {
                throw new InvalidOperationException(
                    $"未能找到 {(isTmp ? "TMP" : "Text")}：{textInfos[i].ObjectPath}，请刷新列表后重试。");
            }

            components.Add(component);
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("批量修改 Text 字体");

        for (int i = 0; i < components.Count; i++)
        {
            Undo.RecordObject(components[i], "批量修改 Text 字体");
            SetComponentFont(components[i], targetFont, isTmp);
            EditorUtility.SetDirty(components[i]);
            UpdateTextInfo(textInfos[i], components[i], targetFont);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);
        return components.Count;
    }

    private static int ReplacePrefabFonts(
        string prefabPath,
        List<TextInfo> textInfos,
        UnityEngine.Object targetFont,
        bool isTmp)
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            throw new InvalidOperationException("未能加载 Prefab 资源。");
        }

        var components = new List<Component>(textInfos.Count);
        for (int i = 0; i < textInfos.Count; i++)
        {
            Component component = textInfos[i].Component;
            if (component == null)
            {
                component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(
                    textInfos[i].GlobalObjectId) as Component;
            }

            if (!IsExpectedComponent(component, isTmp))
            {
                throw new InvalidOperationException(
                    $"未能找到 {(isTmp ? "TMP" : "Text")}：{textInfos[i].ObjectPath}，请刷新列表后重试。");
            }

            components.Add(component);
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("批量修改 Prefab Text 字体");

        for (int i = 0; i < components.Count; i++)
        {
            Undo.RecordObject(components[i], "批量修改 Prefab Text 字体");
            SetComponentFont(components[i], targetFont, isTmp);
            EditorUtility.SetDirty(components[i]);
        }

        PrefabUtility.SavePrefabAsset(prefabAsset);

        for (int i = 0; i < components.Count; i++)
        {
            UpdateTextInfo(textInfos[i], components[i], targetFont);
        }

        Undo.CollapseUndoOperations(undoGroup);
        return components.Count;
    }

    private void ApplyFont(TextInfo textInfo)
    {
        if (textInfo.PendingFontAsset == textInfo.FontAsset)
        {
            return;
        }

        if (textInfo.IsSceneText)
        {
            if (!IsSceneLoaded(textInfo))
            {
                EditorUtility.DisplayDialog("字体修改失败", "该场景已经关闭，请刷新列表后重试。", "确定");
                return;
            }

            ApplySceneFont(textInfo);
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "修改 Prefab 字体",
                $"即将保存 Prefab：\n{textInfo.SourcePath}\n\n对象：{textInfo.ObjectPath}\n字体：{GetFontName(textInfo.FontAsset)} → {GetFontName(textInfo.PendingFontAsset)}",
                "修改并保存",
                "取消"))
        {
            return;
        }

        ApplyPrefabFont(textInfo);
    }

    private void ApplySceneFont(TextInfo textInfo)
    {
        try
        {
            Scene scene = textInfo.Component != null
                ? textInfo.Component.gameObject.scene
                : SceneManager.GetSceneByPath(textInfo.ScenePath);
            Component component = ResolveSceneComponent(textInfo, scene);
            if (!IsExpectedComponent(component, textInfo.IsTmp))
            {
                throw new InvalidOperationException("未能在场景中找到对应组件，请刷新列表后重试。");
            }

            Undo.RecordObject(component, "修改 Text 字体");
            SetComponentFont(component, textInfo.PendingFontAsset, textInfo.IsTmp);
            EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(scene);
            UpdateTextInfo(textInfo, component, textInfo.PendingFontAsset);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("字体修改失败", exception.Message, "确定");
        }
    }

    private void ApplyPrefabFont(TextInfo textInfo)
    {
        try
        {
            Component component = textInfo.Component;
            if (component == null)
            {
                component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(
                    textInfo.GlobalObjectId) as Component;
            }

            if (!IsExpectedComponent(component, textInfo.IsTmp) || textInfo.PrefabAsset == null)
            {
                throw new InvalidOperationException("未能在 Prefab 中找到对应组件，请刷新列表后重试。");
            }

            UnityEngine.Object targetFont = textInfo.PendingFontAsset;
            Undo.RecordObject(component, "修改 Prefab Text 字体");
            SetComponentFont(component, targetFont, textInfo.IsTmp);
            EditorUtility.SetDirty(component);
            PrefabUtility.SavePrefabAsset(textInfo.PrefabAsset);
            UpdateTextInfo(textInfo, component, targetFont);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("字体修改失败", exception.Message, "确定");
        }
    }

    private static Component ResolveSceneComponent(TextInfo textInfo, Scene scene)
    {
        Component component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(
            textInfo.GlobalObjectId) as Component;
        if (IsExpectedComponent(component, textInfo.IsTmp) && component.gameObject.scene == scene)
        {
            return component;
        }

        GameObject[] rootObjects = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
        {
            Component[] components = textInfo.IsTmp
                ? Array.ConvertAll(
                    rootObjects[rootIndex].GetComponentsInChildren<TMP_Text>(true),
                    value => (Component)value)
                : Array.ConvertAll(
                    rootObjects[rootIndex].GetComponentsInChildren<Text>(true),
                    value => (Component)value);
            for (int textIndex = 0; textIndex < components.Length; textIndex++)
            {
                if (GetHierarchyPath(components[textIndex].transform) == textInfo.ObjectPath)
                {
                    return components[textIndex];
                }
            }
        }

        return null;
    }

    private static bool IsExpectedComponent(Component component, bool isTmp)
    {
        return isTmp ? component is TMP_Text : component is Text;
    }

    private static void SetComponentFont(
        Component component,
        UnityEngine.Object fontAsset,
        bool isTmp)
    {
        if (isTmp)
        {
            ((TMP_Text)component).font = fontAsset as TMP_FontAsset;
        }
        else
        {
            ((Text)component).font = fontAsset as Font;
        }
    }

    private static void UpdateTextInfo(
        TextInfo textInfo,
        Component component,
        UnityEngine.Object fontAsset)
    {
        textInfo.Component = component;
        textInfo.FontAsset = fontAsset;
        textInfo.PendingFontAsset = fontAsset;
    }

    private static string GetFontName(UnityEngine.Object font)
    {
        return font != null ? font.name : "未指定字体";
    }

    private List<TextInfo> GetCurrentTexts()
    {
        return selectedTab == 0 ? hierarchyTexts : prefabTexts;
    }

    private List<TextInfo> GetVisibleTexts(List<TextInfo> source)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return source;
        }

        string keyword = searchText.Trim();
        return source.FindAll(info =>
            ContainsIgnoreCase(info.SourcePath, keyword)
            || ContainsIgnoreCase(info.ObjectPath, keyword)
            || (info.PendingFontAsset != null
                && ContainsIgnoreCase(info.PendingFontAsset.name, keyword))
            || ContainsIgnoreCase(
                info.PendingFontAsset != null
                    ? AssetDatabase.GetAssetPath(info.PendingFontAsset)
                    : string.Empty,
                keyword));
    }

    private static bool ContainsIgnoreCase(string value, string keyword)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void LocateText(TextInfo textInfo)
    {
        if (textInfo.IsSceneText)
        {
            if (IsSceneLoaded(textInfo))
            {
                Scene scene = string.IsNullOrEmpty(textInfo.ScenePath)
                    ? textInfo.Component.gameObject.scene
                    : SceneManager.GetSceneByPath(textInfo.ScenePath);
                Component component = ResolveSceneComponent(textInfo, scene);
                if (component != null)
                {
                    Selection.activeGameObject = component.gameObject;
                    EditorGUIUtility.PingObject(component.gameObject);
                }
                return;
            }
            return;
        }

        if (textInfo.PrefabAsset != null)
        {
            Selection.activeObject = textInfo.PrefabAsset;
            EditorGUIUtility.PingObject(textInfo.PrefabAsset);
            AssetDatabase.OpenAsset(textInfo.PrefabAsset);
        }
    }

    private static bool IsSceneLoaded(TextInfo textInfo)
    {
        if (!textInfo.IsSceneText)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(textInfo.ScenePath))
        {
            Scene scene = SceneManager.GetSceneByPath(textInfo.ScenePath);
            return scene.IsValid() && scene.isLoaded;
        }

        return textInfo.Component != null
            && textInfo.Component.gameObject.scene.IsValid()
            && textInfo.Component.gameObject.scene.isLoaded;
    }

    private void RecordScanError(string assetPath, Exception exception)
    {
        string message = $"{assetPath}: {exception.Message}";
        scanErrors.Add(message);
        Debug.LogError("Text 字体检查扫描失败：" + message);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        Transform parent = transform.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private static int CompareTextInfo(TextInfo left, TextInfo right)
    {
        int sourceCompare = StringComparer.OrdinalIgnoreCase.Compare(left.SourcePath, right.SourcePath);
        return sourceCompare != 0
            ? sourceCompare
            : StringComparer.OrdinalIgnoreCase.Compare(left.ObjectPath, right.ObjectPath);
    }
}
