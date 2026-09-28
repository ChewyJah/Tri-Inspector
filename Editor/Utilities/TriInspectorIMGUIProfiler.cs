using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TriInspector.Utilities
{
    internal static class TriInspectorIMGUIProfiler
    {
        private const string MenuRoot = "Tools/Tri Inspector/Profiling/";
        private static readonly List<Registration> Registrations = new List<Registration>();
        private static readonly HashSet<IMGUIContainer> Containers = new HashSet<IMGUIContainer>();
        private static readonly ProfilerMarker RefreshMarker = new ProfilerMarker("Inspector.IMGUI.RefreshLabels");
        private static bool _enabled;
        private static double _nextRefresh;
        private static int _nextId;

        [MenuItem(MenuRoot + "Start IMGUI Callback Profiling")]
        private static void StartProfiling()
        {
            StopProfiling();
            _enabled = true;
            _nextRefresh = 0;
            EditorApplication.update += Refresh;
            EditorApplication.quitting += StopProfiling;
            AssemblyReloadEvents.beforeAssemblyReload += StopProfiling;
            Refresh();
            Debug.Log($"Inspector IMGUI profiling enabled for {Registrations.Count} callbacks. " +
                      "Record the Editor with Deep Profile disabled, then expand OnGUI or search for " +
                      "Inspector.IMGUI/. Use Tools/Tri Inspector/Profiling/Stop IMGUI Callback Profiling when finished. " +
                      "Profiling automatically stops on script reload.");
        }

        [MenuItem(MenuRoot + "Start IMGUI Callback Profiling", true)]
        private static bool CanStartProfiling() => !_enabled;

        [MenuItem(MenuRoot + "Stop IMGUI Callback Profiling")]
        private static void StopProfiling()
        {
            _enabled = false;
            EditorApplication.update -= Refresh;
            EditorApplication.quitting -= StopProfiling;
            AssemblyReloadEvents.beforeAssemblyReload -= StopProfiling;

            foreach (var registration in Registrations)
            {
                registration.Dispose();
            }

            Registrations.Clear();
            Containers.Clear();
        }

        [MenuItem(MenuRoot + "Stop IMGUI Callback Profiling", true)]
        private static bool CanStopProfiling() => _enabled;

        private static void Refresh()
        {
            if (!_enabled || EditorApplication.timeSinceStartup < _nextRefresh)
            {
                return;
            }

            _nextRefresh = EditorApplication.timeSinceStartup + 1;
            using var sample = RefreshMarker.Auto();

            for (var i = Registrations.Count - 1; i >= 0; i--)
            {
                var registration = Registrations[i];
                if (registration.IsAttached)
                {
                    continue;
                }

                registration.Dispose();
                Containers.Remove(registration.Container);
                Registrations.RemoveAt(i);
            }

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window.GetType().FullName != "UnityEditor.InspectorWindow")
                {
                    continue;
                }

                window.rootVisualElement.Query<IMGUIContainer>().ForEach(Attach);
            }
        }

        private static void Attach(IMGUIContainer container)
        {
            if (container.panel == null || container.onGUIHandler == null || Containers.Contains(container))
            {
                return;
            }

            var callback = container.onGUIHandler.Method;
            var owner = "Inspector";
            for (var parent = container.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetType().Name == "EditorElement")
                {
                    owner = parent.name;
                    break;
                }
            }

            var label = $"Inspector.IMGUI/{++_nextId} {owner}/{container.name} " +
                        $"[{callback.DeclaringType?.FullName}.{callback.Name}]";
            Registrations.Add(new Registration(container, label));
            Containers.Add(container);
        }

        private sealed class Registration : IDisposable
        {
            private readonly Action _original;
            private readonly Action _wrapper;
            private readonly ProfilerMarker _layout;
            private readonly ProfilerMarker _repaint;
            private readonly ProfilerMarker _other;

            public IMGUIContainer Container { get; }
            public bool IsAttached => Container.panel != null && Container.onGUIHandler == _wrapper;

            public Registration(IMGUIContainer container, string label)
            {
                Container = container;
                _original = container.onGUIHandler;
                _wrapper = Invoke;
                _layout = new ProfilerMarker(label + "/Layout");
                _repaint = new ProfilerMarker(label + "/Repaint");
                _other = new ProfilerMarker(label + "/OtherEvent");
                container.onGUIHandler = _wrapper;
                container.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            private void Invoke()
            {
                var eventType = Event.current?.type;
                var marker = eventType == EventType.Layout ? _layout
                    : eventType == EventType.Repaint ? _repaint
                    : _other;
                using (marker.Auto())
                {
                    _original();
                }
            }

            private void OnDetach(DetachFromPanelEvent evt) => Dispose();

            public void Dispose()
            {
                Container.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                if (Container.onGUIHandler == _wrapper)
                {
                    Container.onGUIHandler = _original;
                }
            }
        }
    }
}
