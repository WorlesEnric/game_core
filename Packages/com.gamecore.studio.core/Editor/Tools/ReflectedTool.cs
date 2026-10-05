// GameCore.Studio.Edit - an [AuthorOperation] method as a registry tool (docs/studio/03-authoring-contracts.md s4).
// Binding: an EditContext parameter receives the context, an Operation parameter the operation, the first [Authorable]
// parameter without [AuthorArg] the resolved target (a GameObject target is adapted to the requested component type),
// [AuthorArg] parameters their converted arguments. An instance method on the target's type runs on the target.
// Returns: OperationResult as is; void/null means Applied; false means Refused; any other value becomes the output.
// When the method supplies no inverse, the engine-side inverse is a `set` restoring every authorable member it changed.
// A [AuthorOperation(ReadOnly = true)] tool is a pure query: ToolRegistry.Invoke runs it directly and Apply records no
// undo, dirty state, inverse or touched object for its target. A returned UnityEngine.Object is touched as well.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    internal sealed class ReflectedTool : IStudioTool
    {
        private readonly MethodInfo _method;
        private readonly AuthorOperationAttribute _operation;
        private readonly ParameterInfo[] _parameters;
        private readonly int _targetIndex;

        public ReflectedTool(MethodInfo method, ToolEntry entry)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            _operation = AuthoringMetadata.Operation(method)!;
            _parameters = method.GetParameters();
            _targetIndex = -1;
            for (int i = 0; i < _parameters.Length; i++)
            {
                if (AuthoringMetadata.Arg(_parameters[i]) == null
                    && AuthoringMetadata.Authorable(_parameters[i].ParameterType) != null)
                {
                    _targetIndex = i;
                    break;
                }
            }
        }

        public ToolEntry Entry { get; }

        public bool Internal => false;

        public bool ReadOnly => Entry.ReadOnly;

        public MethodInfo Method => _method;

        public ToolStageResult Stage(EditContext context)
        {
            ToolStageResult result = new ToolStageResult();
            BindArguments(context, result);
            if (_targetIndex >= 0 && context.Target != null && Adapt(context.Target, _parameters[_targetIndex].ParameterType) == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "The target is a " + context.Target.GetType().Name + ", tool '" + Entry.Id + "' needs a " + _parameters[_targetIndex].ParameterType.Name + "."));
            }

            if (!_method.IsStatic && context.Target != null && Adapt(context.Target, _method.DeclaringType!) == null)
            {
                result.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Tool '" + Entry.Id + "' runs on a " + _method.DeclaringType!.Name + "; the target is a " + context.Target.GetType().Name + "."));
            }

            if (_operation.Validator != null && typeof(IOperationValidator).IsAssignableFrom(_operation.Validator))
            {
                IOperationValidator? validator;
                try
                {
                    validator = Activator.CreateInstance(_operation.Validator) as IOperationValidator;
                }
                catch (Exception error) when (error is MissingMethodException || error is TargetInvocationException || error is MemberAccessException)
                {
                    result.Add(context.Problem(DiagnosticCodes.ValidationFailed, "Validator " + _operation.Validator.FullName + " could not be created: " + error.Message));
                    return result;
                }

                if (validator != null)
                {
                    foreach (Diagnostic diagnostic in validator.Validate(context))
                    {
                        result.Add(diagnostic);
                    }
                }
            }

            return result;
        }

        public OperationResult Apply(EditContext context)
        {
            ToolStageResult binding = new ToolStageResult();
            object?[] arguments = BindArguments(context, binding);
            if (!binding.Ok)
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, binding.Diagnostics[0].Message);
            }

            UnityEngine.Object? target = null;
            if (_targetIndex >= 0 && context.Target != null)
            {
                target = Adapt(context.Target, _parameters[_targetIndex].ParameterType);
                arguments[_targetIndex] = target;
            }

            UnityEngine.Object? instance = null;
            if (!_method.IsStatic)
            {
                instance = context.Target == null ? null : Adapt(context.Target, _method.DeclaringType!);
                if (instance == null)
                {
                    return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "Tool '" + Entry.Id + "' needs a " + _method.DeclaringType!.Name + " target.");
                }

                target ??= instance;
            }

            AuthoringTypeInfo? info = target == null ? null : context.Identity.Describe(target);
            JObject? before = info == null || ReadOnly ? null : ToolSupport.CaptureMembers(context, target!, info);
            if (target != null && !ReadOnly)
            {
                context.RecordUndo(target);
            }

            object? returned;
            try
            {
                returned = _method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException error)
            {
                Exception inner = error.InnerException ?? error;
                return OperationResult.Failed(DiagnosticCodes.Refused, Entry.Id + " failed: " + inner.GetType().Name + ": " + inner.Message);
            }

            if (target != null && !ReadOnly)
            {
                EditorUtility.SetDirty(target);
            }

            OperationResult result;
            switch (returned)
            {
                case OperationResult operationResult:
                    result = operationResult;
                    break;
                case bool flag:
                    result = flag ? OperationResult.Applied() : OperationResult.Refused(DiagnosticCodes.Refused, Entry.Id + " refused the operation.");
                    break;
                case null:
                    result = OperationResult.Applied();
                    break;
                default:
                    result = OperationResult.Applied(context.Codec.FromClr(returned));
                    break;
            }

            if (result.Status == OutcomeStatus.Applied && result.Inverse.Count == 0 && before != null && info != null && target != null)
            {
                JObject changed = ToolSupport.ChangedBefore(before, ToolSupport.CaptureMembers(context, target, info));
                AuthoringRef? reference = ToolSupport.RefOf(context, target);
                if (changed.HasValues && reference != null)
                {
                    result.WithInverse(ToolSupport.SetFieldsInverse(reference, changed));
                }
            }

            if (ReadOnly)
            {
                return result;
            }

            // An object the tool returns (a created portal asset, a placed scene object) is touched too, so the engine
            // stamps it and the index sees it before the next Flush.
            if (returned is UnityEngine.Object produced && produced != null)
            {
                result.Touch(produced);
            }

            return result.Touch(target);
        }

        private object?[] BindArguments(EditContext context, ToolStageResult problems)
        {
            object?[] arguments = new object?[_parameters.Length];
            for (int i = 0; i < _parameters.Length; i++)
            {
                ParameterInfo parameter = _parameters[i];
                AuthorArgAttribute? arg = AuthoringMetadata.Arg(parameter);
                if (arg == null)
                {
                    if (parameter.ParameterType == typeof(EditContext))
                    {
                        arguments[i] = context;
                    }
                    else if (parameter.ParameterType == typeof(Operation))
                    {
                        arguments[i] = context.Operation;
                    }
                    else if (parameter.HasDefaultValue)
                    {
                        arguments[i] = parameter.DefaultValue;
                    }

                    continue;
                }

                string name = arg.Name ?? parameter.Name ?? string.Empty;
                JToken? raw = context.Arg(name);
                if (raw == null)
                {
                    ArgSpec? spec = Entry.FindArg(name);
                    if (spec != null && spec.Required)
                    {
                        problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Tool '" + Entry.Id + "' requires argument '" + name + "'."));
                    }

                    arguments[i] = parameter.HasDefaultValue ? parameter.DefaultValue : (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null);
                    continue;
                }

                ArgSpec? declared = Entry.FindArg(name);
                if (declared != null)
                    foreach (string issue in FieldValueChecker.Check(declared, raw)) problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, issue));
                if (context.Codec.TryToClr(raw, parameter.ParameterType, out object? converted, out string? problem))
                {
                    arguments[i] = converted;
                }
                else
                {
                    problems.Add(context.Problem(DiagnosticCodes.InvalidArgs, "Argument '" + name + "' of '" + Entry.Id + "': " + problem + "."));
                }
            }

            return arguments;
        }

        /// <summary>The object adapted to <paramref name="type"/> (GameObject to component and back), or null.</summary>
        internal static UnityEngine.Object? Adapt(UnityEngine.Object target, Type type)
        {
            if (type.IsInstanceOfType(target))
            {
                return target;
            }

            GameObject? gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (gameObject == null)
            {
                return null;
            }

            if (type == typeof(GameObject))
            {
                return gameObject;
            }

            if (typeof(Component).IsAssignableFrom(type))
            {
                Component? component = gameObject.GetComponent(type);
                return component == null ? null : component;
            }

            return null;
        }
    }
}
