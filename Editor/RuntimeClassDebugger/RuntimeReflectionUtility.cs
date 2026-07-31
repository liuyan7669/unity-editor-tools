using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor.Compilation;

internal static class RuntimeReflectionUtility
{
    private const BindingFlags DeclaredMemberFlags = BindingFlags.Public
                                                     | BindingFlags.NonPublic
                                                     | BindingFlags.Instance
                                                     | BindingFlags.Static
                                                     | BindingFlags.DeclaredOnly;

    public static List<Type> GetProjectRuntimeTypes()
    {
        HashSet<string> projectAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
        UnityEditor.Compilation.Assembly[] playerAssemblies =
            CompilationPipeline.GetAssemblies(AssembliesType.Player);

        for (int i = 0; i < playerAssemblies.Length; i++)
        {
            UnityEditor.Compilation.Assembly assembly = playerAssemblies[i];
            if (assembly.sourceFiles.Any(IsAssetSourceFile))
            {
                projectAssemblyNames.Add(assembly.name);
            }
        }

        List<Type> result = new List<Type>();
        System.Reflection.Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < loadedAssemblies.Length; i++)
        {
            System.Reflection.Assembly assembly = loadedAssemblies[i];
            if (!projectAssemblyNames.Contains(assembly.GetName().Name))
            {
                continue;
            }

            Type[] types = GetLoadableTypes(assembly);
            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                Type type = types[typeIndex];
                if (type == null
                    || !type.IsClass
                    || type.IsGenericTypeDefinition
                    || type.IsDefined(typeof(CompilerGeneratedAttribute), false))
                {
                    continue;
                }

                result.Add(type);
            }
        }

        return result
            .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<FieldInfo> GetAllFields(Type type)
    {
        List<FieldInfo> fields = new List<FieldInfo>();
        foreach (Type currentType in EnumerateTypeHierarchy(type))
        {
            fields.AddRange(currentType.GetFields(DeclaredMemberFlags));
        }

        return fields
            .OrderBy(field => GetInheritanceDepth(field.DeclaringType))
            .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<PropertyInfo> GetAllProperties(Type type)
    {
        List<PropertyInfo> properties = new List<PropertyInfo>();
        foreach (Type currentType in EnumerateTypeHierarchy(type))
        {
            properties.AddRange(currentType.GetProperties(DeclaredMemberFlags));
        }

        return properties
            .OrderBy(property => GetInheritanceDepth(property.DeclaringType))
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<MethodInfo> GetAllMethods(Type type)
    {
        List<MethodInfo> methods = new List<MethodInfo>();
        foreach (Type currentType in EnumerateTypeHierarchy(type))
        {
            methods.AddRange(currentType.GetMethods(DeclaredMemberFlags));
        }

        return methods
            .OrderBy(method => GetInheritanceDepth(method.DeclaringType))
            .ThenBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(method => method.GetParameters().Length)
            .ToList();
    }

    public static string GetTypeDisplayName(Type type)
    {
        if (type == null)
        {
            return "null";
        }

        if (type.IsByRef)
        {
            return GetTypeDisplayName(type.GetElementType()) + "&";
        }

        if (type.IsArray)
        {
            return GetTypeDisplayName(type.GetElementType()) + "[]";
        }

        Type nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
        {
            return GetTypeDisplayName(nullableType) + "?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int markerIndex = name.IndexOf('`');
        if (markerIndex >= 0)
        {
            name = name.Substring(0, markerIndex);
        }

        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(GetTypeDisplayName)) + ">";
    }

    public static string GetMethodSignature(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string parameterText = string.Join(", ", parameters.Select(parameter =>
        {
            string prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
            Type parameterType = parameter.ParameterType.IsByRef
                ? parameter.ParameterType.GetElementType()
                : parameter.ParameterType;
            return prefix + GetTypeDisplayName(parameterType) + " " + parameter.Name;
        }));

        string genericSuffix = method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">"
            : string.Empty;
        return GetTypeDisplayName(method.ReturnType) + " " + method.Name + genericSuffix + "(" + parameterText + ")";
    }

    public static string GetMemberAccessText(FieldInfo field)
    {
        return field.IsPublic ? "public" : field.IsFamily ? "protected" : field.IsAssembly ? "internal" : "private";
    }

    public static string GetMemberAccessText(MethodBase method)
    {
        return method.IsPublic ? "public" : method.IsFamily ? "protected" : method.IsAssembly ? "internal" : "private";
    }

    public static string GetPropertyAccessText(PropertyInfo property)
    {
        MethodInfo accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
        return accessor == null ? "未知" : GetMemberAccessText(accessor);
    }

    public static bool IsStatic(PropertyInfo property)
    {
        MethodInfo accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
        return accessor != null && accessor.IsStatic;
    }

    public static string GetMemberKey(MemberInfo member)
    {
        return (member.DeclaringType != null ? member.DeclaringType.FullName : string.Empty)
               + "." + member.Name + "#" + member.MetadataToken;
    }

    private static bool IsAssetSourceFile(string sourceFile)
    {
        return sourceFile.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
               || sourceFile.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase);
    }

    private static Type[] GetLoadableTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null).ToArray();
        }
    }

    private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
    {
        Stack<Type> hierarchy = new Stack<Type>();
        for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            hierarchy.Push(currentType);
        }

        while (hierarchy.Count > 0)
        {
            yield return hierarchy.Pop();
        }
    }

    private static int GetInheritanceDepth(Type type)
    {
        int depth = 0;
        while (type != null)
        {
            depth++;
            type = type.BaseType;
        }

        return depth;
    }
}
