using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Cowart.EditorUtilities.Editor
{
    /// <summary>检查当前已加载场景中的常见结构和序列化问题。</summary>
    public sealed class SceneValidationWindow : EditorWindow
    {
        private enum Severity
        {
            Error,
            Warning
        }

        private sealed class ValidationIssue
        {
            public Severity Level;
            public string Message;
            public Object Context;
            public string SceneName;
        }

        private readonly List<ValidationIssue> issues = new List<ValidationIssue>();
        private Vector2 scrollPosition;
        private string status = "尚未检查。";

        [MenuItem("Cowart/项目开发工具/场景体检")]
        private static void Open()
        {
            GetWindow<SceneValidationWindow>("场景体检").minSize = new Vector2(700f, 440f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("只检查当前已加载场景，不修改场景内容。", MessageType.Info);
            if (GUILayout.Button("检查全部已加载场景", GUILayout.Height(30f))) ValidateScenes();
            EditorGUILayout.LabelField(status, EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < issues.Count; i++) DrawIssue(issues[i]);
            EditorGUILayout.EndScrollView();
        }

        private void ValidateScenes()
        {
            issues.Clear();
            List<GameObject> allObjects = new List<GameObject>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    allObjects.AddRange(GetHierarchyObjects(roots[i]));
                }
            }

            ValidateObjectReferences(allObjects);
            ValidateEventSystems(allObjects);
            ValidateAudioListeners(allObjects);
            ValidateMainCameras(allObjects);

            int errorCount = 0;
            for (int i = 0; i < issues.Count; i++) if (issues[i].Level == Severity.Error) errorCount++;
            status = "检查完成：错误 " + errorCount + "，警告 " + (issues.Count - errorCount) + "。";
        }

        private void ValidateObjectReferences(List<GameObject> allObjects)
        {
            for (int objectIndex = 0; objectIndex < allObjects.Count; objectIndex++)
            {
                GameObject target = allObjects[objectIndex];
                int missingScripts = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(target);
                if (missingScripts > 0)
                {
                    AddIssue(Severity.Error, target, "包含 " + missingScripts + " 个 Missing Script。 ");
                }

                Component[] components = target.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null) continue;
                    try
                    {
                        SerializedObject serializedObject = new SerializedObject(component);
                        SerializedProperty property = serializedObject.GetIterator();
                        bool enterChildren = true;
                        while (property.NextVisible(enterChildren))
                        {
                            enterChildren = false;
                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                property.propertyPath != "m_Script" &&
                                property.objectReferenceValue == null &&
                                property.objectReferenceInstanceIDValue != 0)
                            {
                                AddIssue(
                                    Severity.Error,
                                    target,
                                    component.GetType().Name + "." + property.propertyPath + " 引用已丢失。");
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        AddIssue(Severity.Warning, target, "无法检查 " + component.GetType().Name + "：" + exception.Message);
                    }
                }
            }
        }

        private void ValidateEventSystems(List<GameObject> allObjects)
        {
            List<EventSystem> eventSystems = new List<EventSystem>();
            bool hasSelectable = false;
            for (int i = 0; i < allObjects.Count; i++)
            {
                EventSystem eventSystem = allObjects[i].GetComponent<EventSystem>();
                if (eventSystem != null && eventSystem.isActiveAndEnabled) eventSystems.Add(eventSystem);
                if (!hasSelectable && allObjects[i].GetComponent<Selectable>() != null) hasSelectable = true;
            }

            if (hasSelectable && eventSystems.Count == 0)
            {
                AddIssue(Severity.Error, null, "场景包含 Selectable UI，但没有启用的 EventSystem。");
            }
            else if (eventSystems.Count > 1)
            {
                for (int i = 0; i < eventSystems.Count; i++)
                {
                    AddIssue(Severity.Error, eventSystems[i].gameObject, "存在多个启用的 EventSystem。");
                }
            }
        }

        private void ValidateAudioListeners(List<GameObject> allObjects)
        {
            List<AudioListener> listeners = new List<AudioListener>();
            for (int i = 0; i < allObjects.Count; i++)
            {
                AudioListener listener = allObjects[i].GetComponent<AudioListener>();
                if (listener != null && listener.isActiveAndEnabled) listeners.Add(listener);
            }

            if (listeners.Count > 1)
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    AddIssue(Severity.Warning, listeners[i].gameObject, "存在多个启用的 AudioListener。");
                }
            }
        }

        private void ValidateMainCameras(List<GameObject> allObjects)
        {
            List<Camera> cameras = new List<Camera>();
            for (int i = 0; i < allObjects.Count; i++)
            {
                Camera camera = allObjects[i].GetComponent<Camera>();
                if (camera != null && camera.isActiveAndEnabled && allObjects[i].CompareTag("MainCamera"))
                {
                    cameras.Add(camera);
                }
            }

            if (cameras.Count == 0)
            {
                AddIssue(Severity.Warning, null, "没有找到启用且标记为 MainCamera 的 Camera。");
            }
            else if (cameras.Count > 1)
            {
                for (int i = 0; i < cameras.Count; i++)
                {
                    AddIssue(Severity.Warning, cameras[i].gameObject, "存在多个启用且标记为 MainCamera 的 Camera。");
                }
            }
        }

        private void AddIssue(Severity level, Object context, string message)
        {
            string sceneName = context is Component component
                ? component.gameObject.scene.name
                : context is GameObject gameObject
                    ? gameObject.scene.name
                    : "全部场景";
            issues.Add(new ValidationIssue
            {
                Level = level,
                Message = message,
                Context = context,
                SceneName = sceneName
            });
        }

        private static List<GameObject> GetHierarchyObjects(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            List<GameObject> results = new List<GameObject>(transforms.Length);
            for (int i = 0; i < transforms.Length; i++) results.Add(transforms[i].gameObject);
            return results;
        }

        private static void DrawIssue(ValidationIssue issue)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            MessageType messageType = issue.Level == Severity.Error ? MessageType.Error : MessageType.Warning;
            EditorGUILayout.HelpBox("[" + issue.SceneName + "] " + issue.Message, messageType);
            if (issue.Context != null && GUILayout.Button("定位", GUILayout.Width(70f)))
            {
                Selection.activeObject = issue.Context;
                EditorGUIUtility.PingObject(issue.Context);
            }
            EditorGUILayout.EndVertical();
        }
    }
}
