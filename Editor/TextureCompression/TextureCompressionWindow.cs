using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 扫描并展示项目中的贴图资源。
/// </summary>
public sealed class TextureCompressionWindow : EditorWindow
{
    private const float PreviewSize = 78f;
    private const float RowHeight = 92f;
    private const float GroupHeaderHeight = 26f;

    private static readonly string[] MaxTextureSizeNames =
    {
        "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192"
    };

    private static readonly int[] MaxTextureSizeValues =
    {
        32, 64, 128, 256, 512, 1024, 2048, 4096, 8192
    };

    private static readonly string[] SortModeNames =
    {
        "内存从大到小",
        "文件从大到小",
        "尺寸从大到小",
        "资源路径"
    };

    private static readonly TextureImporterType[] TextureTypes =
    {
        TextureImporterType.Default,
        TextureImporterType.NormalMap,
        TextureImporterType.GUI,
        TextureImporterType.Sprite,
        TextureImporterType.Cursor,
        TextureImporterType.Cookie,
        TextureImporterType.Lightmap,
        TextureImporterType.DirectionalLightmap,
        TextureImporterType.Shadowmask,
        TextureImporterType.SingleChannel
    };

    private static readonly string[] TextureTypeNames =
    {
        "Default",
        "Normal map",
        "Editor GUI and Legacy GUI",
        "Sprite (2D and UI)",
        "Cursor",
        "Cookie",
        "Lightmap",
        "Directional Lightmap",
        "Shadowmask",
        "Single Channel"
    };

    private readonly List<TextureAssetInfo> textureAssets = new List<TextureAssetInfo>();
    private readonly List<TextureAssetInfo> visibleTextureAssets = new List<TextureAssetInfo>();
    private readonly List<TextureListItem> visibleListItems = new List<TextureListItem>();
    private readonly HashSet<string> selectedPaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture> previewCache =
        new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TextureImporterType, bool> groupFoldoutStates =
        new Dictionary<TextureImporterType, bool>();

    private Vector2 scrollPosition;
    private string searchText = string.Empty;
    private TextureSortMode sortMode;
    private int targetMaxTextureSize = 2048;
    private bool showReadableOnly;
    private bool showUncompressedOnly;
    private int selectedTextureTypeMask = (1 << 10) - 1;
    private GUIStyle fileSizeStyle;
    private GUIStyle memorySizeStyle;
    private GUIStyle metricLabelStyle;
    private bool isVisibleListDirty = true;
    private string visibleFileSizeText = "0 B";
    private string visibleRuntimeMemoryText = "0 B";
    private int visibleReadableCount;
    private int visibleUncompressedCount;
    private string dataStatusText = "等待更新";
    private bool isApplyingMaxTextureSize;
    private bool isAutomaticRefreshQueued;
    private float visibleListContentHeight;

    private enum TextureSortMode
    {
        RuntimeMemoryDescending,
        FileSizeDescending,
        PixelCountDescending,
        AssetPath
    }

    private sealed class TextureAssetInfo
    {
        public string AssetPath;
        public Texture2D Texture;
        public int Width;
        public int Height;
        public long FileSize;
        public long RuntimeMemory;
        public TextureImporterType ImporterType;
        public string TextureType;
        public string RuntimeFormat;
        public TextureImporterCompression Compression;
        public int CompressionQuality;
        public int MaxTextureSize;
        public bool MipmapEnabled;
        public bool IsReadable;
        public string FileName;
        public string FileSizeText;
        public string RuntimeMemoryText;
        public string BasicInfoText;
        public string ImportInfoText;
        public string ImportTooltip;
    }

    private sealed class TextureListItem
    {
        public TextureAssetInfo TextureAsset;
        public TextureImporterType TextureType;
        public int GroupCount;
        public int RowIndex;
        public float Y;
        public float Height;

        public bool IsGroupHeader => TextureAsset == null;
    }

    [MenuItem("Cowart/项目开发工具/纹理压缩")]
    private static void OpenWindow()
    {
        TextureCompressionWindow window = GetWindow<TextureCompressionWindow>();
        window.titleContent = new GUIContent("贴图压缩");
        window.minSize = new Vector2(900f, 500f);
        window.Show();
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        TextureCompressionAssetPostprocessor.TextureAssetsChanged -= OnTextureAssetsChanged;
        TextureCompressionAssetPostprocessor.TextureAssetsChanged += OnTextureAssetsChanged;
        RefreshTextureAssets();
    }

    private void OnGUI()
    {
        InitializeStyles();
        DrawToolbar();
        DrawFilters();
        List<TextureAssetInfo> visibleAssets = GetVisibleTextureAssets();
        DrawSummary(visibleAssets);
        DrawTextureList(visibleAssets);
        DrawBatchCompression(visibleAssets);
    }

    private void OnDisable()
    {
        TextureCompressionAssetPostprocessor.TextureAssetsChanged -= OnTextureAssetsChanged;
        EditorApplication.delayCall -= RefreshAfterTextureAssetChanged;
        previewCache.Clear();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24f));
        GUILayout.Label("贴图压缩", EditorStyles.boldLabel, GUILayout.Width(70f));
        GUILayout.FlexibleSpace();

        GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField")
            ?? EditorStyles.toolbarTextField;
        EditorGUI.BeginChangeCheck();
        searchText = EditorGUILayout.TextField(
            searchText,
            searchStyle,
            GUILayout.MinWidth(180f),
            GUILayout.MaxWidth(320f));
        if (EditorGUI.EndChangeCheck())
        {
            isVisibleListDirty = true;
        }

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50f)))
        {
            RefreshTextureAssets();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawFilters()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("排序", GUILayout.Width(30f));
        EditorGUI.BeginChangeCheck();
        sortMode = (TextureSortMode)EditorGUILayout.Popup(
            (int)sortMode,
            SortModeNames,
            EditorStyles.toolbarPopup,
            GUILayout.Width(112f));

        GUILayout.Space(8f);
        GUILayout.Label("类型", GUILayout.Width(30f));
        selectedTextureTypeMask = EditorGUILayout.MaskField(
            selectedTextureTypeMask,
            TextureTypeNames,
            EditorStyles.toolbarPopup,
            GUILayout.Width(180f));

        GUILayout.Space(8f);
        showReadableOnly = GUILayout.Toggle(
            showReadableOnly,
            "仅 Read/Write",
            EditorStyles.toolbarButton,
            GUILayout.Width(92f));
        showUncompressedOnly = GUILayout.Toggle(
            showUncompressedOnly,
            "仅未压缩",
            EditorStyles.toolbarButton,
            GUILayout.Width(76f));
        if (EditorGUI.EndChangeCheck())
        {
            isVisibleListDirty = true;
            scrollPosition = Vector2.zero;
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label(
            new GUIContent(
                dataStatusText,
                "贴图重新导入后会自动刷新。内存数据来自当前 Editor Profiler，不等同于目标设备最终显存。"),
            EditorStyles.miniLabel,
            GUILayout.Width(190f));
        EditorGUILayout.EndHorizontal();
    }

    private void InitializeStyles()
    {
        if (fileSizeStyle != null && memorySizeStyle != null && metricLabelStyle != null)
        {
            return;
        }

        bool isDark = EditorGUIUtility.isProSkin;
        fileSizeStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            normal =
            {
                textColor = isDark
                    ? new Color(0.35f, 0.78f, 1f)
                    : new Color(0.05f, 0.35f, 0.7f)
            }
        };
        memorySizeStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            normal =
            {
                textColor = isDark
                    ? new Color(1f, 0.68f, 0.3f)
                    : new Color(0.75f, 0.3f, 0.05f)
            }
        };
        metricLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };
    }

    private void DrawSummary(List<TextureAssetInfo> visibleAssets)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"项目贴图：{textureAssets.Count}", EditorStyles.boldLabel, GUILayout.Width(110f));
        EditorGUILayout.LabelField($"当前显示：{visibleAssets.Count}", GUILayout.Width(100f));
        EditorGUILayout.LabelField($"已选择：{selectedPaths.Count}", GUILayout.Width(90f));
        EditorGUILayout.LabelField($"文件合计：{visibleFileSizeText}", GUILayout.Width(135f));
        EditorGUILayout.LabelField(
            new GUIContent(
                $"Editor 内存：{visibleRuntimeMemoryText}",
                "通过 Profiler.GetRuntimeMemorySizeLong 获取，只代表当前 Editor 中加载后的 Native 内存，不等同于目标设备最终显存。"),
            GUILayout.Width(165f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"Read/Write：{visibleReadableCount}", GUILayout.Width(105f));
        EditorGUILayout.LabelField($"未压缩：{visibleUncompressedCount}", GUILayout.Width(80f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBatchCompression(List<TextureAssetInfo> visibleAssets)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField("批量 Max Size", EditorStyles.boldLabel, GUILayout.Width(100f));
        targetMaxTextureSize = EditorGUILayout.IntPopup(
            targetMaxTextureSize,
            MaxTextureSizeNames,
            MaxTextureSizeValues,
            GUILayout.Width(90f));

        GUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(visibleAssets.Count == 0))
        {
            if (GUILayout.Button($"全选当前结果（{visibleAssets.Count}）", GUILayout.Width(145f)))
            {
                SetSelected(visibleAssets, true);
            }
        }

        using (new EditorGUI.DisabledScope(selectedPaths.Count == 0))
        {
            if (GUILayout.Button("清空选择", GUILayout.Width(76f)))
            {
                selectedPaths.Clear();
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"已选择 {selectedPaths.Count} 张", GUILayout.Width(95f));

        using (new EditorGUI.DisabledScope(selectedPaths.Count == 0))
        {
            if (GUILayout.Button(
                    $"应用 Max Size {targetMaxTextureSize}",
                    GUILayout.Width(160f),
                    GUILayout.Height(26f)))
            {
                ApplyMaxTextureSize();
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawTextureList(List<TextureAssetInfo> visibleAssets)
    {
        Rect scrollRect = GUILayoutUtility.GetRect(
            1f,
            1f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        if (visibleAssets.Count == 0)
        {
            EditorGUI.HelpBox(
                new Rect(scrollRect.x, scrollRect.y, scrollRect.width, 40f),
                textureAssets.Count == 0
                    ? "项目中没有找到贴图文件。"
                    : "没有符合搜索条件的贴图文件。",
                MessageType.Info);
            return;
        }

        float contentHeight = visibleListContentHeight;
        bool needsVerticalScrollbar = contentHeight > scrollRect.height;
        float contentWidth = Mathf.Max(
            1f,
            scrollRect.width - (needsVerticalScrollbar ? 16f : 0f));
        Rect contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

        float maxScrollY = Mathf.Max(0f, contentHeight - scrollRect.height);
        scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, maxScrollY);
        scrollPosition = GUI.BeginScrollView(scrollRect, scrollPosition, contentRect);

        int firstVisibleIndex = FindFirstVisibleListItem(scrollPosition.y);
        float visibleBottom = scrollPosition.y + scrollRect.height;

        for (int i = firstVisibleIndex; i < visibleListItems.Count; i++)
        {
            TextureListItem listItem = visibleListItems[i];
            if (listItem.Y > visibleBottom)
            {
                break;
            }

            Rect itemRect = new Rect(0f, listItem.Y, contentWidth, listItem.Height);
            if (listItem.IsGroupHeader)
            {
                DrawGroupHeader(listItem, itemRect);
            }
            else
            {
                DrawTextureRow(listItem.TextureAsset, listItem.RowIndex, itemRect);
            }
        }

        GUI.EndScrollView();
    }

    private int FindFirstVisibleListItem(float scrollY)
    {
        int low = 0;
        int high = visibleListItems.Count - 1;
        int result = visibleListItems.Count;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            TextureListItem item = visibleListItems[middle];
            if (item.Y + item.Height > scrollY)
            {
                result = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return result;
    }

    private void DrawGroupHeader(TextureListItem listItem, Rect headerRect)
    {
        bool isDark = EditorGUIUtility.isProSkin;
        EditorGUI.DrawRect(
            headerRect,
            isDark ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.82f, 0.82f, 0.82f));

        bool expanded = GetGroupExpanded(listItem.TextureType);
        string title = $"{GetTextureTypeName(listItem.TextureType)} ({listItem.GroupCount})";
        bool newExpanded = EditorGUI.Foldout(
            new Rect(
                headerRect.x + 8f,
                headerRect.y + 3f,
                Mathf.Max(1f, headerRect.width - 16f),
                EditorGUIUtility.singleLineHeight),
            expanded,
            title,
            true);
        if (newExpanded != expanded)
        {
            groupFoldoutStates[listItem.TextureType] = newExpanded;
            isVisibleListDirty = true;
            Repaint();
        }
    }

    private void DrawTextureRow(TextureAssetInfo textureAsset, int index, Rect rowRect)
    {
        bool isDark = EditorGUIUtility.isProSkin;
        bool isSelected = selectedPaths.Contains(textureAsset.AssetPath);
        bool isHover = rowRect.Contains(Event.current.mousePosition);

        Color backgroundColor;
        if (isSelected)
        {
            backgroundColor = isDark
                ? new Color(0.12f, 0.35f, 0.58f, 0.38f)
                : new Color(0.55f, 0.78f, 1f, 0.45f);
        }
        else if (isHover)
        {
            backgroundColor = isDark
                ? new Color(1f, 1f, 1f, 0.06f)
                : new Color(0f, 0f, 0f, 0.06f);
        }
        else if ((index & 1) == 1)
        {
            backgroundColor = isDark
                ? new Color(1f, 1f, 1f, 0.025f)
                : new Color(0f, 0f, 0f, 0.025f);
        }
        else
        {
            backgroundColor = Color.clear;
        }

        EditorGUI.DrawRect(rowRect, backgroundColor);

        if (isSelected)
        {
            Color selectedColor = isDark
                ? new Color(0.25f, 0.62f, 0.95f, 0.85f)
                : new Color(0.1f, 0.45f, 0.85f, 0.9f);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), selectedColor);
        }

        Rect toggleRect = new Rect(
            rowRect.x + 8f,
            rowRect.y + (rowRect.height - 18f) * 0.5f,
            18f,
            18f);
        bool toggledSelection = EditorGUI.Toggle(toggleRect, isSelected);
        if (toggledSelection != isSelected)
        {
            SetSelected(textureAsset.AssetPath, toggledSelection);
            isSelected = toggledSelection;
        }

        Rect previewRect = new Rect(
            rowRect.x + 32f,
            rowRect.y + (rowRect.height - PreviewSize) * 0.5f,
            PreviewSize,
            PreviewSize);
        EditorGUI.DrawRect(
            previewRect,
            isDark ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.85f, 0.85f, 0.85f));

        Texture preview = GetTexturePreview(textureAsset);

        if (preview != null)
        {
            GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
        }

        float contentX = previewRect.xMax + 10f;
        const float metricWidth = 124f;
        const float metricGap = 6f;
        float metricsX = rowRect.xMax - metricWidth * 2f - metricGap - 8f;
        float contentWidth = Mathf.Max(100f, metricsX - contentX - 8f);
        Rect nameRect = new Rect(
            contentX,
            rowRect.y + 6f,
            contentWidth,
            EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(
            nameRect,
            new GUIContent(textureAsset.FileName, textureAsset.AssetPath),
            EditorStyles.boldLabel);

        Rect pathRect = new Rect(
            contentX,
            rowRect.y + 28f,
            contentWidth,
            EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(
            pathRect,
            new GUIContent(textureAsset.AssetPath, textureAsset.AssetPath),
            EditorStyles.miniLabel);

        Rect basicInfoRect = new Rect(
            contentX,
            rowRect.y + 51f,
            contentWidth,
            EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(
            basicInfoRect,
            new GUIContent(textureAsset.BasicInfoText, textureAsset.BasicInfoText),
            EditorStyles.miniLabel);

        Rect importInfoRect = new Rect(
            contentX,
            rowRect.y + 70f,
            contentWidth,
            EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(
            importInfoRect,
            new GUIContent(
                textureAsset.ImportInfoText,
                textureAsset.ImportTooltip),
            EditorStyles.miniLabel);

        DrawMetric(
            new Rect(metricsX, rowRect.y + 11f, metricWidth, 62f),
            "文件大小",
            textureAsset.FileSizeText,
            fileSizeStyle,
            isDark);
        DrawMetric(
            new Rect(metricsX + metricWidth + metricGap, rowRect.y + 11f, metricWidth, 62f),
            "内存占用",
            textureAsset.RuntimeMemoryText,
            memorySizeStyle,
            isDark);

        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseDown
            && currentEvent.button == 0
            && rowRect.Contains(currentEvent.mousePosition))
        {
            if (previewRect.Contains(currentEvent.mousePosition))
            {
                Selection.activeObject = textureAsset.Texture;
                EditorGUIUtility.PingObject(textureAsset.Texture);
            }
            else if (!toggleRect.Contains(currentEvent.mousePosition))
            {
                SetSelected(textureAsset.AssetPath, !isSelected);
            }

            currentEvent.Use();
        }
    }

    private void DrawMetric(
        Rect rect,
        string label,
        string value,
        GUIStyle valueStyle,
        bool isDark)
    {
        Color backgroundColor = isDark
            ? new Color(0f, 0f, 0f, 0.24f)
            : new Color(1f, 1f, 1f, 0.72f);
        Color borderColor = isDark
            ? new Color(1f, 1f, 1f, 0.1f)
            : new Color(0f, 0f, 0f, 0.12f);
        EditorGUI.DrawRect(rect, backgroundColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), borderColor);

        GUI.Label(new Rect(rect.x, rect.y + 6f, rect.width, 17f), label, metricLabelStyle);
        GUI.Label(new Rect(rect.x, rect.y + 25f, rect.width, 26f), value, valueStyle);
    }

    private Texture GetTexturePreview(TextureAssetInfo textureAsset)
    {
        if (previewCache.TryGetValue(textureAsset.AssetPath, out Texture cachedPreview))
        {
            if (cachedPreview != null)
            {
                return cachedPreview;
            }

            previewCache.Remove(textureAsset.AssetPath);
        }

        Texture preview = AssetPreview.GetMiniThumbnail(textureAsset.Texture);
        if (preview != null)
        {
            previewCache[textureAsset.AssetPath] = preview;
        }

        return preview;
    }

    private void OnTextureAssetsChanged()
    {
        if (isApplyingMaxTextureSize || isAutomaticRefreshQueued)
        {
            return;
        }

        isAutomaticRefreshQueued = true;
        dataStatusText = "贴图已变化，等待更新";
        EditorApplication.delayCall -= RefreshAfterTextureAssetChanged;
        EditorApplication.delayCall += RefreshAfterTextureAssetChanged;
        Repaint();
    }

    private void RefreshAfterTextureAssetChanged()
    {
        EditorApplication.delayCall -= RefreshAfterTextureAssetChanged;
        isAutomaticRefreshQueued = false;
        if (this == null)
        {
            return;
        }

        RefreshTextureAssets();
    }

    private void RefreshTextureAssets()
    {
        dataStatusText = "正在更新数据...";
        HashSet<string> previousSelection = new HashSet<string>(
            selectedPaths,
            StringComparer.OrdinalIgnoreCase);
        textureAssets.Clear();
        visibleTextureAssets.Clear();
        visibleListItems.Clear();
        selectedPaths.Clear();
        previewCache.Clear();
        isVisibleListDirty = true;

        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
        Array.Sort(textureGuids, (left, right) => StringComparer.OrdinalIgnoreCase.Compare(
            AssetDatabase.GUIDToAssetPath(left),
            AssetDatabase.GUIDToAssetPath(right)));

        for (int i = 0; i < textureGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter))
            {
                continue;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                continue;
            }

            var textureAsset = new TextureAssetInfo
            {
                AssetPath = assetPath,
                Texture = texture,
                Width = texture.width,
                Height = texture.height,
                FileSize = GetFileSize(assetPath),
                RuntimeMemory = Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(texture)),
                ImporterType = textureImporter.textureType,
                TextureType = GetTextureTypeName(textureImporter.textureType),
                RuntimeFormat = texture.format.ToString(),
                Compression = textureImporter.textureCompression,
                CompressionQuality = textureImporter.compressionQuality,
                MaxTextureSize = textureImporter.maxTextureSize,
                MipmapEnabled = textureImporter.mipmapEnabled,
                IsReadable = textureImporter.isReadable
            };
            textureAsset.FileName = Path.GetFileName(textureAsset.AssetPath);
            textureAsset.FileSizeText = FormatBytes(textureAsset.FileSize);
            textureAsset.RuntimeMemoryText = FormatBytes(textureAsset.RuntimeMemory);
            textureAsset.BasicInfoText =
                $"尺寸 {textureAsset.Width} x {textureAsset.Height}    "
                + $"类型 {textureAsset.TextureType}    格式 {textureAsset.RuntimeFormat}";
            textureAsset.ImportInfoText =
                $"Max Size {textureAsset.MaxTextureSize}    "
                + $"压缩 {GetCompressionName(textureAsset.Compression)}    "
                + $"Mipmap {GetSwitchText(textureAsset.MipmapEnabled)}    "
                + $"Read/Write {GetSwitchText(textureAsset.IsReadable)}";
            textureAsset.ImportTooltip =
                $"压缩质量：{textureAsset.CompressionQuality}\n"
                + "运行时内存来自当前 Editor 的 Profiler 数据。";
            textureAssets.Add(textureAsset);

            if (previousSelection.Contains(assetPath))
            {
                selectedPaths.Add(assetPath);
            }
        }

        scrollPosition = Vector2.zero;
        dataStatusText = "数据已更新 " + DateTime.Now.ToString("HH:mm:ss");
        Repaint();
    }

    private List<TextureAssetInfo> GetVisibleTextureAssets()
    {
        if (!isVisibleListDirty)
        {
            return visibleTextureAssets;
        }

        string keyword = string.IsNullOrWhiteSpace(searchText)
            ? string.Empty
            : searchText.Trim();
        visibleTextureAssets.Clear();

        long totalFileSize = 0L;
        long totalRuntimeMemory = 0L;
        visibleReadableCount = 0;
        visibleUncompressedCount = 0;

        for (int i = 0; i < textureAssets.Count; i++)
        {
            TextureAssetInfo textureAsset = textureAssets[i];
            bool matches = (keyword.Length == 0
                    || textureAsset.AssetPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                    || textureAsset.TextureType.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                    || textureAsset.RuntimeFormat.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                && (!showReadableOnly || textureAsset.IsReadable)
                && (!showUncompressedOnly
                    || textureAsset.Compression == TextureImporterCompression.Uncompressed)
                && IsTextureTypeSelected(textureAsset.ImporterType);
            if (!matches)
            {
                continue;
            }

            visibleTextureAssets.Add(textureAsset);
            totalFileSize += textureAsset.FileSize;
            totalRuntimeMemory += textureAsset.RuntimeMemory;
            if (textureAsset.IsReadable) visibleReadableCount++;
            if (textureAsset.Compression == TextureImporterCompression.Uncompressed)
            {
                visibleUncompressedCount++;
            }
        }

        visibleTextureAssets.Sort(CompareTextureAssets);
        RebuildVisibleListItems();
        visibleFileSizeText = FormatBytes(totalFileSize);
        visibleRuntimeMemoryText = FormatBytes(totalRuntimeMemory);
        isVisibleListDirty = false;
        return visibleTextureAssets;
    }

    private void RebuildVisibleListItems()
    {
        visibleListItems.Clear();
        visibleListContentHeight = 0f;
        int rowIndex = 0;

        for (int typeIndex = 0; typeIndex < TextureTypes.Length; typeIndex++)
        {
            TextureImporterType textureType = TextureTypes[typeIndex];
            int groupCount = 0;
            for (int i = 0; i < visibleTextureAssets.Count; i++)
            {
                if (visibleTextureAssets[i].ImporterType == textureType)
                {
                    groupCount++;
                }
            }

            if (groupCount == 0)
            {
                continue;
            }

            visibleListItems.Add(new TextureListItem
            {
                TextureType = textureType,
                GroupCount = groupCount,
                Y = visibleListContentHeight,
                Height = GroupHeaderHeight
            });
            visibleListContentHeight += GroupHeaderHeight;

            if (!GetGroupExpanded(textureType))
            {
                continue;
            }

            for (int i = 0; i < visibleTextureAssets.Count; i++)
            {
                TextureAssetInfo textureAsset = visibleTextureAssets[i];
                if (textureAsset.ImporterType != textureType)
                {
                    continue;
                }

                visibleListItems.Add(new TextureListItem
                {
                    TextureAsset = textureAsset,
                    TextureType = textureType,
                    RowIndex = rowIndex++,
                    Y = visibleListContentHeight,
                    Height = RowHeight
                });
                visibleListContentHeight += RowHeight;
            }
        }
    }

    private bool GetGroupExpanded(TextureImporterType textureType)
    {
        return !groupFoldoutStates.TryGetValue(textureType, out bool expanded) || expanded;
    }

    private bool IsTextureTypeSelected(TextureImporterType textureType)
    {
        for (int i = 0; i < TextureTypes.Length; i++)
        {
            if (TextureTypes[i] == textureType)
            {
                return (selectedTextureTypeMask & (1 << i)) != 0;
            }
        }

        return false;
    }

    private int CompareTextureAssets(TextureAssetInfo left, TextureAssetInfo right)
    {
        int result;
        switch (sortMode)
        {
            case TextureSortMode.FileSizeDescending:
                result = right.FileSize.CompareTo(left.FileSize);
                break;
            case TextureSortMode.PixelCountDescending:
                long leftPixelCount = (long)left.Width * left.Height;
                long rightPixelCount = (long)right.Width * right.Height;
                result = rightPixelCount.CompareTo(leftPixelCount);
                break;
            case TextureSortMode.AssetPath:
                return StringComparer.OrdinalIgnoreCase.Compare(left.AssetPath, right.AssetPath);
            default:
                result = right.RuntimeMemory.CompareTo(left.RuntimeMemory);
                break;
        }

        return result != 0
            ? result
            : StringComparer.OrdinalIgnoreCase.Compare(left.AssetPath, right.AssetPath);
    }

    private void SetSelected(string assetPath, bool selected)
    {
        if (selected)
        {
            selectedPaths.Add(assetPath);
        }
        else
        {
            selectedPaths.Remove(assetPath);
        }

        Repaint();
    }

    private void SetSelected(List<TextureAssetInfo> assets, bool selected)
    {
        for (int i = 0; i < assets.Count; i++)
        {
            SetSelected(assets[i].AssetPath, selected);
        }
    }

    private void ApplyMaxTextureSize()
    {
        List<TextureAssetInfo> selectedAssets = textureAssets.FindAll(textureAsset =>
            selectedPaths.Contains(textureAsset.AssetPath));
        if (selectedAssets.Count == 0)
        {
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "批量设置贴图 Max Size",
                $"即将把 {selectedAssets.Count} 张贴图的 Default Max Size 设置为 {targetMaxTextureSize}。\n\n"
                + "Unity 会重新导入发生变化的贴图。",
                "开始处理",
                "取消"))
        {
            return;
        }

        int changedCount = 0;
        int skippedCount = 0;
        bool canceled = false;
        var failedAssets = new List<string>();

        isApplyingMaxTextureSize = true;
        try
        {
            for (int i = 0; i < selectedAssets.Count; i++)
            {
                TextureAssetInfo textureAsset = selectedAssets[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "正在设置贴图 Max Size",
                        textureAsset.AssetPath,
                        (float)i / selectedAssets.Count))
                {
                    canceled = true;
                    break;
                }

                try
                {
                    TextureImporter textureImporter =
                        AssetImporter.GetAtPath(textureAsset.AssetPath) as TextureImporter;
                    if (textureImporter == null)
                    {
                        failedAssets.Add(textureAsset.AssetPath);
                        continue;
                    }

                    if (textureImporter.maxTextureSize == targetMaxTextureSize)
                    {
                        skippedCount++;
                        continue;
                    }

                    Undo.RecordObject(textureImporter, "批量设置贴图 Max Size");
                    textureImporter.maxTextureSize = targetMaxTextureSize;
                    textureImporter.SaveAndReimport();
                    changedCount++;
                }
                catch (Exception exception)
                {
                    failedAssets.Add(textureAsset.AssetPath + ": " + exception.Message);
                }
            }
        }
        finally
        {
            isApplyingMaxTextureSize = false;
            EditorUtility.ClearProgressBar();
        }

        if (failedAssets.Count > 0)
        {
            Debug.LogError("以下贴图设置 Max Size 失败：\n" + string.Join("\n", failedAssets));
        }

        RefreshTextureAssets();

        string result = $"已修改 {changedCount} 张，跳过 {skippedCount} 张，失败 {failedAssets.Count} 张。";
        if (canceled)
        {
            result += "\n操作已取消，已完成的修改会保留。";
        }
        if (failedAssets.Count > 0)
        {
            result += "\n失败详情已输出到 Console。";
        }

        EditorUtility.DisplayDialog("Max Size 设置完成", result, "确定");
    }

    private static long GetFileSize(string assetPath)
    {
        string projectDirectory = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return 0L;
        }

        string absolutePath = Path.Combine(
            projectDirectory,
            assetPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : 0L;
    }

    private static string GetTextureTypeName(TextureImporterType textureType)
    {
        switch (textureType)
        {
            case TextureImporterType.NormalMap:
                return "Normal map";
            case TextureImporterType.GUI:
                return "Editor GUI and Legacy GUI";
            case TextureImporterType.Sprite:
                return "Sprite (2D and UI)";
            case TextureImporterType.Cursor:
                return "Cursor";
            case TextureImporterType.Cookie:
                return "Cookie";
            case TextureImporterType.Lightmap:
                return "Lightmap";
            case TextureImporterType.DirectionalLightmap:
                return "Directional Lightmap";
            case TextureImporterType.Shadowmask:
                return "Shadowmask";
            case TextureImporterType.SingleChannel:
                return "Single Channel";
            default:
                return "Default";
        }
    }

    private static string GetCompressionName(TextureImporterCompression compression)
    {
        switch (compression)
        {
            case TextureImporterCompression.Uncompressed:
                return "未压缩";
            case TextureImporterCompression.CompressedLQ:
                return "低质量";
            case TextureImporterCompression.CompressedHQ:
                return "高质量";
            default:
                return "普通";
        }
    }

    private static string GetSwitchText(bool enabled)
    {
        return enabled ? "开" : "关";
    }

    private static string FormatBytes(long byteCount)
    {
        if (byteCount < 1024L)
        {
            return byteCount + " B";
        }

        if (byteCount < 1024L * 1024L)
        {
            return (byteCount / 1024f).ToString("0.##") + " KB";
        }

        if (byteCount < 1024L * 1024L * 1024L)
        {
            return (byteCount / (1024f * 1024f)).ToString("0.##") + " MB";
        }

        return (byteCount / (1024f * 1024f * 1024f)).ToString("0.##") + " GB";
    }
}

internal sealed class TextureCompressionAssetPostprocessor : AssetPostprocessor
{
    public static event Action TextureAssetsChanged;

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (ContainsTextureAsset(importedAssets)
            || ContainsTextureAsset(deletedAssets)
            || ContainsTextureAsset(movedAssets)
            || ContainsTextureAsset(movedFromAssetPaths))
        {
            TextureAssetsChanged?.Invoke();
        }
    }

    private static bool ContainsTextureAsset(string[] assetPaths)
    {
        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter
                || IsTextureFileExtension(Path.GetExtension(assetPath)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTextureFileExtension(string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".bmp":
            case ".dds":
            case ".exr":
            case ".gif":
            case ".hdr":
            case ".iff":
            case ".jpeg":
            case ".jpg":
            case ".pict":
            case ".png":
            case ".psd":
            case ".tga":
            case ".tif":
            case ".tiff":
                return true;
            default:
                return false;
        }
    }
}
