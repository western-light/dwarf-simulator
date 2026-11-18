using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

#if UNITY_2022_1_OR_NEWER
using UnityEditor.Build;
#endif

namespace FIMSpace.FEditor
{
    /// <summary>Helpers for detecting managed types and maintaining scripting define symbols.</summary>
    public sealed class FDefinesCompilation
    {
        /// <summary>
        /// Returns types declared in the requested namespace or any of its child namespaces.
        /// Pass an assembly-name prefix in <paramref name="root"/> to limit the search,
        /// or an empty string to search every loaded assembly.
        /// </summary>
        public static List<Type> GetTypesInNamespace(string nameSpace, string root = "Assembly")
        {
            List<Type> childTypes = new List<Type>();

            if (string.IsNullOrEmpty(nameSpace)) return childTypes;

            Assembly[] assemblies = GetCurrentAssemblies();
            for (int assemblyId = 0; assemblyId < assemblies.Length; assemblyId++)
            {
                Assembly assembly = assemblies[assemblyId];
                if (assembly == null) continue;

                if (!string.IsNullOrEmpty(root) &&
                    !assembly.FullName.StartsWith(root, StringComparison.Ordinal))
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    // Optional dependencies can prevent a subset of types from loading.
                    // Keep the types which the runtime did load successfully.
                    types = exception.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                if (types == null) continue;

                for (int typeId = 0; typeId < types.Length; typeId++)
                {
                    Type type = types[typeId];
                    if (type == null || string.IsNullOrEmpty(type.Namespace)) continue;

                    if (string.Equals(type.Namespace, nameSpace, StringComparison.Ordinal) ||
                        type.Namespace.StartsWith(nameSpace + ".", StringComparison.Ordinal))
                        childTypes.Add(type);
                }
            }

            return childTypes;
        }

        public static System.Reflection.Assembly[] GetCurrentAssemblies()
        {
#if UNITY_6000_7_OR_NEWER
            var assem = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
            var asArray = new System.Reflection.Assembly[assem.Count];
            for( int i = 0; i < assem.Count; i++ ) asArray[i] = assem[i] as System.Reflection.Assembly;
            return asArray;
#else
            return System.AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        /// <summary>Checks for an exact type without adding a compile-time assembly dependency.</summary>
        public static bool HasType(string fullTypeName, string assemblyName)
        {
            if (string.IsNullOrEmpty(fullTypeName) || string.IsNullOrEmpty(assemblyName)) return false;

            return Type.GetType(fullTypeName + ", " + assemblyName, false) != null;
        }

        public static void SetDefine(string newDefine)
        {
            string normalizedDefine = NormalizeDefine(newDefine);
            if (normalizedDefine.Length == 0) return;

            List<string> currentDefines = GetDefines();
            if (ContainsDefine(currentDefines, normalizedDefine)) return;

            currentDefines.Add(normalizedDefine);
            SetDefines(currentDefines);
        }

        public static void RemoveDefine(string define)
        {
            string normalizedDefine = NormalizeDefine(define);
            if (normalizedDefine.Length == 0) return;

            List<string> currentDefines = GetDefines();
            bool removed = false;

            for (int i = currentDefines.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(currentDefines[i], normalizedDefine, StringComparison.OrdinalIgnoreCase)) continue;

                currentDefines.RemoveAt(i);
                removed = true;
            }

            if (removed) SetDefines(currentDefines);
        }

        public static bool IsDefined(string define)
        {
            string normalizedDefine = NormalizeDefine(define);
            if (normalizedDefine.Length == 0) return false;

            return ContainsDefine(GetDefines(), normalizedDefine);
        }

        private static bool ContainsDefine(List<string> defines, string define)
        {
            for (int i = 0; i < defines.Count; i++)
                if (string.Equals(defines[i], define, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        private static List<string> GetDefines()
        {
            string[] splitDefines = GetSerializedDefines().Split(';');
            List<string> defines = new List<string>(splitDefines.Length);

            for (int i = 0; i < splitDefines.Length; i++)
            {
                string define = splitDefines[i].Trim();
                if (define.Length > 0 && !ContainsDefine(defines, define)) defines.Add(define);
            }

            return defines;
        }

        private static string NormalizeDefine(string define)
        {
            return string.IsNullOrWhiteSpace(define) ? string.Empty : define.Trim().ToUpperInvariant();
        }

        private static string GetSerializedDefines()
        {
#if UNITY_2022_1_OR_NEWER
            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            return PlayerSettings.GetScriptingDefineSymbols(target);
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
        }

        private static void SetDefines(List<string> defines)
        {
            string serializedDefines = string.Join(";", defines.ToArray());

#if UNITY_2022_1_OR_NEWER
            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            PlayerSettings.SetScriptingDefineSymbols(target, serializedDefines);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, serializedDefines);
#endif
        }
    }
}
