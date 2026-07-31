using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 扫描并导出项目中的模型源文件。
/// </summary>
public sealed class ModelFileExporterWindow : EditorWindow
{
    private const string OutputDirectoryPreference = "Cowart.EditorTools.ModelFileExporter.OutputDirectory";
    private const string LegacyOutputDirectoryPreference = "JingGuan.ModelFileExporter.OutputDirectory";
    private const float PreviewSize = 56f;
    private const float RowHeight = 66f;

    private readonly List<ModelFileInfo> modelFiles = new List<ModelFileInfo>();
    private readonly HashSet<string> selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private Vector2 scrollPosition;
    private string searchText = string.Empty;
    private string outputDirectory;
    private bool includeMetaFiles;
    private bool hasInitializedSelection;
    private ModelFileInfo hoverPreviewFile;

    private sealed class ModelFileInfo
    {
        public string AssetPath;
        public UnityEngine.Object Asset;
    }

    private static class Styles
    {
        public static GUIStyle StatValue;
        public static GUIStyle StatLabel;
        public static GUIStyle BadgeExtension;
        public static GUIStyle SectionHeader;
        private static bool isInitialized;

        public static void InitIfNeeded()
        {
            if (isInitialized && StatValue != null) return;
            isInitialized = true;

            bool isDark = EditorGUIUtility.isProSkin;

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

            BadgeExtension = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = isDark ? new Color(0.95f, 0.75f, 0.35f) : new Color(0.75f, 0.45f, 0.1f) }
            };

            SectionHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = isDark ? new Color(0.9f, 0.92f, 0.95f) : new Color(0.15f, 0.15f, 0.15f) }
            };
        }
    }

    [MenuItem("Cowart/项目开发工具/模型导出")]
    private static void OpenWindow()
    {
        ModelFileExporterWindow window = GetWindow<ModelFileExporterWindow>();
        window.titleContent = new GUIContent("模型导出");
        window.minSize = new Vector2(620f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        string projectDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        outputDirectory = EditorPrefs.GetString(
            OutputDirectoryPreference,
            EditorPrefs.GetString(
                LegacyOutputDirectoryPreference,
                Path.Combine(projectDirectory, "ModelExports")));
        RefreshModelFiles();
    }

    private void OnGUI()
    {
        hoverPreviewFile = null;
        Styles.InitIfNeeded();
        DrawToolbar();
        DrawSummaryAndSelection();
        DrawModelList();
        DrawExportSection();

        if (hoverPreviewFile != null)
        {
            DrawHoverPreviewOverlay(hoverPreviewFile);
        }

        if (AssetPreview.IsLoadingAssetPreviews() || hoverPreviewFile != null)
        {
            Repaint();
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24f));
        GUILayout.Label("📦 模型文件导出", EditorStyles.boldLabel, GUILayout.Width(110f));
        GUILayout.FlexibleSpace();

        GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField;
        searchText = EditorGUILayout.TextField(
            searchText,
            searchStyle,
            GUILayout.MinWidth(180f),
            GUILayout.MaxWidth(320f));

        if (GUILayout.Button(" 刷新 ", EditorStyles.toolbarButton, GUILayout.Width(58f)))
        {
            RefreshModelFiles();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSummaryAndSelection()
    {
        List<ModelFileInfo> visibleFiles = GetVisibleFiles();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 统计面板 Badges
        EditorGUILayout.BeginHorizontal();
        DrawStatCard("项目模型总数", modelFiles.Count.ToString(), "📦");
        DrawStatCard("当前显示结果", visibleFiles.Count.ToString(), "👁️");
        DrawStatCard("当前已选择", selectedPaths.Count.ToString(), "☑️");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);

        // 选择控制按钮栏
        EditorGUILayout.BeginHorizontal();
        bool allVisibleSelected = visibleFiles.Count > 0
            && visibleFiles.TrueForAll(file => selectedPaths.Contains(file.AssetPath));
        using (new EditorGUI.DisabledScope(visibleFiles.Count == 0))
        {
            string btnText = allVisibleSelected ? "取消全选当前结果" : $"全选当前结果（{visibleFiles.Count}）";
            if (GUILayout.Button(btnText, GUILayout.Height(22f), GUILayout.Width(160f)))
            {
                for (int i = 0; i < visibleFiles.Count; i++)
                {
                    if (allVisibleSelected)
                    {
                        selectedPaths.Remove(visibleFiles[i].AssetPath);
                    }
                    else
                    {
                        selectedPaths.Add(visibleFiles[i].AssetPath);
                    }
                }
            }
        }

        if (selectedPaths.Count > 0)
        {
            if (GUILayout.Button($"清空已选（{selectedPaths.Count}）", GUILayout.Height(22f), GUILayout.Width(110f)))
            {
                selectedPaths.Clear();
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawStatCard(string label, string value, string icon)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField($"{icon} {value}", Styles.StatValue);
        EditorGUILayout.LabelField(label, Styles.StatLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawModelList()
    {
        List<ModelFileInfo> visibleFiles = GetVisibleFiles();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

        if (visibleFiles.Count == 0)
        {
            EditorGUILayout.HelpBox(
                modelFiles.Count == 0 ? "项目中没有找到模型文件。" : "没有符合搜索条件的模型文件。",
                MessageType.Info);
        }

        for (int i = 0; i < visibleFiles.Count; i++)
        {
            DrawModelRow(visibleFiles[i], i);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawModelRow(ModelFileInfo modelFile, int index)
    {
        float rowHeight = 70f;
        float previewSize = 58f;
        Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);

        rowRect.y += 2f;
        rowRect.height -= 4f;

        bool isDark = EditorGUIUtility.isProSkin;
        bool selected = selectedPaths.Contains(modelFile.AssetPath);
        bool isHover = rowRect.Contains(Event.current.mousePosition);

        Color bgColor;
        if (selected)
        {
            bgColor = isDark ? new Color(0.18f, 0.38f, 0.65f, 0.3f) : new Color(0.7f, 0.85f, 1f, 0.45f);
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

        Rect toggleRect = new Rect(rowRect.x + 8f, rowRect.y + 24f, 18f, 18f);

        Rect previewRect = new Rect(rowRect.x + 32f, rowRect.y + 4f, previewSize, previewSize);
        Color previewBg = isDark ? new Color(0.12f, 0.12f, 0.12f, 0.8f) : new Color(0.85f, 0.85f, 0.85f, 0.8f);
        EditorGUI.DrawRect(previewRect, previewBg);

        if (previewRect.Contains(Event.current.mousePosition))
        {
            hoverPreviewFile = modelFile;
        }

        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseDown
            && currentEvent.button == 0
            && rowRect.Contains(currentEvent.mousePosition)
            && !toggleRect.Contains(currentEvent.mousePosition)
            && !previewRect.Contains(currentEvent.mousePosition))
        {
            selected = !selected;
            SetSelected(modelFile.AssetPath, selected);
            currentEvent.Use();
        }

        bool newSelected = EditorGUI.Toggle(toggleRect, selected);
        if (newSelected != selected)
        {
            SetSelected(modelFile.AssetPath, newSelected);
        }

        Texture preview = AssetPreview.GetAssetPreview(modelFile.Asset);
        if (preview == null)
        {
            preview = AssetPreview.GetMiniThumbnail(modelFile.Asset);
        }

        if (preview != null)
        {
            GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
        }

        if (GUI.Button(previewRect, GUIContent.none, GUIStyle.none))
        {
            SelectInProject(modelFile.Asset);
        }

        float contentX = previewRect.xMax + 10f;
        float contentWidth = Mathf.Max(80f, rowRect.xMax - contentX - 10f);
        string fileName = Path.GetFileName(modelFile.AssetPath);
        string extension = Path.GetExtension(modelFile.AssetPath).ToUpperInvariant().TrimStart('.');

        float badgeWidth = 44f;
        Rect badgeRect = new Rect(contentX, rowRect.y + 10f, badgeWidth, 16f);
        Color badgeBg = isDark ? new Color(0.35f, 0.25f, 0.1f, 0.4f) : new Color(0.95f, 0.9f, 0.8f, 0.9f);
        EditorGUI.DrawRect(badgeRect, badgeBg);
        GUI.Label(badgeRect, extension, Styles.BadgeExtension);

        Rect nameRect = new Rect(contentX + badgeWidth + 6f, rowRect.y + 8f, contentWidth - badgeWidth - 6f, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(nameRect, new GUIContent(fileName, modelFile.AssetPath), EditorStyles.boldLabel);

        Rect pathRect = new Rect(contentX, rowRect.y + 34f, contentWidth, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(pathRect, new GUIContent(modelFile.AssetPath, modelFile.AssetPath), EditorStyles.miniLabel);
    }

    private void DrawHoverPreviewOverlay(ModelFileInfo modelFile)
    {
        Texture preview = AssetPreview.GetAssetPreview(modelFile.Asset) ?? AssetPreview.GetMiniThumbnail(modelFile.Asset);
        if (preview == null) return;

        float overlaySize = 180f;
        float padding = 8f;
        float textHeight = 20f;
        float totalWidth = overlaySize + padding * 2f;
        float totalHeight = overlaySize + padding * 2f + textHeight;

        Vector2 mousePos = Event.current.mousePosition;
        float x = mousePos.x + 16f;
        float y = mousePos.y + 16f;

        if (x + totalWidth > position.width - 10f)
        {
            x = mousePos.x - totalWidth - 10f;
        }
        if (y + totalHeight > position.height - 10f)
        {
            y = position.height - totalHeight - 10f;
        }
        if (x < 10f) x = 10f;
        if (y < 10f) y = 10f;

        Rect overlayRect = new Rect(x, y, totalWidth, totalHeight);
        bool isDark = EditorGUIUtility.isProSkin;

        Color bg = isDark ? new Color(0.12f, 0.12f, 0.12f, 0.96f) : new Color(0.95f, 0.95f, 0.95f, 0.96f);
        Color border = isDark ? new Color(0.3f, 0.6f, 0.95f, 0.85f) : new Color(0.2f, 0.5f, 0.9f, 0.85f);

        EditorGUI.DrawRect(overlayRect, bg);

        EditorGUI.DrawRect(new Rect(overlayRect.x, overlayRect.y, overlayRect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(overlayRect.x, overlayRect.yMax - 1f, overlayRect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(overlayRect.x, overlayRect.y, 1f, overlayRect.height), border);
        EditorGUI.DrawRect(new Rect(overlayRect.xMax - 1f, overlayRect.y, 1f, overlayRect.height), border);

        Rect imageRect = new Rect(x + padding, y + padding, overlaySize, overlaySize);
        Color imgBg = isDark ? new Color(0.05f, 0.05f, 0.05f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
        EditorGUI.DrawRect(imageRect, imgBg);
        GUI.DrawTexture(imageRect, preview, ScaleMode.ScaleToFit);

        Rect labelRect = new Rect(x + padding, y + padding + overlaySize + 2f, overlaySize, textHeight);
        string fileName = Path.GetFileName(modelFile.AssetPath);
        EditorGUI.LabelField(labelRect, new GUIContent(fileName, modelFile.AssetPath), EditorStyles.boldLabel);
    }

    private void DrawExportSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.LabelField("🚀 模型导出配置与操作", Styles.SectionHeader);
        EditorGUILayout.Space(2f);

        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 70f;

        EditorGUILayout.BeginHorizontal();
        outputDirectory = EditorGUILayout.TextField("导出到", outputDirectory);
        if (GUILayout.Button("浏览...", GUILayout.Width(64f)))
        {
            string selectedDirectory = EditorUtility.OpenFolderPanel(
                "选择模型导出目录",
                outputDirectory,
                string.Empty);
            if (!string.IsNullOrEmpty(selectedDirectory))
            {
                outputDirectory = selectedDirectory;
                EditorPrefs.SetString(OutputDirectoryPreference, outputDirectory);
                GUI.FocusControl(null);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUIUtility.labelWidth = oldLabelWidth;

        includeMetaFiles = EditorGUILayout.ToggleLeft("同时导出对应的 .meta 配置文件", includeMetaFiles);

        string pathError = GetOutputPathError();
        if (!string.IsNullOrEmpty(pathError))
        {
            EditorGUILayout.HelpBox($"⚠️ {pathError}", MessageType.Warning);
        }

        bool canExport = selectedPaths.Count > 0 && string.IsNullOrEmpty(pathError);
        Color oldBg = GUI.backgroundColor;
        if (canExport)
        {
            GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.2f, 0.65f, 0.4f) : new Color(0.3f, 0.8f, 0.4f);
        }

        using (new EditorGUI.DisabledScope(!canExport))
        {
            if (GUILayout.Button($"🚀 导出所选模型（{selectedPaths.Count}）", GUILayout.Height(32f)))
            {
                ExportSelectedModels();
            }
        }
        GUI.backgroundColor = oldBg;

        EditorGUILayout.EndVertical();
    }

    private void RefreshModelFiles()
    {
        HashSet<string> previousSelection = new HashSet<string>(selectedPaths, StringComparer.OrdinalIgnoreCase);
        modelFiles.Clear();
        selectedPaths.Clear();

        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
        Array.Sort(modelGuids, (left, right) => StringComparer.OrdinalIgnoreCase.Compare(
            AssetDatabase.GUIDToAssetPath(left),
            AssetDatabase.GUIDToAssetPath(right)));

        for (int i = 0; i < modelGuids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(modelGuids[i]);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null || !File.Exists(ToAbsolutePath(assetPath)))
            {
                continue;
            }

            modelFiles.Add(new ModelFileInfo
            {
                AssetPath = assetPath,
                Asset = asset
            });

            if (!hasInitializedSelection || previousSelection.Contains(assetPath))
            {
                selectedPaths.Add(assetPath);
            }
        }

        hasInitializedSelection = true;
        Repaint();
    }

    private List<ModelFileInfo> GetVisibleFiles()
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return new List<ModelFileInfo>(modelFiles);
        }

        string keyword = searchText.Trim();
        return modelFiles.FindAll(file =>
            file.AssetPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
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
    }

    private static void SelectInProject(UnityEngine.Object asset)
    {
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private string GetOutputPathError()
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return "请选择导出目录。";
        }

        try
        {
            string normalizedOutput = Path.GetFullPath(outputDirectory);
            if (File.Exists(normalizedOutput))
            {
                return "导出路径指向文件，请选择一个文件夹。";
            }

            string normalizedAssets = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if ((normalizedOutput + Path.DirectorySeparatorChar).StartsWith(
                    normalizedAssets,
                    StringComparison.OrdinalIgnoreCase))
            {
                return "导出目录不能位于项目的 Assets 文件夹内，否则导出文件会被再次扫描为项目模型。";
            }
        }
        catch (Exception)
        {
            return "导出目录格式无效。";
        }

        return null;
    }

    private void ExportSelectedModels()
    {
        EditorPrefs.SetString(OutputDirectoryPreference, outputDirectory);

        string exportRoot = CreateUniqueExportDirectory(outputDirectory);
        List<ModelFileInfo> filesToExport = modelFiles.FindAll(file => selectedPaths.Contains(file.AssetPath));
        var failedFiles = new List<string>();
        int exportedCount = 0;
        bool canceled = false;

        try
        {
            Directory.CreateDirectory(exportRoot);

            for (int i = 0; i < filesToExport.Count; i++)
            {
                ModelFileInfo modelFile = filesToExport[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "正在导出模型",
                        modelFile.AssetPath,
                        (float)i / filesToExport.Count))
                {
                    canceled = true;
                    break;
                }

                try
                {
                    ExportModelFile(modelFile.AssetPath, exportRoot);
                    exportedCount++;
                }
                catch (Exception exception)
                {
                    failedFiles.Add($"{modelFile.AssetPath}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("模型导出失败", exception.Message, "确定");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (failedFiles.Count > 0)
        {
            Debug.LogError("以下模型导出失败：\n" + string.Join("\n", failedFiles));
        }

        EditorUtility.RevealInFinder(exportRoot);
        EditorUtility.DisplayDialog(
            canceled ? "模型导出已取消" : "模型导出完成",
            $"成功导出 {exportedCount} 个模型，失败 {failedFiles.Count} 个。"
            + (canceled ? "\n未处理的模型已跳过。" : string.Empty)
            + $"\n\n导出位置：\n{exportRoot}",
            "确定");
    }

    private void ExportModelFile(string assetPath, string exportRoot)
    {
        string relativePath = assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            ? assetPath.Substring("Assets/".Length)
            : Path.GetFileName(assetPath);
        string sourcePath = ToAbsolutePath(assetPath);
        string destinationPath = Path.Combine(
            exportRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        string destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, false);

        if (includeMetaFiles && File.Exists(sourcePath + ".meta"))
        {
            File.Copy(sourcePath + ".meta", destinationPath + ".meta", false);
        }
    }

    private static string CreateUniqueExportDirectory(string parentDirectory)
    {
        string directoryName = "ModelExport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string candidate = Path.Combine(Path.GetFullPath(parentDirectory), directoryName);
        int suffix = 1;

        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(
                Path.GetFullPath(parentDirectory),
                directoryName + "_" + suffix.ToString("00"));
            suffix++;
        }

        return candidate;
    }

    private static string ToAbsolutePath(string assetPath)
    {
        string projectDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.GetFullPath(Path.Combine(projectDirectory, assetPath));
    }
}
