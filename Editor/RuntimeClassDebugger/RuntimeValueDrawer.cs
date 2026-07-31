using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

internal sealed class RuntimeValueDrawer
{
    private const int MaxCollectionItems = 200;

    private readonly Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();
    private readonly Dictionary<string, ValueSnapshot> propertySnapshots = new Dictionary<string, ValueSnapshot>();

    public void DrawValue(string label, Type declaredType, object value, string key, int maxDepth)
    {
        DrawValueInternal(label, declaredType, value, key, 0, maxDepth, new HashSet<object>(ReferenceComparer.Instance));
    }

    private void DrawValueInternal(
        string label,
        Type declaredType,
        object value,
        string key,
        int depth,
        int maxDepth,
        HashSet<object> referencePath)
    {
        Type actualType = value != null ? value.GetType() : declaredType;
        if (value == null)
        {
            DrawTextValue(label, "null", declaredType);
            return;
        }

        UnityEngine.Object unityObject = value as UnityEngine.Object;
        if (unityObject != null || value is UnityEngine.Object)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.ObjectField(unityObject, actualType, true);
            EditorGUILayout.EndHorizontal();
            return;
        }

        if (DrawSimpleValue(label, actualType, value))
        {
            return;
        }

        string summary = GetSummary(value, actualType);
        bool expanded = GetFoldout(key);
        expanded = EditorGUILayout.Foldout(expanded, label + "  " + summary, true);
        foldoutStates[key] = expanded;
        if (!expanded)
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            if (depth >= maxDepth)
            {
                EditorGUILayout.LabelField("已达到递归深度上限 " + maxDepth, EditorStyles.miniLabel);
                return;
            }

            bool tracksReference = !actualType.IsValueType;
            if (tracksReference && !referencePath.Add(value))
            {
                EditorGUILayout.LabelField("检测到循环引用", EditorStyles.miniLabel);
                return;
            }

            try
            {
                IDictionary dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    DrawDictionary(dictionary, key, depth, maxDepth, referencePath);
                }
                else if (value is IEnumerable enumerable && !(value is string))
                {
                    DrawEnumerable(enumerable, key, depth, maxDepth, referencePath);
                }
                else
                {
                    DrawObjectMembers(value, actualType, key, depth, maxDepth, referencePath);
                }
            }
            finally
            {
                if (tracksReference)
                {
                    referencePath.Remove(value);
                }
            }
        }
    }

    private static bool DrawSimpleValue(string label, Type type, object value)
    {
        using (new EditorGUI.DisabledScope(true))
        {
            if (type == typeof(bool))
            {
                EditorGUILayout.Toggle(label, (bool)value);
                return true;
            }

            if (type == typeof(string))
            {
                string text = (string)value;
                if (text != null && (text.Contains("\n") || text.Length > 120))
                {
                    EditorGUILayout.PrefixLabel(label);
                    EditorGUILayout.TextArea(text, GUILayout.MinHeight(36f));
                }
                else
                {
                    EditorGUILayout.TextField(label, text ?? "null");
                }

                return true;
            }

            if (type == typeof(int))
            {
                EditorGUILayout.IntField(label, (int)value);
                return true;
            }

            if (type == typeof(long))
            {
                EditorGUILayout.LongField(label, (long)value);
                return true;
            }

            if (type == typeof(float))
            {
                EditorGUILayout.FloatField(label, (float)value);
                return true;
            }

            if (type == typeof(double))
            {
                EditorGUILayout.DoubleField(label, (double)value);
                return true;
            }

            if (type == typeof(Vector2))
            {
                EditorGUILayout.Vector2Field(label, (Vector2)value);
                return true;
            }

            if (type == typeof(Vector3))
            {
                EditorGUILayout.Vector3Field(label, (Vector3)value);
                return true;
            }

            if (type == typeof(Vector4))
            {
                EditorGUILayout.Vector4Field(label, (Vector4)value);
                return true;
            }

            if (type == typeof(Vector2Int))
            {
                EditorGUILayout.Vector2IntField(label, (Vector2Int)value);
                return true;
            }

            if (type == typeof(Vector3Int))
            {
                EditorGUILayout.Vector3IntField(label, (Vector3Int)value);
                return true;
            }

            if (type == typeof(Color))
            {
                EditorGUILayout.ColorField(label, (Color)value);
                return true;
            }

            if (type == typeof(Rect))
            {
                EditorGUILayout.RectField(label, (Rect)value);
                return true;
            }

            if (type == typeof(Bounds))
            {
                EditorGUILayout.BoundsField(label, (Bounds)value);
                return true;
            }

            if (type.IsEnum)
            {
                EditorGUILayout.EnumPopup(label, (Enum)value);
                return true;
            }
        }

        if (type.IsPrimitive
            || type == typeof(decimal)
            || type == typeof(char)
            || type == typeof(DateTime)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || type == typeof(Type))
        {
            DrawTextValue(label, value.ToString(), type);
            return true;
        }

        return false;
    }

    private void DrawDictionary(
        IDictionary dictionary,
        string key,
        int depth,
        int maxDepth,
        HashSet<object> referencePath)
    {
        int index = 0;
        try
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (index >= MaxCollectionItems)
                {
                    EditorGUILayout.LabelField("仅显示前 " + MaxCollectionItems + " 项", EditorStyles.miniLabel);
                    break;
                }

                string itemKey = key + "[" + index + "]";
                DrawValueInternal("键", entry.Key != null ? entry.Key.GetType() : typeof(object), entry.Key, itemKey + ".Key", depth + 1, maxDepth, referencePath);
                DrawValueInternal("值", entry.Value != null ? entry.Value.GetType() : typeof(object), entry.Value, itemKey + ".Value", depth + 1, maxDepth, referencePath);
                index++;
            }
        }
        catch (Exception exception)
        {
            EditorGUILayout.HelpBox("枚举字典失败：" + exception.Message, MessageType.Warning);
        }
    }

    private void DrawEnumerable(
        IEnumerable enumerable,
        string key,
        int depth,
        int maxDepth,
        HashSet<object> referencePath)
    {
        int index = 0;
        try
        {
            foreach (object item in enumerable)
            {
                if (index >= MaxCollectionItems)
                {
                    EditorGUILayout.LabelField("仅显示前 " + MaxCollectionItems + " 项", EditorStyles.miniLabel);
                    break;
                }

                Type itemType = item != null ? item.GetType() : typeof(object);
                DrawValueInternal("[" + index + "]", itemType, item, key + "[" + index + "]", depth + 1, maxDepth, referencePath);
                index++;
            }
        }
        catch (Exception exception)
        {
            EditorGUILayout.HelpBox("枚举集合失败：" + exception.Message, MessageType.Warning);
        }
    }

    private void DrawObjectMembers(
        object value,
        Type type,
        string key,
        int depth,
        int maxDepth,
        HashSet<object> referencePath)
    {
        List<FieldInfo> fields = RuntimeReflectionUtility.GetAllFields(type);
        for (int i = 0; i < fields.Count; i++)
        {
            FieldInfo field = fields[i];
            if (field.IsStatic)
            {
                continue;
            }

            string childKey = key + ".Field." + RuntimeReflectionUtility.GetMemberKey(field);
            try
            {
                object fieldValue = field.GetValue(value);
                string fieldLabel = RuntimeReflectionUtility.GetMemberAccessText(field)
                                    + " " + RuntimeReflectionUtility.GetTypeDisplayName(field.FieldType)
                                    + " " + field.Name;
                DrawValueInternal(fieldLabel, field.FieldType, fieldValue, childKey, depth + 1, maxDepth, referencePath);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(field.Name + "：" + exception.Message, MessageType.Warning);
            }
        }

        List<PropertyInfo> properties = RuntimeReflectionUtility.GetAllProperties(type);
        for (int i = 0; i < properties.Count; i++)
        {
            PropertyInfo property = properties[i];
            MethodInfo getter = property.GetGetMethod(true);
            if (getter == null || getter.IsStatic || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            string propertyKey = key + ".Property." + RuntimeReflectionUtility.GetMemberKey(property);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("属性 " + RuntimeReflectionUtility.GetTypeDisplayName(property.PropertyType) + " " + property.Name);
            if (GUILayout.Button("读取 Getter", GUILayout.Width(88f)))
            {
                propertySnapshots[propertyKey] = ReadProperty(property, value);
            }

            EditorGUILayout.EndHorizontal();

            if (propertySnapshots.TryGetValue(propertyKey, out ValueSnapshot snapshot))
            {
                if (snapshot.Exception != null)
                {
                    EditorGUILayout.HelpBox(snapshot.Exception.Message, MessageType.Warning);
                }
                else
                {
                    DrawValueInternal("值", property.PropertyType, snapshot.Value, propertyKey + ".Value", depth + 1, maxDepth, referencePath);
                }
            }
        }
    }

    private bool GetFoldout(string key)
    {
        return foldoutStates.TryGetValue(key, out bool expanded) && expanded;
    }

    private static string GetSummary(object value, Type type)
    {
        if (value is ICollection collection)
        {
            return "(" + RuntimeReflectionUtility.GetTypeDisplayName(type) + ", Count = " + collection.Count + ")";
        }

        return "(" + RuntimeReflectionUtility.GetTypeDisplayName(type) + ")";
    }

    private static void DrawTextValue(string label, string value, Type type)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        EditorGUILayout.SelectableLabel(
            value ?? "null",
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        GUILayout.Label(RuntimeReflectionUtility.GetTypeDisplayName(type), EditorStyles.miniLabel, GUILayout.Width(90f));
        EditorGUILayout.EndHorizontal();
    }

    private static ValueSnapshot ReadProperty(PropertyInfo property, object target)
    {
        try
        {
            return new ValueSnapshot { Value = property.GetValue(target, null) };
        }
        catch (TargetInvocationException exception)
        {
            return new ValueSnapshot { Exception = exception.InnerException ?? exception };
        }
        catch (Exception exception)
        {
            return new ValueSnapshot { Exception = exception };
        }
    }

    private sealed class ValueSnapshot
    {
        public object Value;
        public Exception Exception;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
