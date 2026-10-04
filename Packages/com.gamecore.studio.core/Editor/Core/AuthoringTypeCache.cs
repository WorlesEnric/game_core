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
                if (seen.Add(type))
                {
                    yield return type;
                }
            }

            foreach (Type attribute in MirrorAttributes(AuthoringMetadata.AuthorableName))
            {
                foreach (Type type in TypeCache.GetTypesWithAttribute(attribute))
                {
                    if (seen.Add(type))
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
                if (seen.Add(method))
                {
                    yield return method;
                }
            }

            foreach (Type attribute in MirrorAttributes(AuthoringMetadata.AuthorOperationName))
            {
                foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute(attribute))
                {
                    if (seen.Add(method))
                    {
                        yield return method;
                    }
                }
            }
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
