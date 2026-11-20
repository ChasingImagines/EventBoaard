using System;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// Abstract base class defining the payload type and initial Blackboard default value for each event
    /// in an EventBoardGroup without external package dependencies (via [SerializeReference]).
    /// </summary>
    [Serializable]
    public abstract class EventPayloadDefinition
    {
        public abstract Type PayloadType { get; }
        public abstract string DisplayName { get; }
        public abstract bool SupportsDefaultValue { get; }
        public virtual bool SupportsSaveable => SupportsDefaultValue && !typeof(UnityEngine.Object).IsAssignableFrom(PayloadType);

        /// <summary>
        /// Configures the default value and saveability metadata on the corresponding generic channel without boxing.
        /// </summary>
        public abstract void ConfigureChannel(int runtimeId, string guid, string eventPath, bool isSaveable);

        /// <summary>
        /// Returns a text summary of the default value for Editor display.
        /// </summary>
        public abstract string GetDefaultValueSummary();
    }

    /// <summary>
    /// Derive from this class to register a custom struct or class payload type;
    /// it is automatically discovered by the Editor type selector via TypeCache.
    /// </summary>
    [Serializable]
    public abstract class EventPayloadDefinition<T> : EventPayloadDefinition
    {
        [Tooltip("Initial Blackboard value available at startup before any event has been published.")]
        public T defaultValue;

        public override Type PayloadType => typeof(T);
        public override bool SupportsDefaultValue => true;

        public override void ConfigureChannel(int runtimeId, string guid, string eventPath, bool isSaveable)
        {
            EventBoard.ConfigureChannel<T>(runtimeId, guid, eventPath, defaultValue, SupportsDefaultValue, isSaveable);
        }

        public override string GetDefaultValueSummary()
        {
            if (!SupportsDefaultValue)
                return "—";
            return defaultValue != null ? defaultValue.ToString() : "null";
        }
    }

    [Serializable]
    public sealed class VoidPayload : EventPayloadDefinition<VoidSignal>
    {
        public override string DisplayName => "void";
        public override bool SupportsDefaultValue => false;
    }

    [Serializable]
    public sealed class IntPayload : EventPayloadDefinition<int>
    {
        public override string DisplayName => "int";
    }

    [Serializable]
    public sealed class FloatPayload : EventPayloadDefinition<float>
    {
        public override string DisplayName => "float";
    }

    [Serializable]
    public sealed class BoolPayload : EventPayloadDefinition<bool>
    {
        public override string DisplayName => "bool";
    }

    [Serializable]
    public sealed class StringPayload : EventPayloadDefinition<string>
    {
        public StringPayload()
        {
            defaultValue = string.Empty;
        }

        public override string DisplayName => "string";
    }

    [Serializable]
    public sealed class Vector2Payload : EventPayloadDefinition<Vector2>
    {
        public override string DisplayName => "Vector2";
    }

    [Serializable]
    public sealed class Vector3Payload : EventPayloadDefinition<Vector3>
    {
        public override string DisplayName => "Vector3";
    }

    [Serializable]
    public sealed class ColorPayload : EventPayloadDefinition<Color>
    {
        public ColorPayload()
        {
            defaultValue = Color.white;
        }

        public override string DisplayName => "Color";
    }

    [Serializable]
    public sealed class GameObjectPayload : EventPayloadDefinition<GameObject>
    {
        public override string DisplayName => "GameObject";
    }

    [Serializable]
    public sealed class TransformPayload : EventPayloadDefinition<Transform>
    {
        public override string DisplayName => "Transform";
    }
}
