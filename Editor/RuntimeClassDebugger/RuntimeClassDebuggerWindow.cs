using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在 Unity 运行时浏览项目脚本实例与静态状态。
/// </summary>
public sealed class RuntimeClassDebuggerWindow : EditorWindow
{
    private readonly List<UnityEngine.Object> runtimeInstances = new List<UnityEngine.Object>();
    private readonly List<Type> projectTypes = new List<Type>();
    private readonly string[] tabs = { "运行实例", "项目类型 / 静态状态" };

    private Vector2 scrollPosition;
    private string searchText = string.Empty;
    private int selectedTab;
    private double nextRepaintTime;

    [MenuItem("Cowart/调试/运行时对象调试")]
    private static void OpenWindow()
    {
        RuntimeClassDebuggerWindow window = GetWindow<RuntimeClassDebuggerWindow>();
        window.titleContent = new GUIContent("运行时对象调试");
        window.minSize = new Vector2(760f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
        RefreshAll();
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "请先进入 Play 模式。项目类型仍可浏览，但当前显示的是编辑器域中的静态状态，不代表游戏运行状态。",
                MessageType.Info);
        }

        EditorGUILayout.HelpBox(
            "“运行实例”列出项目中的 MonoBehaviour 与已加载 ScriptableObject。普通 C# 对象无法从托管堆直接枚举，"
            + "但可以从静态字段、实例字段、属性值和方法返回值继续递归展开。",
            MessageType.None);

        if (selectedTab == 0)
        {
            DrawRuntimeInstances();
        }
        else
        {
            DrawProjectTypes();
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24f));
        int newTab = GUILayout.Toolbar(selectedTab, tabs, EditorStyles.toolbarButton, GUILayout.Width(270f));
        if (newTab != selectedTab)
        {
            selectedTab = newTab;
            scrollPosition = Vector2.zero;
        }

        GUILayout.FlexibleSpace();
        GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField")
                               ?? EditorStyles.toolbarTextField;
        searchText = EditorGUILayout.TextField(
            searchText,
            searchStyle,
            GUILayout.MinWidth(180f),
            GUILayout.MaxWidth(320f));

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50f)))
        {
            RefreshAll();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRuntimeInstances()
    {
        List<UnityEngine.Object> visibleInstances = runtimeInstances
            .Where(instance => instance != null && MatchesSearch(instance.GetType(), GetObjectDisplayName(instance)))
            .OrderBy(instance => instance.GetType().FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(GetObjectDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        EditorGUILayout.LabelField(
            "已发现 " + runtimeInstances.Count + " 个项目脚本实例，当前显示 " + visibleInstances.Count + " 个。",
            EditorStyles.miniLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        Type currentType = null;
        for (int i = 0; i < visibleInstances.Count; i++)
        {
            UnityEngine.Object instance = visibleInstances[i];
            Type instanceType = instance.GetType();
            if (instanceType != currentType)
            {
                currentType = instanceType;
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(
                    instanceType.FullName + "  (" + visibleInstances.Count(item => item != null && item.GetType() == instanceType) + ")",
                    EditorStyles.boldLabel);
            }

            DrawInstanceRow(instance);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawInstanceRow(UnityEngine.Object instance)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.ObjectField(instance, typeof(UnityEngine.Object), true);
        GUILayout.Label(GetObjectLocation(instance), EditorStyles.miniLabel, GUILayout.MaxWidth(320f));
        if (GUILayout.Button("打开", GUILayout.Width(58f)))
        {
            RuntimeClassDetailWindow.Open(instance.GetType(), instance);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawProjectTypes()
    {
        List<Type> visibleTypes = projectTypes
            .Where(type => MatchesSearch(type, string.Empty))
            .ToList();

        EditorGUILayout.LabelField(
            "项目 Player 程序集中共有 " + projectTypes.Count + " 个类，当前显示 " + visibleTypes.Count + " 个。打开后可查看该类型的静态成员。",
            EditorStyles.miniLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        for (int i = 0; i < visibleTypes.Count; i++)
        {
            Type type = visibleTypes[i];
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(type.IsAbstract && type.IsSealed ? "静态类" : "类型", EditorStyles.miniLabel, GUILayout.Width(48f));
            EditorGUILayout.LabelField(type.FullName);
            GUILayout.Label(type.Assembly.GetName().Name, EditorStyles.miniLabel, GUILayout.Width(150f));
            if (GUILayout.Button("打开", GUILayout.Width(58f)))
            {
                RuntimeClassDetailWindow.Open(type, null);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void RefreshAll()
    {
        projectTypes.Clear();
        projectTypes.AddRange(RuntimeReflectionUtility.GetProjectRuntimeTypes());

        HashSet<Type> projectTypeSet = new HashSet<Type>(projectTypes);
        runtimeInstances.Clear();

        MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null
                && projectTypeSet.Contains(behaviour.GetType())
                && !EditorUtility.IsPersistent(behaviour))
            {
                runtimeInstances.Add(behaviour);
            }
        }

        ScriptableObject[] scriptableObjects = Resources.FindObjectsOfTypeAll<ScriptableObject>();
        for (int i = 0; i < scriptableObjects.Length; i++)
        {
            ScriptableObject scriptableObject = scriptableObjects[i];
            if (scriptableObject != null && projectTypeSet.Contains(scriptableObject.GetType()))
            {
                runtimeInstances.Add(scriptableObject);
            }
        }

        Repaint();
    }

    private bool MatchesSearch(Type type, string additionalText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        string keyword = searchText.Trim();
        return (type.FullName ?? type.Name).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
               || additionalText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetObjectDisplayName(UnityEngine.Object instance)
    {
        Component component = instance as Component;
        return component != null ? component.gameObject.name : instance.name;
    }

    private static string GetObjectLocation(UnityEngine.Object instance)
    {
        Component component = instance as Component;
        if (component == null)
        {
            string assetPath = AssetDatabase.GetAssetPath(instance);
            return string.IsNullOrEmpty(assetPath) ? "运行时对象" : assetPath;
        }

        List<string> names = new List<string>();
        Transform current = component.transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return component.gameObject.scene.name + ": " + string.Join("/", names);
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        EditorApplication.delayCall += RefreshAll;
    }

    private void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextRepaintTime)
        {
            return;
        }

        nextRepaintTime = EditorApplication.timeSinceStartup + 0.25d;
        Repaint();
    }
}
