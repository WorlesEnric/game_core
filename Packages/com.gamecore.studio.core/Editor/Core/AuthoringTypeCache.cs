// GameCore.Studio.Edit - TypeCache discovery of authorable types and tool methods, for the Model attributes and for
// mirror attributes declared by runtime packages (matched by type name; see AuthoringMetadata).
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    public static class AuthoringTypeCache
    {
        /// <summary>Every type carrying the Model or a mirror [Authorable].</summary>
        public static IEnumerable<Type> AuthorableTypes()
        {
            HashSet<Type> seen = new HashSet<Type>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<AuthorableAttribute>())
            {
                if (IsProductionAssembly(type.Assembly) && seen.Add(type))
                {
                    yield return type;
                }
            }

            foreach (Type attribute in MirrorAttributes(AuthoringMetadata.AuthorableName))
            {
                foreach (Type type in TypeCache.GetTypesWithAttribute(attribute))
                {
                    if (IsProductionAssembly(type.Assembly) && seen.Add(type))
                    {
                        yield return type;
                    }
                }
            }
        }

        /// <summary>Every method carrying the Model or a mirror [AuthorOperation].</summary>
        public static IEnumerable<MethodInfo> ToolMethods()
        {
            HashSet<MethodInfo> seen = new HashSet<MethodInfo>();
            foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<AuthorOperationAttribute>())
            {
                if (IsProductionAssembly(method.Module.Assembly) && seen.Add(method))
                {
                    yield return method;
                }
            }

            foreach (Type attribute in MirrorAttributes(AuthoringMetadata.AuthorOperationName))
            {
                foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute(attribute))
                {
                    if (IsProductionAssembly(method.Module.Assembly) && seen.Add(method))
                    {
                        yield return method;
                    }
                }
            }
        }

        public static bool IsProductionAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name ?? string.Empty;
            foreach (string part in name.Split('.'))
                if (part.Equals("Tests", StringComparison.OrdinalIgnoreCase) || part.Equals("Fixtures", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                if (reference.Name == "nunit.framework") return false;
            return true;
        }

        private static IEnumerable<Type> MirrorAttributes(string name)
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Attribute>())
            {
                if (AuthoringMetadata.IsMirror(type, name))
                {
                    yield return type;
                }
            }
        }
    }
}
