# EventBoard & Blackboard for Unity

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3+-blue.svg)](https://unity.com/)
[![Language](https://img.shields.io/badge/Language-English%20%7C%20Turkish-informational)](#-documentation-languages)

[🇹🇷 Türkçe Dokümantasyon için tıklayın (README.tr.md)](README.tr.md)

**EventBoard** is a high-performance, decoupled, strongly-typed, and Inspector-friendly **Event Bus + Global Blackboard (Shared State Store)** architecture for Unity built on ScriptableObjects.

---

## 💡 Why EventBoard?

In complex game projects, systems easily become tightly coupled (Spaghetti Code):
- UI scripts reference the Player, Player references the Quest Manager, Enemies reference UI...
- When an object is destroyed or a scene unloads, `NullReferenceException` and `MissingReference` occur.

**EventBoard v2.0 provides clean decoupling:**
- **Zero-Coupling:** Systems never hold direct references to one another; both sides interact through shared ScriptableObject channels.
- **Zero Scene Setup & Any Folder Structure:** Place your `EventBoardGroup` ScriptableObjects anywhere under `Assets/`. No `Resources` folder or scene manager GameObject is required—`EventBoardRegistry` is automatically synchronized via Unity's `PreloadedAssets` and verified before builds via `IPreprocessBuildWithReport`.
- **Event Bus + Blackboard Combined:** Unlike typical event systems that fire and forget, EventBoard retains the latest value in memory. Late-spawned entities or newly opened UI menus immediately retrieve the current state without querying other managers.
- **Fresh Runtime State:** Dynamic data lives in C# static memory—never dirtying ScriptableObject asset files on disk. When Play Mode exits, memory resets cleanly.
- **Zero Allocation & Re-Entrancy Safe:** Generic delegate dispatching with runtime integer hashing avoids garbage collection spikes and handles nested `Publish` calls safely.
- **Unbreakable GUID Safety:** Channel references in scenes, prefabs, and ScriptableObjects remain valid even if you rename events or reorganize groups. Duplicated group assets automatically receive fresh GUIDs.
- **Owner-Aware & Disposable Subscriptions:** Supports `IDisposable` `EventSubscription` tokens, parameterless `Subscribe(Action)` listeners without closures, and explicit owner tracking (`key.Subscribe(this, val => ...)`) for lambdas.
- **Culture-Independent Save / Load (JSON Snapshot):** Export only marked `Saveable` data to JSON with single-line calls, safe across all locales (`tr-TR`, `en-US`, etc.), with automatic fallback to ScriptableObject defaults for missing or corrupt keys.

---

## 📦 Installation (Unity Package Manager)

1. Open **Window > Package Manager** in Unity.
2. Click the **`+`** icon in the top-left corner and select **"Add package from git URL..."**.
3. Paste the following Git URL:
   ```text
   https://github.com/ChasingImagines/EventBoard.git
   ```

---

## 🚀 Quick Start Guide

### 1. Create an Event/Blackboard Group
- Open **Tools > EventBoard > Hub** from the top menu (or right-click in the Project window **Create > EventBoard > Event Group (SO)** anywhere under `Assets/`).
- Enter a group name (e.g., `PlayerEvents`) and click **"+ Create"**.
- `EventBoardRegistry` automatically tracks your groups and registers itself in `PlayerSettings > Preloaded Assets`—no scene setup needed!
- In the Inspector, add typed channels (e.g., `Health`, `Score`).

---

### 2. Reading & Writing State (Blackboard)

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class PlayerController : MonoBehaviour
{
    // Searchable dropdown in the Inspector
    [SerializeField] private EventKey<int> healthKey;

    private void Start()
    {
        // 1. Direct Zero-GC read:
        int currentHealth = healthKey.Value;

        // 2. Safe read:
        if (healthKey.TryGet(out int val))
        {
            Debug.Log($"Current health: {val}");
        }

        // 3. Silent state update (no event notification):
        healthKey.SetState(80, notify: false);

        // 4. Update and notify only if the value changed:
        healthKey.SetStateIfChanged(50, notify: true);
    }

    public void TakeDamage(int damage)
    {
        // Raise event with new value:
        healthKey.Raise(healthKey.Value - damage);
    }
}
```

---

### 3. Listening to Events (Event Bus)

```csharp
using UnityEngine;
using UnityEngine.UI;
using Yasin.EventBoard;

public class HealthUI : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;
    [SerializeField] private Slider healthSlider;

    private EventSubscription _subscription;

    private void OnEnable()
    {
        // replayLatest: true triggers callback immediately with current Blackboard value upon subscribe!
        // Optional owner tracking + IDisposable token keeps lambdas safe too:
        _subscription = healthKey.Subscribe(this, newHealth => healthSlider.value = newHealth, replayLatest: true);
    }

    private void OnDisable()
    {
        _subscription.Dispose();
    }
}
```

---

### 4. Parameterless Signals & Listeners

`Subscribe(Action)` works natively on `EventKey<VoidSignal>` as well as any `EventKey<T>` without allocating closures:

```csharp
[SerializeField] private EventKey<VoidSignal> onGameStarted;

// Fire:
onGameStarted.Raise();

// Listen without arguments:
onGameStarted.Subscribe(HandleGameStarted);
```

---

## 🧩 Custom Data Types (Extensibility)

EventBoard supports primitives (`int`, `float`, `string`, `bool`, `Vector2`, `Vector3`, `Color`, `GameObject`, `Transform`) out of the box. You can easily register custom `struct` or `class` types:

```csharp
using System;
using Yasin.EventBoard;

[Serializable]
public struct QuestData
{
    public string questId;
    public int targetCount;
}

[Serializable]
public sealed class QuestDataPayload : EventPayloadDefinition<QuestData>
{
    public override string DisplayName => "QuestData";
}
```
*Unity Editor's `TypeCache` automatically detects this definition; `QuestData` will appear instantly in the Inspector dropdown.*

---

## 💾 Optional Save / Load (JSON Snapshot)

```csharp
// 1. Export channels marked as [Saveable] to culture-independent JSON:
string json = EventBoard.ExportStateJson();

// 2. Restore state (missing/invalid fields safely fallback to ScriptableObject defaults):
EventBoard.ImportStateJson(json, notifyListeners: true);
```

---

## 🔍 Visual Inspector & Diagnostics

Open the visual dashboard via:
> **Tools > EventBoard > Hub**

Inspect all active channels, live Blackboard state values, subscriber counts, scene/prefab/ScriptableObject dependency matrix, static audit checks, and test-fire events directly from the editor during Play Mode.

---

## 👥 Credits & Acknowledgments
- **Author:** [Yasin Öztürk](https://github.com/ChasingImagines)
- **AI Pair-Programmer:** Antigravity (Google DeepMind)

## 📄 License
This project is licensed under the [MIT License](LICENSE.md) - created by Yasin Öztürk.
