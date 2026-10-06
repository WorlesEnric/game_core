#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Views
{
    public sealed partial class ReflectionGameplayBridge
    {
        private readonly Dictionary<string, object> _moveRequests = new Dictionary<string, object>(StringComparer.Ordinal);
        public OperationResult Move(AuthoringRef reference, Vector3 position, float yaw)
        {
            object? target = MoveTarget(reference);
            object? commands = _gameplay == null ? null : Get(_gameplay, "Commands");
            if (target == null || commands == null)
                return OperationResult.Refused(DiagnosticCodes.StaleTarget, "No live entity maps to this authored target.");
            object? receipt = InvokeCommand(commands, "Place", new[] { target, (object)Milli(position.x), Milli(position.y), Milli(position.z), Yaw(yaw) }, true);
            object? result = receipt == null ? null : Get(receipt, "Result");
            object? kind = result == null ? null : Get(result, "Kind");
            object? request = receipt == null ? null : Get(receipt, "Request");
            if (receipt == null || !(Get(receipt, "Admitted") is bool admitted) || !admitted || kind == null
                || !kind.GetType().IsEnum || (kind.ToString() != "Accepted" && kind.ToString() != "Committed") || request == null)
                return OperationResult.Refused(DiagnosticCodes.Refused, _diagnostic.Length > 0 ? _diagnostic : "world.place refused: " + result);
            object? world = Get(request, "World");
            string id = "w:" + (world == null ? null : Get(world, "Session")) + "/i:" + Get(request, "IssuerId") + "/s:" + Get(request, "IssuerSequence");
            _moveRequests[id] = request;
            return OperationResult.Applied().WithGameCoreOp(id).WithDetail("Runtime move admitted; non-undoable. Apply to authored is available only once its pose commits.");
        }

        public bool IsAt(AuthoringRef reference, Vector3 position, float yaw, string operationId)
        {
            object? target = MoveTarget(reference);
            object? slots = _gameplay == null ? null : Get(_gameplay, "Slots");
            if (target == null || slots == null) return false;
            object? root = Get(_gameplay!, "Root");
            object? world = root == null ? null : Get(root, "World");
            object? session = world == null ? null : Get(world, "Session");
            if (session == null || !operationId.StartsWith("w:" + session + "/", StringComparison.Ordinal)) return false;
            if (!_moveRequests.TryGetValue(operationId, out object request)) return false;
            object? host = Get(root!, "Host");
            object? messages = host == null ? null : Get(host, "Messages");
            object? requests = messages == null ? null : Get(messages, "Requests");
            object?[] receiptArgs = { request, null };
            if (requests == null || !(Invoke(requests, "TryGet", receiptArgs) is bool found) || !found || receiptArgs[1] == null) return false;
            object? outcome = Get(receiptArgs[1]!, "Outcome");
            if (outcome == null || Get(outcome, "Kind")?.ToString() != "Committed") return false;
            Type? declarations = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                declarations = assembly.GetType("GameCore.Gameplay.Contracts.GameplaySlots", false);
                if (declarations != null) break;
            }
            object? owner = declarations?.GetField("WorldOwner")?.GetValue(null);
            if (owner == null) return false;
            return SlotEquals("PosX", Milli(position.x)) && SlotEquals("PosY", Milli(position.y))
                && SlotEquals("PosZ", Milli(position.z)) && SlotEquals("Yaw", Yaw(yaw));

            bool SlotEquals(string name, int expected)
            {
                object? slot = declarations!.GetField(name)?.GetValue(null);
                if (slot == null) return false;
                object?[] args = { target, owner, slot, 0 };
                return Invoke(slots, "TryRead", args) is bool found && found && args[3] is int value && value == expected;
            }
        }

        private object? MoveTarget(AuthoringRef reference)
        {
            if (!Locate() || _gameplay == null || reference.AuthoringId == null) return null;
            object? entities = Get(_gameplay, "Entities");
            if (entities == null || !(Get(entities, "Records") is IEnumerable records)) return null;
            object? target = null;
            foreach (object record in records)
            {
                if (!string.Equals(Get(record, "AuthoringId") as string, reference.AuthoringId, StringComparison.Ordinal)) continue;
                if (target != null) return null;
                target = Get(record, "Target");
            }
            return target;
        }
        private static int Milli(float value) => checked((int)Math.Round((double)value * 1000, MidpointRounding.AwayFromZero));
        private static int Yaw(float degrees)
        {
            int value = checked((int)Math.Round(degrees * Math.PI / 180 * 1000, MidpointRounding.AwayFromZero));
            return ((value % 6283) + 6283) % 6283;
        }
    }
}
