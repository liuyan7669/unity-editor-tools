using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

internal sealed class RuntimeMethodInvokerWindow : EditorWindow
{
    private readonly RuntimeValueDrawer valueDrawer = new RuntimeValueDrawer();

    [NonSerialized] private object target;
    [NonSerialized] private MethodInfo method;
    [NonSerialized] private ParameterState[] parameterStates;
    [NonSerialized] private object returnValue;
    [NonSerialized] private object[] lastArguments;
    [NonSerialized] private Exception invocationException;
    private Vector2 scrollPosition;
    private string statusText;
    private double elapsedMilliseconds;

    public static void Open(object target, MethodInfo method)
    {
        RuntimeMethodInvokerWindow window = CreateInstance<RuntimeMethodInvokerWindow>();
        window.target = target;
        window.method = method;
        window.InitializeParameters();
        window.titleContent = new GUIContent("调用 " + method.Name);
        window.minSize = new Vector2(660f, 420f);
        window.Show();
    }

    private void OnGUI()
    {
        if (method == null)
        {
            EditorGUILayout.HelpBox("脚本重新编译后方法引用已失效，请从详情窗口重新打开。", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(RuntimeReflectionUtility.GetMethodSignature(method), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            RuntimeReflectionUtility.GetMemberAccessText(method)
            + (method.IsStatic ? " static" : " instance")
            + " · " + method.DeclaringType.FullName,
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.HelpBox("调用会直接执行当前方法并可能修改游戏状态。参数 JSON 仅在点击调用时解析。", MessageType.Warning);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawParameters();
        EditorGUILayout.Space(8f);
        DrawInvokeButton();
        DrawResult();
        EditorGUILayout.EndScrollView();
    }

    private void DrawParameters()
    {
        ParameterInfo[] parameters = method.GetParameters();
        EditorGUILayout.LabelField("参数 " + parameters.Length, EditorStyles.boldLabel);
        if (parameters.Length == 0)
        {
            EditorGUILayout.LabelField("此方法没有参数。", EditorStyles.miniLabel);
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            ParameterState state = parameterStates[i];
            Type valueType = GetParameterValueType(parameter);
            string prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
            string label = prefix + RuntimeReflectionUtility.GetTypeDisplayName(valueType) + " " + parameter.Name;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (parameter.IsOut)
            {
                EditorGUILayout.LabelField(label, "由方法输出");
            }
            else
            {
                DrawParameterInput(label, valueType, state);
            }

            if (parameter.IsOptional)
            {
                EditorGUILayout.LabelField("可选参数，默认值：" + FormatDefaultValue(parameter.DefaultValue), EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }
    }

    private static void DrawParameterInput(string label, Type type, ParameterState state)
    {
        Type nullableType = Nullable.GetUnderlyingType(type);
        bool canBeNull = !type.IsValueType || nullableType != null;
        Type effectiveType = nullableType ?? type;

        if (canBeNull)
        {
            state.IsNull = EditorGUILayout.ToggleLeft("传入 null", state.IsNull);
            if (state.IsNull)
            {
                return;
            }
        }

        if (effectiveType == typeof(string))
        {
            state.Value = EditorGUILayout.TextField(label, (string)state.Value ?? string.Empty);
        }
        else if (effectiveType == typeof(bool))
        {
            state.Value = EditorGUILayout.Toggle(label, state.Value != null && (bool)state.Value);
        }
        else if (effectiveType == typeof(int))
        {
            state.Value = EditorGUILayout.IntField(label, state.Value != null ? (int)state.Value : 0);
        }
        else if (effectiveType == typeof(long))
        {
            state.Value = EditorGUILayout.LongField(label, state.Value != null ? (long)state.Value : 0L);
        }
        else if (effectiveType == typeof(float))
        {
            state.Value = EditorGUILayout.FloatField(label, state.Value != null ? (float)state.Value : 0f);
        }
        else if (effectiveType == typeof(double))
        {
            state.Value = EditorGUILayout.DoubleField(label, state.Value != null ? (double)state.Value : 0d);
        }
        else if (effectiveType == typeof(Vector2))
        {
            state.Value = EditorGUILayout.Vector2Field(label, state.Value != null ? (Vector2)state.Value : Vector2.zero);
        }
        else if (effectiveType == typeof(Vector3))
        {
            state.Value = EditorGUILayout.Vector3Field(label, state.Value != null ? (Vector3)state.Value : Vector3.zero);
        }
        else if (effectiveType == typeof(Vector4))
        {
            state.Value = EditorGUILayout.Vector4Field(label, state.Value != null ? (Vector4)state.Value : Vector4.zero);
        }
        else if (effectiveType == typeof(Vector2Int))
        {
            state.Value = EditorGUILayout.Vector2IntField(label, state.Value != null ? (Vector2Int)state.Value : Vector2Int.zero);
        }
        else if (effectiveType == typeof(Vector3Int))
        {
            state.Value = EditorGUILayout.Vector3IntField(label, state.Value != null ? (Vector3Int)state.Value : Vector3Int.zero);
        }
        else if (effectiveType == typeof(Color))
        {
            state.Value = EditorGUILayout.ColorField(label, state.Value != null ? (Color)state.Value : Color.white);
        }
        else if (effectiveType == typeof(Rect))
        {
            state.Value = EditorGUILayout.RectField(label, state.Value != null ? (Rect)state.Value : new Rect());
        }
        else if (effectiveType == typeof(Bounds))
        {
            state.Value = EditorGUILayout.BoundsField(label, state.Value != null ? (Bounds)state.Value : new Bounds());
        }
        else if (effectiveType == typeof(Quaternion))
        {
            Quaternion quaternion = state.Value != null ? (Quaternion)state.Value : Quaternion.identity;
            Vector4 components = new Vector4(quaternion.x, quaternion.y, quaternion.z, quaternion.w);
            components = EditorGUILayout.Vector4Field(label + " (x, y, z, w)", components);
            state.Value = new Quaternion(components.x, components.y, components.z, components.w);
        }
        else if (effectiveType == typeof(AnimationCurve))
        {
            state.Value = EditorGUILayout.CurveField(label, state.Value as AnimationCurve ?? new AnimationCurve());
        }
        else if (effectiveType == typeof(Gradient))
        {
            state.Value = EditorGUILayout.GradientField(label, state.Value as Gradient ?? new Gradient());
        }
        else if (effectiveType.IsEnum)
        {
            state.Value = EditorGUILayout.EnumPopup(label, (Enum)(state.Value ?? Activator.CreateInstance(effectiveType)));
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(effectiveType))
        {
            state.Value = EditorGUILayout.ObjectField(label, state.Value as UnityEngine.Object, effectiveType, true);
        }
        else if (IsTextConvertedType(effectiveType))
        {
            EditorGUILayout.LabelField(label);
            state.Text = EditorGUILayout.TextField(state.Text ?? Convert.ToString(state.Value, CultureInfo.InvariantCulture));
        }
        else
        {
            EditorGUILayout.LabelField(label + "  (JSON)");
            state.Json = EditorGUILayout.TextArea(state.Json ?? "null", GUILayout.MinHeight(70f));
            EditorGUILayout.LabelField("支持数组、List、Dictionary、普通类和结构体；字段名需与类型定义一致。", EditorStyles.miniLabel);
        }
    }

    private void DrawInvokeButton()
    {
        bool targetMissing = !method.IsStatic && !IsTargetAlive();
        using (new EditorGUI.DisabledScope(targetMissing))
        {
            if (GUILayout.Button("调用方法", GUILayout.Height(30f)))
            {
                InvokeMethod();
            }
        }

        if (targetMissing)
        {
            EditorGUILayout.HelpBox("目标实例已经销毁，无法调用实例方法。", MessageType.Warning);
        }
    }

    private void DrawResult()
    {
        if (string.IsNullOrEmpty(statusText))
        {
            return;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("调用结果", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(statusText + "，耗时 " + elapsedMilliseconds.ToString("0.###") + " ms");

        if (invocationException != null)
        {
            EditorGUILayout.HelpBox(
                invocationException.GetType().FullName + "\n" + invocationException.Message + "\n" + invocationException.StackTrace,
                MessageType.Error);
            return;
        }

        if (method.ReturnType == typeof(void))
        {
            EditorGUILayout.LabelField("返回值：void", EditorStyles.miniLabel);
        }
        else
        {
            valueDrawer.DrawValue("返回值", method.ReturnType, returnValue, "ReturnValue", 8);
        }

        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (!parameters[i].IsOut && !parameters[i].ParameterType.IsByRef)
            {
                continue;
            }

            Type valueType = GetParameterValueType(parameters[i]);
            valueDrawer.DrawValue(
                parameters[i].Name + " 调用后",
                valueType,
                lastArguments[i],
                "ArgumentAfter." + i,
                8);
        }
    }

    private void InitializeParameters()
    {
        ParameterInfo[] parameters = method.GetParameters();
        parameterStates = new ParameterState[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            Type valueType = GetParameterValueType(parameter);
            object initialValue = GetInitialValue(parameter, valueType);
            parameterStates[i] = new ParameterState
            {
                Value = initialValue,
                IsNull = initialValue == null && !valueType.IsValueType,
                Text = initialValue != null ? Convert.ToString(initialValue, CultureInfo.InvariantCulture) : string.Empty,
                Json = SerializeInitialJson(initialValue)
            };
        }
    }

    private void InvokeMethod()
    {
        invocationException = null;
        returnValue = null;
        ParameterInfo[] parameters = method.GetParameters();
        object[] arguments = new object[parameters.Length];

        try
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                arguments[i] = GetParameterValue(parameters[i], parameterStates[i]);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                returnValue = method.Invoke(method.IsStatic ? null : target, arguments);
            }
            finally
            {
                stopwatch.Stop();
                elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }

            lastArguments = arguments;
            statusText = "调用成功";
        }
        catch (TargetInvocationException exception)
        {
            invocationException = exception.InnerException ?? exception;
            lastArguments = arguments;
            statusText = "方法内部抛出异常";
        }
        catch (Exception exception)
        {
            invocationException = exception;
            lastArguments = arguments;
            statusText = "调用失败";
        }
    }

    private static object GetParameterValue(ParameterInfo parameter, ParameterState state)
    {
        Type valueType = GetParameterValueType(parameter);
        if (parameter.IsOut)
        {
            return valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
        }

        if (state.IsNull && (!valueType.IsValueType || Nullable.GetUnderlyingType(valueType) != null))
        {
            return null;
        }

        Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (IsDirectValueType(effectiveType))
        {
            return state.Value;
        }

        if (IsTextConvertedType(effectiveType))
        {
            if (effectiveType == typeof(char))
            {
                if (string.IsNullOrEmpty(state.Text) || state.Text.Length != 1)
                {
                    throw new FormatException(parameter.Name + " 必须是单个字符。");
                }

                return state.Text[0];
            }

            if (effectiveType == typeof(Guid))
            {
                return Guid.Parse(state.Text);
            }

            if (effectiveType == typeof(DateTime))
            {
                return DateTime.Parse(state.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (effectiveType == typeof(TimeSpan))
            {
                return TimeSpan.Parse(state.Text, CultureInfo.InvariantCulture);
            }

            return Convert.ChangeType(state.Text, effectiveType, CultureInfo.InvariantCulture);
        }

        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto
        };
        return JsonConvert.DeserializeObject(state.Json, effectiveType, settings);
    }

    private static object GetInitialValue(ParameterInfo parameter, Type valueType)
    {
        if (parameter.IsOptional
            && parameter.DefaultValue != null
            && parameter.DefaultValue != DBNull.Value
            && parameter.DefaultValue != Missing.Value)
        {
            return parameter.DefaultValue;
        }

        if (valueType == typeof(string))
        {
            return string.Empty;
        }

        if (valueType == typeof(AnimationCurve))
        {
            return new AnimationCurve();
        }

        if (valueType == typeof(Gradient))
        {
            return new Gradient();
        }

        return valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
    }

    private static string SerializeInitialJson(object value)
    {
        if (value == null)
        {
            return "null";
        }

        try
        {
            return JsonConvert.SerializeObject(value, Formatting.Indented, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
        }
        catch
        {
            return "{}";
        }
    }

    private static Type GetParameterValueType(ParameterInfo parameter)
    {
        return parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()
            : parameter.ParameterType;
    }

    private static bool IsDirectValueType(Type type)
    {
        return type == typeof(string)
               || type == typeof(bool)
               || type == typeof(int)
               || type == typeof(long)
               || type == typeof(float)
               || type == typeof(double)
               || type == typeof(Vector2)
               || type == typeof(Vector3)
               || type == typeof(Vector4)
               || type == typeof(Vector2Int)
               || type == typeof(Vector3Int)
               || type == typeof(Color)
               || type == typeof(Rect)
               || type == typeof(Bounds)
               || type == typeof(Quaternion)
               || type == typeof(AnimationCurve)
               || type == typeof(Gradient)
               || type.IsEnum
               || typeof(UnityEngine.Object).IsAssignableFrom(type);
    }

    private static bool IsTextConvertedType(Type type)
    {
        return type == typeof(byte)
               || type == typeof(sbyte)
               || type == typeof(short)
               || type == typeof(ushort)
               || type == typeof(uint)
               || type == typeof(ulong)
               || type == typeof(decimal)
               || type == typeof(char)
               || type == typeof(Guid)
               || type == typeof(DateTime)
               || type == typeof(TimeSpan);
    }

    private bool IsTargetAlive()
    {
        if (target == null)
        {
            return false;
        }

        UnityEngine.Object unityObject = target as UnityEngine.Object;
        return !(target is UnityEngine.Object) || unityObject != null;
    }

    private static string FormatDefaultValue(object value)
    {
        return value == null || value == DBNull.Value || value == Missing.Value ? "null" : value.ToString();
    }

    private sealed class ParameterState
    {
        public object Value;
        public string Text;
        public string Json;
        public bool IsNull;
    }
}
