using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 提供物体全部组件的复制、剪切和粘贴菜单。
/// </summary>
public static class ComponentClipboardMenu
{
    private const string ComponentCopyMenu = "Cowart/组件/复制";
    private const string ComponentCutMenu = "Cowart/组件/剪切";
    private const string ComponentPasteMenu = "Cowart/组件/粘贴";
    private const string GameObjectCopyMenu = "GameObject/Cowart/组件/复制";
    private const string GameObjectCutMenu = "GameObject/Cowart/组件/剪切";
    private const string GameObjectPasteMenu = "GameObject/Cowart/组件/粘贴";
    private const int MenuGroupPriority = 0;

    private static ComponentClipboardState clipboard;

    /// <summary>从顶部 Cowart 菜单复制当前选中物体的全部组件。</summary>
    [MenuItem(ComponentCopyMenu, false, MenuGroupPriority)]
    private static void CopyFromComponentMenu()
    {
        Capture(Selection.activeGameObject, false);
    }

    /// <summary>验证顶部 Cowart 复制菜单。</summary>
    [MenuItem(ComponentCopyMenu, true)]
    private static bool ValidateCopyFromComponentMenu()
    {
        return ComponentClipboardUtility.CanCapture(Selection.activeGameObject);
    }

    /// <summary>从顶部 Cowart 菜单剪切当前选中物体的全部组件。</summary>
    [MenuItem(ComponentCutMenu, false, MenuGroupPriority + 1)]
    private static void CutFromComponentMenu()
    {
        Capture(Selection.activeGameObject, true);
    }

    /// <summary>验证顶部 Cowart 剪切菜单。</summary>
    [MenuItem(ComponentCutMenu, true)]
    private static bool ValidateCutFromComponentMenu()
    {
        return ComponentClipboardUtility.CanCapture(Selection.activeGameObject);
    }

    /// <summary>把组件剪贴板内容粘贴到全部选中物体。</summary>
    [MenuItem(ComponentPasteMenu, false, MenuGroupPriority + 2)]
    private static void PasteFromComponentMenu()
    {
        Paste(GetPasteTargets(null));
    }

    /// <summary>验证顶部 Cowart 粘贴菜单。</summary>
    [MenuItem(ComponentPasteMenu, true)]
    private static bool ValidatePasteFromComponentMenu()
    {
        return ComponentClipboardUtility.CanPaste(
            clipboard,
            GetPasteTargets(null));
    }

    /// <summary>从 Hierarchy 物体右键菜单复制全部组件。</summary>
    [MenuItem(GameObjectCopyMenu, false, MenuGroupPriority)]
    private static void CopyFromGameObjectMenu(MenuCommand menuCommand)
    {
        Capture(GetContextGameObject(menuCommand), false);
    }

    /// <summary>验证 Hierarchy 物体右键复制菜单。</summary>
    [MenuItem(GameObjectCopyMenu, true)]
    private static bool ValidateCopyFromGameObjectMenu()
    {
        return ComponentClipboardUtility.CanCapture(Selection.activeGameObject);
    }

    /// <summary>从 Hierarchy 物体右键菜单剪切全部组件。</summary>
    [MenuItem(GameObjectCutMenu, false, MenuGroupPriority + 1)]
    private static void CutFromGameObjectMenu(MenuCommand menuCommand)
    {
        Capture(GetContextGameObject(menuCommand), true);
    }

    /// <summary>验证 Hierarchy 物体右键剪切菜单。</summary>
    [MenuItem(GameObjectCutMenu, true)]
    private static bool ValidateCutFromGameObjectMenu()
    {
        return ComponentClipboardUtility.CanCapture(Selection.activeGameObject);
    }

    /// <summary>从 Hierarchy 物体右键菜单粘贴到全部选中物体。</summary>
    [MenuItem(GameObjectPasteMenu, false, MenuGroupPriority + 2)]
    private static void PasteFromGameObjectMenu(MenuCommand menuCommand)
    {
        Paste(GetPasteTargets(GetContextGameObject(menuCommand)));
    }

    /// <summary>验证 Hierarchy 物体右键粘贴菜单。</summary>
    [MenuItem(GameObjectPasteMenu, true)]
    private static bool ValidatePasteFromGameObjectMenu()
    {
        return ComponentClipboardUtility.CanPaste(
            clipboard,
            GetPasteTargets(Selection.activeGameObject));
    }

    /// <summary>记录源物体及其组件列表和 Transform 快照。</summary>
    private static void Capture(GameObject source, bool cut)
    {
        if (!ComponentClipboardUtility.TryCreateClipboard(
                source,
                cut,
                out ComponentClipboardState newClipboard,
                out string errorMessage))
        {
            EditorUtility.DisplayDialog(
                cut ? "剪切组件失败" : "复制组件失败",
                errorMessage,
                "确定");
            return;
        }

        clipboard = newClipboard;
        string operationName = cut ? "剪切" : "复制";
        string message = "已" + operationName + " " + source.name + " 的 Transform 和 "
                         + clipboard.Components.Length + " 个其他组件，请选择目标物体后粘贴。";
        Notify(message);
        Debug.Log(message, source);
    }

    /// <summary>把剪贴板中的 Transform 和其他组件批量粘贴到目标物体。</summary>
    private static void Paste(GameObject[] targets)
    {
        if (!ComponentClipboardUtility.TryValidatePasteTargets(
                clipboard,
                targets,
                out string errorMessage))
        {
            EditorUtility.DisplayDialog("粘贴组件失败", errorMessage, "确定");
            return;
        }

        ComponentClipboardState currentClipboard = clipboard;
        try
        {
            ComponentTransferResult result = ComponentClipboardUtility.Paste(
                currentClipboard,
                targets);

            if (currentClipboard.Cut)
            {
                clipboard = null;
            }

            EditorGUIUtility.PingObject(targets[0]);
            Notify(result.GetSummary(currentClipboard.Cut));
            Debug.Log(
                result.GetDetailedSummary(
                    currentClipboard.Source,
                    targets,
                    currentClipboard.Cut),
                targets[0]);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "粘贴组件失败",
                exception.Message + "\n\n本次粘贴已整体回滚。",
                "确定");
        }
    }

    /// <summary>获取右键菜单命令对应的物体。</summary>
    private static GameObject GetContextGameObject(MenuCommand menuCommand)
    {
        if (menuCommand != null && menuCommand.context is GameObject gameObject)
        {
            return gameObject;
        }

        if (menuCommand != null && menuCommand.context is Component component)
        {
            return component.gameObject;
        }

        return Selection.activeGameObject;
    }

    /// <summary>
    /// 获取批量粘贴目标；右键物体属于当前多选时保留全部选择，
    /// 否则只使用右键物体，并把活动物体排在首位作为剪切引用的承接目标。
    /// </summary>
    private static GameObject[] GetPasteTargets(GameObject context)
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        GameObject preferredTarget = context != null
            ? context
            : Selection.activeGameObject;

        bool contextIsSelected = context == null;
        if (context != null)
        {
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                if (selectedObjects[i] == context)
                {
                    contextIsSelected = true;
                    break;
                }
            }
        }

        if (!contextIsSelected)
        {
            return preferredTarget != null
                ? new[] { preferredTarget }
                : Array.Empty<GameObject>();
        }

        List<GameObject> targets = new List<GameObject>(selectedObjects.Length);
        if (preferredTarget != null)
        {
            targets.Add(preferredTarget);
        }

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];
            if (selectedObject != null && selectedObject != preferredTarget)
            {
                targets.Add(selectedObject);
            }
        }

        return targets.ToArray();
    }

    /// <summary>在 Scene 视图中显示剪贴板操作提示。</summary>
    private static void Notify(string message)
    {
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent(message));
        }
    }
}

/// <summary>保存一次复制或剪切操作的源对象和快照。</summary>
internal sealed class ComponentClipboardState
{
    internal ComponentClipboardState(
        GameObject source,
        Component[] components,
        TransformSnapshot transformSnapshot,
        bool cut)
    {
        Source = source;
        Components = components;
        TransformSnapshot = transformSnapshot;
        Cut = cut;
    }

    internal GameObject Source { get; }
    internal Component[] Components { get; }
    internal TransformSnapshot TransformSnapshot { get; }
    internal bool Cut { get; }
}

/// <summary>保存 Transform 或 RectTransform 可粘贴的 Inspector 数值。</summary>
internal sealed class TransformSnapshot
{
    private readonly Type transformType;
    private readonly Vector3 localPosition;
    private readonly Quaternion localRotation;
    private readonly Vector3 localScale;
    private readonly Vector2 anchorMin;
    private readonly Vector2 anchorMax;
    private readonly Vector2 pivot;
    private readonly Vector2 sizeDelta;
    private readonly Vector3 anchoredPosition3D;

    internal TransformSnapshot(Transform source)
    {
        transformType = source.GetType();
        localPosition = source.localPosition;
        localRotation = source.localRotation;
        localScale = source.localScale;

        if (source is RectTransform rectTransform)
        {
            anchorMin = rectTransform.anchorMin;
            anchorMax = rectTransform.anchorMax;
            pivot = rectTransform.pivot;
            sizeDelta = rectTransform.sizeDelta;
            anchoredPosition3D = rectTransform.anchoredPosition3D;
        }
    }

    internal Type TransformType => transformType;

    /// <summary>把快照值粘贴到同类型目标 Transform。</summary>
    internal void Apply(
        Transform target,
        string undoName,
        HashSet<Component> modifiedComponents)
    {
        Undo.RecordObject(target, undoName);

        if (target is RectTransform targetRectTransform)
        {
            targetRectTransform.anchorMin = anchorMin;
            targetRectTransform.anchorMax = anchorMax;
            targetRectTransform.pivot = pivot;
            targetRectTransform.sizeDelta = sizeDelta;
            targetRectTransform.anchoredPosition3D = anchoredPosition3D;
        }
        else
        {
            target.localPosition = localPosition;
        }

        target.localRotation = localRotation;
        target.localScale = localScale;
        modifiedComponents.Add(target);
    }
}

/// <summary>
/// 执行组件创建、覆盖、引用重映射、Transform 粘贴和剪切删除。
/// </summary>
internal static class ComponentClipboardUtility
{
    /// <summary>判断当前物体是否可以作为复制或剪切源。</summary>
    internal static bool CanCapture(GameObject source)
    {
        return TryValidateSource(source, out _);
    }

    /// <summary>判断剪贴板能否粘贴到全部指定目标。</summary>
    internal static bool CanPaste(
        ComponentClipboardState clipboard,
        GameObject[] targets)
    {
        return TryValidatePasteTargets(clipboard, targets, out _);
    }

    /// <summary>创建组件剪贴板并记录 Transform 快照。</summary>
    internal static bool TryCreateClipboard(
        GameObject source,
        bool cut,
        out ComponentClipboardState clipboard,
        out string message)
    {
        clipboard = null;
        if (!TryValidateSource(source, out message))
        {
            return false;
        }

        clipboard = new ComponentClipboardState(
            source,
            GetTransferableComponents(source),
            new TransformSnapshot(source.transform),
            cut);
        message = string.Empty;
        return true;
    }

    /// <summary>检查剪贴板和目标物体是否仍然有效。</summary>
    internal static bool TryValidatePaste(
        ComponentClipboardState clipboard,
        GameObject target,
        out string message)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            message = "请退出 Play Mode 后再粘贴组件。";
            return false;
        }

        if (clipboard == null || clipboard.Source == null)
        {
            message = "组件剪贴板为空，或源物体已经被删除。请重新复制或剪切。";
            return false;
        }

        if (target == null)
        {
            message = "请选择要粘贴组件的目标物体。";
            return false;
        }

        if (target == clipboard.Source)
        {
            message = "不能把组件粘贴回同一个源物体。";
            return false;
        }

        if (EditorUtility.IsPersistent(target))
        {
            message = "不直接修改 Project 中的 Prefab 资源。请先进入 Prefab Mode 再粘贴。";
            return false;
        }

        if (!target.scene.IsValid())
        {
            message = "目标物体必须位于已加载场景或 Prefab Mode 中。";
            return false;
        }

        if (target.transform.GetType() != clipboard.TransformSnapshot.TransformType)
        {
            message = "源物体使用 " + clipboard.TransformSnapshot.TransformType.Name
                      + "，目标物体使用 " + target.transform.GetType().Name
                      + "。Transform 类型不同，无法完整粘贴锚点和布局数据。";
            return false;
        }

        for (int i = 0; i < clipboard.Components.Length; i++)
        {
            Component component = clipboard.Components[i];
            if (component == null || component.gameObject != clipboard.Source)
            {
                message = "源物体的组件结构在复制或剪切后发生了变化，请重新执行复制或剪切。";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    /// <summary>粘贴前一次性验证全部目标，避免批处理中途失败。</summary>
    internal static bool TryValidatePasteTargets(
        ComponentClipboardState clipboard,
        GameObject[] targets,
        out string message)
    {
        if (targets == null || targets.Length == 0)
        {
            message = "请至少选择一个要粘贴组件的目标物体。";
            return false;
        }

        HashSet<GameObject> uniqueTargets = new HashSet<GameObject>();
        for (int i = 0; i < targets.Length; i++)
        {
            GameObject target = targets[i];
            if (!uniqueTargets.Add(target))
            {
                message = "批量粘贴目标中包含重复物体，请重新选择。";
                return false;
            }

            if (!TryValidatePaste(clipboard, target, out string targetMessage))
            {
                string targetName = target != null ? target.name : "<空目标>";
                message = "目标 “" + targetName + "” 无法粘贴：\n" + targetMessage;
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    /// <summary>批量粘贴 Transform 和其他组件，并在全部成功后处理剪切源。</summary>
    internal static ComponentTransferResult Paste(
        ComponentClipboardState clipboard,
        GameObject[] targets)
    {
        if (!TryValidatePasteTargets(
                clipboard,
                targets,
                out string validationMessage))
        {
            throw new InvalidOperationException(validationMessage);
        }

        string undoName = clipboard.Cut ? "剪切并粘贴全部组件" : "粘贴全部组件";
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        try
        {
            ComponentTransferResult result = new ComponentTransferResult();
            result.TargetCount = targets.Length;
            HashSet<Component> modifiedComponents = new HashSet<Component>();
            Dictionary<Object, Object> primaryComponentMap = null;

            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                GameObject target = targets[targetIndex];
                clipboard.TransformSnapshot.Apply(
                    target.transform,
                    undoName,
                    modifiedComponents);

                Dictionary<Object, Object> componentMap = CreateTargetComponents(
                    clipboard.Components,
                    target,
                    result);
                CopySerializedComponents(
                    clipboard.Components,
                    componentMap,
                    undoName,
                    modifiedComponents);

                Dictionary<Object, Object> internalReferenceMap =
                    CreateInternalReferenceMap(
                        clipboard.Source,
                        target,
                        componentMap);
                result.InternalReferenceCount += RemapTargetReferences(
                    clipboard.Components,
                    componentMap,
                    internalReferenceMap,
                    modifiedComponents);

                if (targetIndex == 0)
                {
                    primaryComponentMap = componentMap;
                }
            }

            if (clipboard.Cut)
            {
                result.ExternalReferenceCount = RemapLoadedSceneReferences(
                    clipboard.Source,
                    targets,
                    clipboard.Components,
                    primaryComponentMap,
                    undoName,
                    modifiedComponents);
                DestroySourceComponents(clipboard.Components);
            }

            FinalizeChanges(
                clipboard.Source,
                targets,
                modifiedComponents);
            Undo.CollapseUndoOperations(undoGroup);
            EditorApplication.RepaintHierarchyWindow();
            SceneView.RepaintAll();
            return result;
        }
        catch
        {
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
    }

    /// <summary>获取物体上除 Transform / RectTransform 外的有效组件。</summary>
    private static Component[] GetTransferableComponents(GameObject gameObject)
    {
        Component[] allComponents = gameObject.GetComponents<Component>();
        List<Component> components = new List<Component>(allComponents.Length);
        for (int i = 0; i < allComponents.Length; i++)
        {
            Component component = allComponents[i];
            if (component != null && !(component is Transform))
            {
                components.Add(component);
            }
        }

        return components.ToArray();
    }

    /// <summary>检查源物体是否可以进入组件剪贴板。</summary>
    private static bool TryValidateSource(GameObject source, out string message)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            message = "请退出 Play Mode 后再复制或剪切组件。";
            return false;
        }

        if (source == null)
        {
            message = "请先选择源物体。";
            return false;
        }

        if (EditorUtility.IsPersistent(source))
        {
            message = "不直接读取 Project 中的 Prefab 资源。请先进入 Prefab Mode 再复制或剪切。";
            return false;
        }

        if (!source.scene.IsValid())
        {
            message = "源物体必须位于已加载场景或 Prefab Mode 中。";
            return false;
        }

        if (HasMissingScript(source))
        {
            message = "源物体包含 Missing Script，无法保证完整复制，请先修复或移除缺失脚本。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    /// <summary>为每个源组件查找同顺序目标组件，找不到时通过 Undo 添加。</summary>
    private static Dictionary<Object, Object> CreateTargetComponents(
        Component[] sourceComponents,
        GameObject target,
        ComponentTransferResult result)
    {
        Dictionary<Object, Object> componentMap = new Dictionary<Object, Object>();
        Dictionary<Type, int> sourceTypeIndices = new Dictionary<Type, int>();

        for (int i = 0; i < sourceComponents.Length; i++)
        {
            Component sourceComponent = sourceComponents[i];
            Type componentType = sourceComponent.GetType();
            sourceTypeIndices.TryGetValue(componentType, out int typeIndex);
            sourceTypeIndices[componentType] = typeIndex + 1;

            Component[] targetComponents = target.GetComponents(componentType);
            Component targetComponent;
            if (typeIndex < targetComponents.Length)
            {
                targetComponent = targetComponents[typeIndex];
                result.OverwrittenCount++;
            }
            else
            {
                targetComponent = Undo.AddComponent(target, componentType);
                if (targetComponent == null)
                {
                    throw new InvalidOperationException(
                        "无法向目标物体添加组件：" + componentType.FullName);
                }

                result.AddedCount++;
            }

            componentMap.Add(sourceComponent, targetComponent);
        }

        return componentMap;
    }

    /// <summary>复制每个组件的完整序列化数据。</summary>
    private static void CopySerializedComponents(
        Component[] sourceComponents,
        Dictionary<Object, Object> componentMap,
        string undoName,
        HashSet<Component> modifiedComponents)
    {
        for (int i = 0; i < sourceComponents.Length; i++)
        {
            Component sourceComponent = sourceComponents[i];
            Component targetComponent = componentMap[sourceComponent] as Component;
            Undo.RecordObject(targetComponent, undoName);
            EditorUtility.CopySerialized(sourceComponent, targetComponent);
            modifiedComponents.Add(targetComponent);
        }
    }

    /// <summary>建立源组件、自身 GameObject 和 Transform 到目标对象的内部引用映射。</summary>
    private static Dictionary<Object, Object> CreateInternalReferenceMap(
        GameObject source,
        GameObject target,
        Dictionary<Object, Object> componentMap)
    {
        Dictionary<Object, Object> referenceMap =
            new Dictionary<Object, Object>(componentMap);
        referenceMap[source] = target;
        referenceMap[source.transform] = target.transform;
        return referenceMap;
    }

    /// <summary>把复制后组件中的源内部引用改为目标对应对象。</summary>
    private static int RemapTargetReferences(
        Component[] sourceComponents,
        Dictionary<Object, Object> componentMap,
        Dictionary<Object, Object> referenceMap,
        HashSet<Component> modifiedComponents)
    {
        int remappedCount = 0;
        for (int i = 0; i < sourceComponents.Length; i++)
        {
            Component targetComponent = componentMap[sourceComponents[i]] as Component;
            remappedCount += RemapSerializedReferences(
                targetComponent,
                referenceMap,
                null,
                modifiedComponents);
        }

        return remappedCount;
    }

    /// <summary>剪切粘贴前更新已加载场景中指向源组件的外部引用。</summary>
    private static int RemapLoadedSceneReferences(
        GameObject source,
        GameObject[] targets,
        Component[] sourceComponents,
        Dictionary<Object, Object> componentMap,
        string undoName,
        HashSet<Component> modifiedComponents)
    {
        HashSet<Component> sourceComponentSet =
            new HashSet<Component>(sourceComponents);
        List<Scene> scenes = GetLoadedScenes(source.scene, targets);
        int remappedCount = 0;

        for (int sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            GameObject[] rootObjects = scenes[sceneIndex].GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
            {
                Component[] sceneComponents =
                    rootObjects[rootIndex].GetComponentsInChildren<Component>(true);
                for (int componentIndex = 0;
                     componentIndex < sceneComponents.Length;
                     componentIndex++)
                {
                    Component component = sceneComponents[componentIndex];
                    if (component == null || sourceComponentSet.Contains(component))
                    {
                        continue;
                    }

                    remappedCount += RemapSerializedReferences(
                        component,
                        componentMap,
                        undoName,
                        modifiedComponents);
                }
            }
        }

        return remappedCount;
    }

    /// <summary>查找并替换组件序列化属性中的对象引用。</summary>
    private static int RemapSerializedReferences(
        Component component,
        Dictionary<Object, Object> referenceMap,
        string undoName,
        HashSet<Component> modifiedComponents)
    {
        if (component == null)
        {
            return 0;
        }

        SerializedObject serializedObject = new SerializedObject(component);
        serializedObject.UpdateIfRequiredOrScript();
        SerializedProperty iterator = serializedObject.GetIterator();
        List<ReferenceReplacement> replacements = new List<ReferenceReplacement>();
        bool enterChildren = true;

        while (iterator.Next(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            Object currentReference = iterator.objectReferenceValue;
            if (currentReference != null &&
                referenceMap.TryGetValue(currentReference, out Object replacement) &&
                replacement != currentReference)
            {
                replacements.Add(new ReferenceReplacement(
                    iterator.propertyPath,
                    replacement));
            }
        }

        if (replacements.Count == 0)
        {
            return 0;
        }

        if (!string.IsNullOrEmpty(undoName))
        {
            Undo.RecordObject(component, undoName);
        }

        serializedObject.UpdateIfRequiredOrScript();
        for (int i = 0; i < replacements.Count; i++)
        {
            SerializedProperty property = serializedObject.FindProperty(
                replacements[i].PropertyPath);
            if (property != null)
            {
                property.objectReferenceValue = replacements[i].Replacement;
            }
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        modifiedComponents.Add(component);
        return replacements.Count;
    }

    /// <summary>获取全部已加载场景，并确保包含源物体和全部目标所在场景。</summary>
    private static List<Scene> GetLoadedScenes(
        Scene sourceScene,
        GameObject[] targets)
    {
        List<Scene> scenes = new List<Scene>();
        HashSet<int> sceneHandles = new HashSet<int>();

        AddLoadedScene(sourceScene, scenes, sceneHandles);
        for (int i = 0; i < targets.Length; i++)
        {
            AddLoadedScene(targets[i].scene, scenes, sceneHandles);
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            AddLoadedScene(SceneManager.GetSceneAt(i), scenes, sceneHandles);
        }

        return scenes;
    }

    /// <summary>把有效且尚未加入的场景添加到扫描列表。</summary>
    private static void AddLoadedScene(
        Scene scene,
        List<Scene> scenes,
        HashSet<int> sceneHandles)
    {
        if (scene.IsValid() && scene.isLoaded && sceneHandles.Add(scene.handle))
        {
            scenes.Add(scene);
        }
    }

    /// <summary>反向删除源物体上已剪切的非 Transform 组件。</summary>
    private static void DestroySourceComponents(Component[] sourceComponents)
    {
        for (int i = sourceComponents.Length - 1; i >= 0; i--)
        {
            if (sourceComponents[i] != null)
            {
                Undo.DestroyObjectImmediate(sourceComponents[i]);
            }
        }
    }

    /// <summary>记录 Prefab 覆盖、脏标记和受影响场景。</summary>
    private static void FinalizeChanges(
        GameObject source,
        GameObject[] targets,
        HashSet<Component> modifiedComponents)
    {
        HashSet<int> sceneHandles = new HashSet<int>();
        List<Scene> scenes = new List<Scene>();

        foreach (Component component in modifiedComponents)
        {
            if (component == null)
            {
                continue;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorUtility.SetDirty(component);
            AddLoadedScene(component.gameObject.scene, scenes, sceneHandles);
        }

        EditorUtility.SetDirty(source);
        AddLoadedScene(source.scene, scenes, sceneHandles);
        for (int i = 0; i < targets.Length; i++)
        {
            EditorUtility.SetDirty(targets[i]);
            AddLoadedScene(targets[i].scene, scenes, sceneHandles);
        }

        for (int i = 0; i < scenes.Count; i++)
        {
            EditorSceneManager.MarkSceneDirty(scenes[i]);
        }
    }

    /// <summary>检查物体是否包含无法识别类型的 Missing Script。</summary>
    private static bool HasMissingScript(GameObject gameObject)
    {
        Component[] components = gameObject.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>记录一个待替换的序列化对象引用。</summary>
    private sealed class ReferenceReplacement
    {
        internal ReferenceReplacement(string propertyPath, Object replacement)
        {
            PropertyPath = propertyPath;
            Replacement = replacement;
        }

        internal string PropertyPath { get; }
        internal Object Replacement { get; }
    }
}

/// <summary>记录一次组件粘贴的处理数量。</summary>
internal sealed class ComponentTransferResult
{
    internal int TargetCount;
    internal int AddedCount;
    internal int OverwrittenCount;
    internal int InternalReferenceCount;
    internal int ExternalReferenceCount;

    /// <summary>生成 Scene 视图通知使用的简短结果。</summary>
    internal string GetSummary(bool cut)
    {
        return (cut ? "剪切粘贴" : "批量粘贴") + "完成：" + TargetCount
               + " 个目标，Transform + 覆盖 "
               + OverwrittenCount + "，添加 " + AddedCount;
    }

    /// <summary>生成 Console 使用的完整处理结果。</summary>
    internal string GetDetailedSummary(
        GameObject source,
        GameObject[] targets,
        bool cut)
    {
        string[] targetNames = new string[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            targetNames[i] = targets[i].name;
        }

        return "组件" + (cut ? "剪切粘贴" : "复制粘贴") + "完成："
               + source.name + " → " + string.Join("，", targetNames)
               + "\n目标物体：" + TargetCount
               + "\nTransform / RectTransform：每个目标均已覆盖"
               + "\n覆盖组件：" + OverwrittenCount
               + "\n新增组件：" + AddedCount
               + "\n内部引用重映射：" + InternalReferenceCount
               + "\n外部引用重映射：" + ExternalReferenceCount
               + (cut && TargetCount > 1
                   ? "\n剪切外部引用承接目标：" + targets[0].name
                   : string.Empty);
    }
}
