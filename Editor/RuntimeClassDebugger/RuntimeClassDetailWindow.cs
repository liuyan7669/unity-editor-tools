using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

internal sealed class RuntimeClassDetailWindow : EditorWindow
{
    private readonly RuntimeValueDrawer valueDrawer = new RuntimeValueDrawer();
    private readonly Dictionary<string, PropertySnapshot> propertySnapshots = new Dictionary<string, PropertySnapshot>();
    private readonly string[] tabs = { "字段", "属性", "方法" };
    private readonly string[] visibilityNames = { "不显示", "全部", "仅 Public", "仅 Private" };

    [NonSerialized] private Type inspectedType;
    [NonSerialized] private object inspectedTarget;
    private Vector2 scrollPosition;
    private string searchText = string.Empty;
    private int selectedTab;
    private int maxDepth = 8;
    private bool automaticPropertyRead;
    private MemberVisibility memberVisibility = MemberVisibility.All;
    private double nextRepaintTime;

    private enum MemberVisibility
    {
        None,
        All,
        PublicOnly,
        PrivateOnly
    }

    public static void Open(Type type, object target)
    {
        RuntimeClassDetailWindow window = CreateInstance<RuntimeClassDetailWindow>();
        window.inspectedType = type;
        window.inspectedTarget = target;
        window.titleContent = new GUIContent(type.Name + " 状态");
        window.minSize = new Vector2(820f, 480f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnGUI()
    {
        if (inspectedType == null)
        {
            EditorGUILayout.HelpBox("脚本重新编译后详情引用已失效，请从主窗口重新打开。", MessageType.Info);
            return;
        }

        if (!IsTargetAlive())
        {
            EditorGUILayout.HelpBox("该运行实例已经销毁。", MessageType.Warning);
            return;
        }

        DrawHeader();
        DrawToolbar();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        if (selectedTab == 0)
        {
            DrawFields();
        }
        else if (selectedTab == 1)
        {
            DrawProperties();
        }
        else
        {
            DrawMethods();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(inspectedType.FullName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("程序集：" + inspectedType.Assembly.GetName().Name, EditorStyles.miniLabel);

        UnityEngine.Object unityObject = inspectedTarget as UnityEngine.Object;
        if (unityObject != null)
        {
            EditorGUILayout.ObjectField("当前实例", unityObject, unityObject.GetType(), true);
        }
        else
        {
            EditorGUILayout.LabelField("当前目标", "未绑定实例（实例成员可查看定义，但不能读取或调用）");
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        selectedTab = GUILayout.Toolbar(selectedTab, tabs, EditorStyles.toolbarButton, GUILayout.Width(190f));
        GUILayout.Space(8f);
        GUILayout.Label("显示", GUILayout.Width(28f));
        memberVisibility = (MemberVisibility)EditorGUILayout.Popup(
            (int)memberVisibility,
            visibilityNames,
            EditorStyles.toolbarPopup,
            GUILayout.Width(82f));

        if (selectedTab != 2)
        {
            GUILayout.Space(8f);
            GUILayout.Label("递归深度", GUILayout.Width(58f));
            maxDepth = EditorGUILayout.IntSlider(maxDepth, 1, 16, GUILayout.Width(180f));
        }

        if (selectedTab == 1)
        {
            automaticPropertyRead = GUILayout.Toggle(
                automaticPropertyRead,
                new GUIContent("自动读取属性", "Getter 可能包含逻辑、产生副作用或抛出异常。"),
                EditorStyles.toolbarButton,
                GUILayout.Width(95f));
        }

        GUILayout.FlexibleSpace();
        GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField;
        searchText = EditorGUILayout.TextField(searchText, searchStyle, GUILayout.MinWidth(160f), GUILayout.MaxWidth(280f));
        EditorGUILayout.EndHorizontal();

        if (selectedTab == 1 && automaticPropertyRead)
        {
            EditorGUILayout.HelpBox("已启用自动读取：属性 Getter 将随窗口刷新执行。", MessageType.Warning);
        }
    }

    private void DrawFields()
    {
        List<FieldInfo> fields = RuntimeReflectionUtility.GetAllFields(inspectedType)
            .Where(field => ShouldShowMember(field.Name) && MatchesVisibility(field))
            .ToList();

        EditorGUILayout.LabelField("字段 " + fields.Count, EditorStyles.boldLabel);
        for (int i = 0; i < fields.Count; i++)
        {
            FieldInfo field = fields[i];
            string label = BuildFieldLabel(field);
            if (!field.IsStatic && inspectedTarget == null)
            {
                DrawInstanceRequiredRow(label);
                continue;
            }

            try
            {
                object value = field.GetValue(field.IsStatic ? null : inspectedTarget);
                valueDrawer.DrawValue(label, field.FieldType, value, "Field." + RuntimeReflectionUtility.GetMemberKey(field), maxDepth);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(label + "：" + exception.Message, MessageType.Warning);
            }
        }
    }

    private void DrawProperties()
    {
        List<PropertyInfo> properties = RuntimeReflectionUtility.GetAllProperties(inspectedType)
            .Where(property => ShouldShowMember(property.Name) && MatchesVisibility(property))
            .ToList();

        EditorGUILayout.LabelField("属性 " + properties.Count, EditorStyles.boldLabel);
        for (int i = 0; i < properties.Count; i++)
        {
            PropertyInfo property = properties[i];
            DrawProperty(property);
        }
    }

    private void DrawProperty(PropertyInfo property)
    {
        MethodInfo getter = property.GetGetMethod(true);
        MethodInfo setter = property.GetSetMethod(true);
        string key = RuntimeReflectionUtility.GetMemberKey(property);
        string accessors = (getter != null ? "get; " : string.Empty) + (setter != null ? "set;" : string.Empty);
        string label = RuntimeReflectionUtility.GetPropertyAccessText(property)
                       + (RuntimeReflectionUtility.IsStatic(property) ? " static " : " ")
                       + RuntimeReflectionUtility.GetTypeDisplayName(property.PropertyType)
                       + " " + property.Name + " { " + accessors + " }";
        bool instanceRequired = !RuntimeReflectionUtility.IsStatic(property) && inspectedTarget == null;

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField(label);
        if (instanceRequired)
        {
            GUILayout.Label("需要实例", EditorStyles.miniLabel, GUILayout.Width(52f));
        }

        using (new EditorGUI.DisabledScope(
                   getter == null || property.GetIndexParameters().Length > 0 || instanceRequired))
        {
            if (GUILayout.Button("读取 Getter", GUILayout.Width(88f)))
            {
                propertySnapshots[key] = ReadProperty(property);
            }
        }

        EditorGUILayout.EndHorizontal();

        if (automaticPropertyRead
            && getter != null
            && property.GetIndexParameters().Length == 0
            && !instanceRequired)
        {
            propertySnapshots[key] = ReadProperty(property);
        }

        if (property.GetIndexParameters().Length > 0)
        {
            EditorGUILayout.LabelField("索引器请在“方法”页调用对应的 get_/set_ 方法。", EditorStyles.miniLabel);
            return;
        }

        if (!propertySnapshots.TryGetValue(key, out PropertySnapshot snapshot))
        {
            return;
        }

        if (snapshot.Exception != null)
        {
            EditorGUILayout.HelpBox(snapshot.Exception.GetType().Name + "：" + snapshot.Exception.Message, MessageType.Warning);
        }
        else
        {
            valueDrawer.DrawValue("当前值", property.PropertyType, snapshot.Value, "Property." + key, maxDepth);
        }
    }

    private void DrawMethods()
    {
        List<MethodInfo> methods = RuntimeReflectionUtility.GetAllMethods(inspectedType)
            .Where(method => ShouldShowMember(method.Name) && MatchesVisibility(method))
            .ToList();

        EditorGUILayout.LabelField("方法 " + methods.Count, EditorStyles.boldLabel);
        for (int i = 0; i < methods.Count; i++)
        {
            MethodInfo method = methods[i];
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            string prefix = RuntimeReflectionUtility.GetMemberAccessText(method)
                            + (method.IsStatic ? " static" : string.Empty)
                            + (method.IsSpecialName ? " 特殊成员" : string.Empty);
            EditorGUILayout.LabelField(prefix + "  " + RuntimeReflectionUtility.GetMethodSignature(method));

            bool instanceRequired = !method.IsStatic && inspectedTarget == null;
            if (instanceRequired)
            {
                GUILayout.Label("需要实例", EditorStyles.miniLabel, GUILayout.Width(52f));
            }

            bool unsupported = method.IsAbstract || method.ContainsGenericParameters || instanceRequired;
            using (new EditorGUI.DisabledScope(unsupported))
            {
                if (GUILayout.Button("调用", GUILayout.Width(58f)))
                {
                    RuntimeMethodInvokerWindow.Open(inspectedTarget, method);
                }
            }

            EditorGUILayout.EndHorizontal();
            if (method.IsAbstract || method.ContainsGenericParameters)
            {
                EditorGUILayout.LabelField("抽象方法或开放泛型方法不能直接调用。", EditorStyles.miniLabel);
            }
        }
    }

    private PropertySnapshot ReadProperty(PropertyInfo property)
    {
        try
        {
            object target = RuntimeReflectionUtility.IsStatic(property) ? null : inspectedTarget;
            return new PropertySnapshot { Value = property.GetValue(target, null) };
        }
        catch (TargetInvocationException exception)
        {
            return new PropertySnapshot { Exception = exception.InnerException ?? exception };
        }
        catch (Exception exception)
        {
            return new PropertySnapshot { Exception = exception };
        }
    }

    private bool MatchesVisibility(FieldInfo field)
    {
        return memberVisibility == MemberVisibility.All
               || memberVisibility == MemberVisibility.PublicOnly && field.IsPublic
               || memberVisibility == MemberVisibility.PrivateOnly && field.IsPrivate;
    }

    private bool MatchesVisibility(PropertyInfo property)
    {
        MethodInfo[] accessors = property.GetAccessors(true);
        return memberVisibility == MemberVisibility.All
               || memberVisibility == MemberVisibility.PublicOnly && accessors.Any(accessor => accessor.IsPublic)
               || memberVisibility == MemberVisibility.PrivateOnly && accessors.Any(accessor => accessor.IsPrivate);
    }

    private bool MatchesVisibility(MethodInfo method)
    {
        return memberVisibility == MemberVisibility.All
               || memberVisibility == MemberVisibility.PublicOnly && method.IsPublic
               || memberVisibility == MemberVisibility.PrivateOnly && method.IsPrivate;
    }

    private bool ShouldShowMember(string memberName)
    {
        return string.IsNullOrWhiteSpace(searchText)
               || memberName.IndexOf(searchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsTargetAlive()
    {
        if (inspectedTarget == null)
        {
            return true;
        }

        UnityEngine.Object unityObject = inspectedTarget as UnityEngine.Object;
        return !(inspectedTarget is UnityEngine.Object) || unityObject != null;
    }

    private static string BuildFieldLabel(FieldInfo field)
    {
        return RuntimeReflectionUtility.GetMemberAccessText(field)
               + (field.IsStatic ? " static" : string.Empty)
               + (field.IsInitOnly ? " readonly" : string.Empty)
               + " " + RuntimeReflectionUtility.GetTypeDisplayName(field.FieldType)
               + " " + field.Name;
    }

    private static void DrawInstanceRequiredRow(string label)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField(label);
        GUILayout.Label("需要实例", EditorStyles.miniLabel, GUILayout.Width(52f));
        EditorGUILayout.EndHorizontal();
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

    private sealed class PropertySnapshot
    {
        public object Value;
        public Exception Exception;
    }
}
