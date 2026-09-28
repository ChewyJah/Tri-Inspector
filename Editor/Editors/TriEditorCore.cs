using UnityEditor;
using UnityEditor.UIElements;
using Unity.Profiling;
using UnityEngine.UIElements;

namespace TriInspector.Editors
{
    public class TriEditorCore
    {
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("TriInspector.Update");
        private readonly SerializedObject _serializedObject;

        private TriPropertyTreeForSerializedObject _inspector;
        private IVisualElementScheduledItem _updateSchedule;

        public TriEditorCore(Editor editor)
        {
            _serializedObject = editor.serializedObject;
        }
        
        public TriEditorCore(SerializedObject serializedObject)
        {
            _serializedObject = serializedObject;
        }

        public bool HideMonoScript { get; set; }

        public void Dispose()
        {
            _updateSchedule?.Pause();
            _updateSchedule = null;
            if (_inspector != null)
            {
                _inspector.Dispose();
            }

            _inspector = null;
        }

        public VisualElement CreateVisualElement()
        {
            _updateSchedule?.Pause();
            var serializedObject = _serializedObject;

            var container = new VisualElement();

            if (serializedObject.targetObjects.Length == 0 || serializedObject.targetObject == null)
            {
                container.Add(new HelpBox("Script is missing", HelpBoxMessageType.Warning));
                return container;
            }

            if (_inspector == null)
            {
                _inspector = new TriPropertyTreeForSerializedObject(serializedObject);
            }
            
            serializedObject.UpdateIfRequiredOrScript();
            _inspector.Update();
            _inspector.RunValidation();

            if (!HideMonoScript && !_inspector.RootProperty.TryGetAttribute(out HideMonoScriptAttribute _))
            {
                var scriptProperty = serializedObject.FindProperty("m_Script");
                if (scriptProperty != null)
                {
                    var scriptField = new PropertyField(scriptProperty);
                    scriptField.SetEnabled(false);
                    scriptField.Bind(serializedObject);
                    container.Add(scriptField);
                }
            }

            container.Add(_inspector.GetRootElement());

            _updateSchedule = container.schedule.Execute(() =>
            {
                using var sample = UpdateMarker.Auto();
                _inspector.Update();
                _inspector.RunValidationIfRequired();
            }).Every(VisualElementExtensions.PollIntervalMs);

            return container;
        }
    }
}
