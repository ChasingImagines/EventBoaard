using System;
using UnityEngine;

namespace Yasin.EventBoard
{
    /// <summary>
    /// EventBoardDatabase içindeki her olayın veri tipini ve başlangıç (Blackboard Default) değerini
    /// dış paket bağımlılığı olmadan ([SerializeReference] ile) tanımlayan soyut temel sınıf.
    /// </summary>
    [Serializable]
    public abstract class EventPayloadDefinition
    {
        public abstract Type PayloadType { get; }
        public abstract string DisplayName { get; }
        public abstract bool SupportsDefaultValue { get; }

        /// <summary>
        /// Veritabanındaki varsayılan değeri ve kalıcılık bilgisini ilgili generic kanala (Zero-Boxing ile) yükler.
        /// </summary>
        public abstract void ConfigureChannel(int runtimeId, string guid, string eventPath, bool isPersistent);

        /// <summary>
        /// Editör arayüzünde varsayılan değerin metin özetini döndürür.
        /// </summary>
        public abstract string GetDefaultValueSummary();
    }

    /// <summary>
    /// Yeni bir veri tipi (struct veya class) eklemek için bu sınıftan türetmeniz yeterlidir;
    /// Editör tip seçici menüsünde otomatik olarak listelenir.
    /// </summary>
    [Serializable]
    public abstract class EventPayloadDefinition<T> : EventPayloadDefinition
    {
        [Tooltip("Oyun başladığında henüz hiçbir olay tetiklenmeden önce Blackboard'da bulunacak başlangıç değeri.")]
        public T defaultValue;

        public override Type PayloadType => typeof(T);
        public override bool SupportsDefaultValue => true;

        public override void ConfigureChannel(int runtimeId, string guid, string eventPath, bool isPersistent)
        {
            EventBoard.ConfigureChannel<T>(runtimeId, guid, eventPath, defaultValue, SupportsDefaultValue, isPersistent);
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

