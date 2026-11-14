using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>
/// 源码提取工具窗口（大气简约美化版）。
/// </summary>
public sealed class SourceCodeExtractorWindow : EditorWindow
{
    private const string PreferencesPrefix = "Cowart.EditorUtilities.SourceCodeExtractor.";
    // 按优先级读取改名前保存的设置：Editor Tools 时期的键，以及更早的 JingGuan 键。
    private static readonly string[] LegacyPreferencesPrefixes =
    {
        "Cowart.EditorTools.SourceCodeExtractor.",
        "JingGuan.SourceCodeExtractor."
    };
    private const string DefaultExtensions = ".cs;.shader;.compute;.cginc;.hlsl;.json;.xml;.uss;.uxml";
    private const string DefaultExcludedDirectories = "Library;Temp;Obj;obj;Logs;.git;.svn";

    private readonly string[] modeNames = { "📄  合并为单个文本", "📁  按原目录结构复制" };

    private string sourceDirectory;
    private string outputDirectory;
    private string extensions;
    private string excludedDirectories;
    private bool includeMetaFiles;
    private bool includePackages;
    private int selectedMode;
    private Vector2 scrollPosition;
    private Vector2 previewScrollPosition;

    // 动态扫描预览数据
    private readonly List<SourceCodeFile> matchedFiles = new List<SourceCodeFile>();
    private int matchedFileCount;
    private bool isSourcePathValid;
    private string scanError;

    // 样式缓存
    private static Styles styles;

    private class Styles
    {
        public GUIStyle HeaderTitle;
        public GUIStyle HeaderSubtitle;
        public GUIStyle CardHeader;
        public GUIStyle CardBackground;
        public GUIStyle ChipButton;
        public GUIStyle PrimaryButton;
        public GUIStyle StatusBadge;

        public Styles()
        {
            bool isDark = EditorGUIUtility.isProSkin;

            HeaderTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = isDark ? new Color(0.92f, 0.94f, 0.96f) : new Color(0.12f, 0.14f, 0.16f) }
            };

            HeaderSubtitle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = isDark ? new Color(0.65f, 0.68f, 0.72f) : new Color(0.45f, 0.47f, 0.5f) }
            };

            CardHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = isDark ? new Color(0.85f, 0.88f, 0.92f) : new Color(0.2f, 0.22f, 0.25f) },
                margin = new RectOffset(0, 0, 2, 4)
            };

            CardBackground = new GUIStyle("helpBox")
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 4, 8)
            };

            ChipButton = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fixedHeight = 22,
                padding = new RectOffset(10, 10, 2, 2)
            };

            PrimaryButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 38,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isDark ? Color.white : new Color(0.1f, 0.1f, 0.1f) }
            };

            StatusBadge = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                richText = true,
                alignment = TextAnchor.MiddleRight
            };
        }
    }

    [MenuItem("Cowart/项目/源码导出")]
    private static void OpenWindow()
    {
        var window = GetWindow<SourceCodeExtractorWindow>();
        window.titleContent = new GUIContent("源码提取");
        window.minSize = new Vector2(580f, 470f);
        window.Show();
    }

    private void OnEnable()
    {
        string projectDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string defaultSourceDirectory = Application.dataPath;
        string defaultOutputDirectory = Path.Combine(projectDirectory, "SourceCodeExports");

        sourceDirectory = GetStringPreference("SourceDirectory", defaultSourceDirectory);
        outputDirectory = GetStringPreference("OutputDirectory", defaultOutputDirectory);
        extensions = GetStringPreference("Extensions", DefaultExtensions);
        excludedDirectories = GetStringPreference(
            "ExcludedDirectories",
            DefaultExcludedDirectories);
        includeMetaFiles = GetBoolPreference("IncludeMetaFiles", false);
        includePackages = GetBoolPreference("IncludePackages", false);
        selectedMode = GetIntPreference("Mode", 0);

        RefreshScanPreview();
    }

    private void OnGUI()
    {
        if (styles == null)
        {
            styles = new Styles();
        }

        DrawHeader();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        EditorGUI.BeginChangeCheck();

        DrawPathSection();
        DrawRuleSection();
        DrawStatusPreviewCard();

        if (EditorGUI.EndChangeCheck())
        {
            SavePreferences();
            RefreshScanPreview();
        }

        EditorGUILayout.EndScrollView();

        DrawFooter();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Space(6f);

        GUILayout.Label("源码提取工具", styles.HeaderTitle);
        GUILayout.Space(6f);
        GUILayout.Label("Source Code Extractor", styles.HeaderSubtitle);

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("🔄 重置规则", EditorStyles.toolbarButton, GUILayout.Width(76f)))
        {
            if (EditorUtility.DisplayDialog("重置提取规则", "确定恢复为默认的提取扩展名与排除目录吗？", "重置", "取消"))
            {
                extensions = DefaultExtensions;
                excludedDirectories = DefaultExcludedDirectories;
                includeMetaFiles = false;
                includePackages = false;
                selectedMode = 0;
                SavePreferences();
                RefreshScanPreview();
                GUI.FocusControl(null);
            }
        }

        GUILayout.Space(2f);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4f);
    }

    private void DrawPathSection()
    {
        EditorGUILayout.BeginVertical(styles.CardBackground);

        EditorGUILayout.LabelField("📁  路径配置", styles.CardHeader);
        EditorGUILayout.Space(4f);

        // 源码目录
        EditorGUILayout.BeginHorizontal();
        sourceDirectory = EditorGUILayout.TextField("源码目录", sourceDirectory);
        if (GUILayout.Button("浏览...", GUILayout.Width(58f)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择源码目录", sourceDirectory, string.Empty);
            if (!string.IsNullOrEmpty(selected))
            {
                sourceDirectory = selected;
                GUI.FocusControl(null);
            }
        }
        if (GUILayout.Button(new GUIContent("📍 Project 选中项", "将路径设为 Project 窗口中当前选中的目录"), GUILayout.Width(100f)))
        {
            TryUseProjectSelection();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);

        // 输出目录
        EditorGUILayout.BeginHorizontal();
        outputDirectory = EditorGUILayout.TextField("输出目录", outputDirectory);
        if (GUILayout.Button("浏览...", GUILayout.Width(58f)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择输出目录", outputDirectory, string.Empty);
            if (!string.IsNullOrEmpty(selected))
            {
                outputDirectory = selected;
                GUI.FocusControl(null);
            }
        }
        using (new EditorGUI.DisabledScope(!Directory.Exists(outputDirectory)))
        {
            if (GUILayout.Button(new GUIContent("📂 打开", "在系统文件管理器中打开输出目录"), GUILayout.Width(100f)))
            {
                EditorUtility.RevealInFinder(outputDirectory);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawRuleSection()
    {
        EditorGUILayout.BeginVertical(styles.CardBackground);

        EditorGUILayout.LabelField("⚙️  提取规则", styles.CardHeader);
        EditorGUILayout.Space(4f);

        // 输出方式 (分段 Toolbar)
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("输出方式");
        selectedMode = GUILayout.Toolbar(selectedMode, modeNames, GUILayout.Height(24f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6f);

        // 文件扩展名
        extensions = EditorGUILayout.TextField("文件扩展名", extensions);

        // 扩展名快捷预设 Chips
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUIUtility.labelWidth);
        if (GUILayout.Button("仅 C# 代码", styles.ChipButton))
        {
            extensions = ".cs";
            GUI.FocusControl(null);
        }
        if (GUILayout.Button("代码与着色器", styles.ChipButton))
        {
            extensions = ".cs;.shader;.compute;.cginc;.hlsl";
            GUI.FocusControl(null);
        }
        if (GUILayout.Button("包含配置文件", styles.ChipButton))
        {
            extensions = ".cs;.shader;.compute;.cginc;.hlsl;.json;.xml";
            GUI.FocusControl(null);
        }
        if (GUILayout.Button("全部默认", styles.ChipButton))
        {
            extensions = DefaultExtensions;
            GUI.FocusControl(null);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6f);

        // 排除目录名
        excludedDirectories = EditorGUILayout.TextField("排除目录名", excludedDirectories);

        EditorGUILayout.Space(4f);

        // 包含 .meta
        EditorGUILayout.BeginHorizontal();
        includeMetaFiles = EditorGUILayout.Toggle("包含 .meta 文件", includeMetaFiles);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        includePackages = EditorGUILayout.Toggle(
            new GUIContent(
                "包含 Packages 包",
                "包含 Unity 当前已注册的全部包，预览和导出路径统一使用 Packages/<包名>/..."),
            includePackages);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawStatusPreviewCard()
    {
        EditorGUILayout.BeginVertical(styles.CardBackground);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("📊  扫描状态与预览", styles.CardHeader);

        string statusText = isSourcePathValid
            ? (string.IsNullOrEmpty(scanError)
                ? $"<color=#4CAF50>● 源码目录有效</color>  |  预计提取 <color=#2196F3><b>{matchedFileCount}</b></color> 个文件"
                : "<color=#F44336>● 扫描失败</color>")
            : "<color=#F44336>● 源码目录不存在或路径无效</color>";

        EditorGUILayout.LabelField(statusText, styles.StatusBadge);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);

        EditorGUILayout.HelpBox(
            "提示：多个扩展名或目录名请使用分号或逗号分隔。每次提取输出文件均附带时间戳，不会覆盖以前的提取结果。",
            MessageType.Info);

        if (!string.IsNullOrEmpty(scanError))
        {
            EditorGUILayout.HelpBox(scanError, MessageType.Error);
        }
        else if (isSourcePathValid && matchedFiles.Count == 0)
        {
            EditorGUILayout.HelpBox("当前规则下没有找到可提取的文件。", MessageType.Warning);
        }
        else if (matchedFiles.Count > 0)
        {
            DrawFilePreviewList();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawFilePreviewList()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("文件列表", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"共 {matchedFiles.Count} 个", EditorStyles.miniLabel, GUILayout.Width(64f));
        EditorGUILayout.EndHorizontal();

        float rowHeight = EditorGUIUtility.singleLineHeight + 2f;
        float previewHeight = Mathf.Clamp(matchedFiles.Count * rowHeight + 4f, 80f, 220f);
        previewScrollPosition = EditorGUILayout.BeginScrollView(
            previewScrollPosition,
            GUILayout.Height(previewHeight));

        for (int i = 0; i < matchedFiles.Count; i++)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
            if ((i & 1) == 1)
            {
                Color rowColor = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f)
                    : new Color(0f, 0f, 0f, 0.025f);
                EditorGUI.DrawRect(rowRect, rowColor);
            }

            Rect indexRect = new Rect(rowRect.x + 4f, rowRect.y, 36f, rowRect.height);
            Rect pathRect = new Rect(rowRect.x + 40f, rowRect.y, rowRect.width - 44f, rowRect.height);
            EditorGUI.LabelField(indexRect, $"{i + 1}.", EditorStyles.miniLabel);
            EditorGUI.LabelField(
                pathRect,
                new GUIContent(matchedFiles[i].RelativePath, matchedFiles[i].FullPath),
                EditorStyles.label);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawFooter()
    {
        EditorGUILayout.BeginVertical(styles.CardBackground);

        using (new EditorGUI.DisabledScope(
            !isSourcePathValid || !string.IsNullOrEmpty(scanError) || matchedFileCount == 0))
        {
            string buttonText = !isSourcePathValid
                ? "❌  源码目录无效"
                : (!string.IsNullOrEmpty(scanError)
                    ? "❌  扫描失败"
                    : (matchedFileCount > 0
                        ? $"🚀  开始提取  ( 共 {matchedFileCount} 个文件 )"
                        : "⚠️  未发现匹配文件"));

            if (GUILayout.Button(buttonText, styles.PrimaryButton))
            {
                ExtractSourceCode();
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4f);
    }

    private void RefreshScanPreview()
    {
        matchedFiles.Clear();
        matchedFileCount = 0;
        scanError = null;
        isSourcePathValid = !string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory);
        if (!isSourcePathValid)
        {
            return;
        }

        try
        {
            SourceCodeExtractOptions options = CreateOptions();
            matchedFiles.AddRange(SourceCodeExtractor.Scan(options));

            matchedFileCount = matchedFiles.Count;
        }
        catch (Exception exception)
        {
            scanError = exception.Message;
        }
    }

    private void TryUseProjectSelection()
    {
        string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("源码提取", "请先在 Project 窗口中选择一个目录或文件。", "确定");
            return;
        }

        string projectDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string absolutePath = Path.GetFullPath(Path.Combine(projectDirectory, assetPath));
        sourceDirectory = Directory.Exists(absolutePath)
            ? absolutePath
            : Path.GetDirectoryName(absolutePath);
        SavePreferences();
        RefreshScanPreview();
        Repaint();
    }

    private void ExtractSourceCode()
    {
        SourceCodeExtractOptions options = CreateOptions();

        try
        {
            EditorUtility.DisplayProgressBar("源码提取", "正在扫描并导出源码...", 0.5f);
            SourceCodeExtractResult result = SourceCodeExtractor.Extract(options);
            EditorUtility.RevealInFinder(result.OutputPath);
            EditorUtility.DisplayDialog(
                "源码提取完成",
                $"共成功提取 {result.FileCount} 个文件。\n\n输出位置：\n{result.OutputPath}",
                "确定");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("源码提取失败", exception.Message, "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            RefreshScanPreview();
        }
    }

    private SourceCodeExtractOptions CreateOptions()
    {
        return new SourceCodeExtractOptions
        {
            SourceDirectory = sourceDirectory,
            OutputDirectory = outputDirectory,
            Extensions = extensions,
            ExcludedDirectoryNames = excludedDirectories,
            IncludeMetaFiles = includeMetaFiles,
            AdditionalSourceRoots = includePackages ? GetPackageSourceRoots() : null,
            Mode = selectedMode == 0
                ? SourceCodeExtractMode.MergeToText
                : SourceCodeExtractMode.CopyDirectory
        };
    }

    private static List<SourceCodeScanRoot> GetPackageSourceRoots()
    {
        var roots = new List<SourceCodeScanRoot>();
        PackageManagerPackageInfo[] packages = PackageManagerPackageInfo.GetAllRegisteredPackages();
        if (packages == null)
        {
            return roots;
        }

        for (int i = 0; i < packages.Length; i++)
        {
            PackageManagerPackageInfo package = packages[i];
            if (package == null
                || string.IsNullOrWhiteSpace(package.name)
                || string.IsNullOrWhiteSpace(package.resolvedPath)
                || !Directory.Exists(package.resolvedPath))
            {
                continue;
            }

            roots.Add(new SourceCodeScanRoot
            {
                SourceDirectory = package.resolvedPath,
                ExportPathPrefix = Path.Combine("Packages", package.name)
            });
        }

        roots.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
            left.ExportPathPrefix,
            right.ExportPathPrefix));
        return roots;
    }

    private void SavePreferences()
    {
        EditorPrefs.SetString(PreferencesPrefix + "SourceDirectory", sourceDirectory ?? string.Empty);
        EditorPrefs.SetString(PreferencesPrefix + "OutputDirectory", outputDirectory ?? string.Empty);
        EditorPrefs.SetString(PreferencesPrefix + "Extensions", extensions ?? string.Empty);
        EditorPrefs.SetString(PreferencesPrefix + "ExcludedDirectories", excludedDirectories ?? string.Empty);
        EditorPrefs.SetBool(PreferencesPrefix + "IncludeMetaFiles", includeMetaFiles);
        EditorPrefs.SetBool(PreferencesPrefix + "IncludePackages", includePackages);
        EditorPrefs.SetInt(PreferencesPrefix + "Mode", selectedMode);
    }

    private static string GetStringPreference(string key, string defaultValue)
    {
        return EditorPrefs.GetString(ResolvePreferenceKey(key), defaultValue);
    }

    private static bool GetBoolPreference(string key, bool defaultValue)
    {
        return EditorPrefs.GetBool(ResolvePreferenceKey(key), defaultValue);
    }

    private static int GetIntPreference(string key, int defaultValue)
    {
        return EditorPrefs.GetInt(ResolvePreferenceKey(key), defaultValue);
    }

    /// <summary>返回当前键；当前键不存在时返回第一个存在的旧键，都不存在时仍返回当前键。</summary>
    private static string ResolvePreferenceKey(string key)
    {
        string preferenceKey = PreferencesPrefix + key;
        if (EditorPrefs.HasKey(preferenceKey)) return preferenceKey;
        foreach (string prefix in LegacyPreferencesPrefixes)
        {
            if (EditorPrefs.HasKey(prefix + key)) return prefix + key;
        }
        return preferenceKey;
    }
}
