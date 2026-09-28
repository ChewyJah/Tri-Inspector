using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TriInspector
{
    [Serializable]
    public struct TriDictionaryEntry<TKey, TValue> : IEquatable<TriDictionaryEntry<TKey, TValue>>, ITriDictionaryEntry
    {
        [SerializeField] public TKey key;
        [SerializeField] public TValue value;

        public bool Equals(TriDictionaryEntry<TKey, TValue> other)
        {
            return EqualityComparer<TKey>.Default.Equals(key, other.key) &&
                   (IsStructuredValue(value) && IsStructuredValue(other.value)
                       ? ValuesEqual(value, other.value)
                       : EqualityComparer<TValue>.Default.Equals(value, other.value));
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((TriDictionaryEntry<TKey, TValue>) obj);
        }

        public override int GetHashCode()
        {
            return IsStructuredValue(value) ? HashCode.Combine(key, ValueHashCode(value)) : HashCode.Combine(key, value);
        }

        private static bool IsStructuredValue(object value) => value is IList || value is IDictionary;

        private static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is IDictionary leftDictionary && right is IDictionary rightDictionary &&
                left.GetType() == right.GetType())
            {
                if (leftDictionary.Count != rightDictionary.Count)
                {
                    return false;
                }

                foreach (DictionaryEntry entry in leftDictionary)
                {
                    if (!rightDictionary.Contains(entry.Key) ||
                        !ValuesEqual(entry.Value, rightDictionary[entry.Key]))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (left is IList leftList && right is IList rightList && left.GetType() == right.GetType())
            {
                if (leftList.Count != rightList.Count)
                {
                    return false;
                }

                for (var i = 0; i < leftList.Count; i++)
                {
                    if (!ValuesEqual(leftList[i], rightList[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return Equals(left, right);
        }

        private static int ValueHashCode(object value)
        {
            if (value is IDictionary dictionary)
            {
                var hash = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    hash = unchecked(hash + HashCode.Combine(ValueHashCode(entry.Key), ValueHashCode(entry.Value)));
                }

                return hash;
            }

            if (value is IList list)
            {
                var hash = 17;
                for (var i = 0; i < list.Count; i++)
                {
                    hash = unchecked(hash * 31 + ValueHashCode(list[i]));
                }

                return hash;
            }

            return value?.GetHashCode() ?? 0;
        }

        public static List<TriDictionaryEntry<TKey, TValue>> MakeList(Dictionary<TKey, TValue> dict)
        {
            var list = new List<TriDictionaryEntry<TKey, TValue>>(dict?.Count ?? 0);

            if (dict != null)
            {
                foreach (var (key, val) in dict)
                {
                    list.Add(new TriDictionaryEntry<TKey, TValue> {key = key, value = val});
                }
            }

            return list;
        }

        public static List<TriDictionaryEntry<TKey, TValue>> MakeListFromSerializedProperty(
            SerializedProperty serializedProperty)
        {
            var count = serializedProperty.arraySize;
            var list = new List<TriDictionaryEntry<TKey, TValue>>(count);

            for (var i = 0; i < count; i++)
            {
                using var element = serializedProperty.GetArrayElementAtIndex(i);

                using var keyProperty = element.FindPropertyRelative("key");
                using var valueProperty = element.FindPropertyRelative("value");

                list.Add(new TriDictionaryEntry<TKey, TValue>
                {
                    key = (TKey) ReadSerializedValue(keyProperty, typeof(TKey)),
                    value = (TValue) ReadSerializedValue(valueProperty, typeof(TValue)),
                });
            }

            return list;
        }

        public static void WriteToSerializedProperty(List<TriDictionaryEntry<TKey, TValue>> list,
            SerializedProperty serializedProperty)
        {
            var count = list?.Count ?? 0;

            if (serializedProperty.arraySize != count)
            {
                serializedProperty.arraySize = count;
            }

            if (list != null)
            {
                for (var i = 0; i < count; i++)
                {
                    var entry = list[i];
                    using var element = serializedProperty.GetArrayElementAtIndex(i);
                    using var keyProperty = element.FindPropertyRelative("key");
                    using var valueProperty = element.FindPropertyRelative("value");

                    WriteSerializedValue(keyProperty, entry.key, typeof(TKey));
                    WriteSerializedValue(valueProperty, entry.value, typeof(TValue));
                }
            }

            serializedProperty.serializedObject.ApplyModifiedProperties();
        }

        private static object ReadSerializedValue(SerializedProperty property, Type valueType)
        {
            if (IsDictionaryType(valueType))
            {
                return ReadSerializedDictionary(property, valueType);
            }

            if (!IsCollectionProperty(property, valueType))
            {
                var value = property.boxedValue;
                if (valueType == typeof(char))
                {
                    return Convert.ToChar(value);
                }

                return valueType.IsEnum ? Enum.ToObject(valueType, value) : value;
            }

            var count = property.arraySize;
            var elementType = valueType.IsArray
                ? valueType.GetElementType()
                : valueType.GetGenericArguments()[0];

            if (valueType.IsArray)
            {
                var values = Array.CreateInstance(elementType, count);

                for (var i = 0; i < count; i++)
                {
                    using var elementProperty = property.GetArrayElementAtIndex(i);
                    values.SetValue(ReadSerializedValue(elementProperty, elementType), i);
                }

                return values;
            }

            var list = (IList) Activator.CreateInstance(valueType, new object[] {count});
            for (var i = 0; i < count; i++)
            {
                using var elementProperty = property.GetArrayElementAtIndex(i);
                list.Add(ReadSerializedValue(elementProperty, elementType));
            }

            return list;
        }

        private static void WriteSerializedValue(SerializedProperty property, object value, Type valueType)
        {
            if (IsDictionaryType(valueType))
            {
                WriteSerializedDictionary(property, value as IDictionary, valueType);
                return;
            }

            if (!IsCollectionProperty(property, valueType))
            {
                property.boxedValue = value;
                return;
            }

            var values = value as IList;
            var count = values?.Count ?? 0;
            if (property.arraySize != count)
            {
                property.arraySize = count;
            }

            var elementType = valueType.IsArray
                ? valueType.GetElementType()
                : valueType.GetGenericArguments()[0];

            for (var i = 0; i < count; i++)
            {
                using var elementProperty = property.GetArrayElementAtIndex(i);
                WriteSerializedValue(elementProperty, values[i], elementType);
            }
        }

        private static bool IsCollectionProperty(SerializedProperty property, Type valueType)
        {
            return property.isArray && property.propertyType != SerializedPropertyType.String &&
                   (valueType.IsArray ||
                    valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(List<>));
        }

        private static object ReadSerializedDictionary(SerializedProperty property, Type dictionaryType)
        {
            var dictionary = (IDictionary) Activator.CreateInstance(dictionaryType);
            var genericArguments = dictionaryType.GetGenericArguments();

            for (var i = 0; i < property.arraySize; i++)
            {
                using var element = property.GetArrayElementAtIndex(i);
                using var keyProperty = element.FindPropertyRelative("key");
                using var valueProperty = element.FindPropertyRelative("value");

                var key = ReadSerializedValue(keyProperty, genericArguments[0]);
                if (key is null)
                {
                    continue;
                }

                dictionary[key] = ReadSerializedValue(valueProperty, genericArguments[1]);
            }

            return dictionary;
        }

        private static void WriteSerializedDictionary(SerializedProperty property, IDictionary dictionary,
            Type dictionaryType)
        {
            var count = dictionary?.Count ?? 0;
            if (property.arraySize != count)
            {
                property.arraySize = count;
            }

            var genericArguments = dictionaryType.GetGenericArguments();
            if (dictionary == null)
            {
                return;
            }

            var index = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                using var element = property.GetArrayElementAtIndex(index++);
                using var keyProperty = element.FindPropertyRelative("key");
                using var valueProperty = element.FindPropertyRelative("value");

                WriteSerializedValue(keyProperty, entry.Key, genericArguments[0]);
                WriteSerializedValue(valueProperty, entry.Value, genericArguments[1]);
            }
        }

        private static bool IsDictionaryType(Type valueType)
        {
            return valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        }

        public static Dictionary<TKey, TValue> MakeDict(List<TriDictionaryEntry<TKey, TValue>> list,
            List<int> duplicateEntryIndices, List<int> nullKeyEntryIndices)
        {
            duplicateEntryIndices.Clear();
            nullKeyEntryIndices.Clear();

            var dict = new Dictionary<TKey, TValue>();

            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var entry = list[i];

                    if (IsNullKey(entry.key))
                    {
                        nullKeyEntryIndices.Add(i);
                        continue;
                    }

                    if (!dict.TryAdd(entry.key, entry.value))
                    {
                        duplicateEntryIndices.Add(i);
                    }
                }
            }

            return dict;
        }

        private static bool IsNullKey(TKey key)
        {
            return key is null || key is UnityEngine.Object unityKey && unityKey == null;
        }
    }

    public interface ITriDictionaryEntry
    {
    }
}
