# EventBoard & Blackboard for Unity

**EventBoard**, Unity projeleri için ScriptableObject tabanlı, decoupled (bağımsız), tip güvenli (strongly-typed) ve Inspector dostu bir **Event Bus + Global Blackboard (Veri Deposu / State Store)** mimarisidir.

Sahneler ve sistemler arasında hem anlık olay haberleşmesini sağlar hem de paylaşılan verileri (Blackboard) sıfır GC ile saklayıp okumanıza olanak tanır.

---

## 🚀 Öne Çıkan Özellikler

### 1. 📡 Event Bus (Olay Haberleşmesi)
- **Decoupled (Bağımsız) Mimari:** Sınıflar birbirine doğrudan referans vermez.
- **Tip Güvenliği (Type-Safe):** `EventKey<T>` ile hem Inspector üzerinden seçim yapılır hem de kod tarafında tam tip kontrolü sağlanır.
- **VoidSignal Desteği:** Parametresiz sinyaller için sıfır maliyetli tetikleme.

### 2. 🧠 Global Blackboard (Veri Depolama & State Store)
- **Hafızada Son Değer Saklama (`Value`):** Kanallar en son iletilen değeri tutar. Dinleyici olmasanız bile doğrudan `eventKey.Value` ile okuyabilirsiniz.
- **Replay Desteği (`replayLatest`):** Geç doğan (spawn olan) nesneler abone olur olmaz son kaydedilen değeri anında alır.
- **Sessiz Güncelleme (`SetState`):** Olay fırlatmadan sadece veriyi güncelleme veya sadece değer değiştiğinde fırlatma (`SetStateIfChanged`).
- **DefaultValue & Kalıcılık:** ScriptableObject üzerinden varsayılan değer tanımlama ve sahne geçişlerinde veriyi koruma (`IsPersistent`).

### 3. 🛠️ Editör Araçları & Performans
- **Inspector Dropdown:** Arama çubuğu olan şık açılır liste (AdvancedDropdown) ile kolay kanal seçimi.
- **EventBoard Hub Penceresi (`Tools > EventBoard > Hub`):** Tüm event kanallarını, mevcut değerlerini ve dinleyicileri canlı izleme/tetikleme.
- **Sıfır GC (Zero Allocation):** C# generic delegeleri ve runtime ID eşleştirmeleriyle çöp üretmeyen yapı.

---

## 📦 Kurulum (Unity Package Manager / Git URL)

1. **Window > Package Manager** menüsünü açın.
2. Sol üstteki **`+`** butonuna tıklayıp **"Add package from git URL..."** seçin:
   ```text
   https://github.com/<kullanici_adi>/<repo_adi>.git
   ```
*(Yerel test için: **"Add package from disk..."** ile paketin `package.json` dosyasını seçebilirsiniz.)*

---

## 🛠️ Kullanım Kılavuzu

### 1. Event Group (Blackboard) Tanımlama
Proje penceresinde:
> **Create > EventBoard > Event Group (SO)**

Örn: `PlayerBoard.asset` oluşturun ve kanalları ekleyin:
- `Health` (Type: `Integer`, DefaultValue: `100`)
- `PlayerDied` (Type: `None / VoidSignal`)

---

### 2. Blackboard Olarak Kullanım (Veri Okuma / Yazma)

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;

    private void Start()
    {
        // 1. Blackboard'daki mevcut değeri okuma:
        int currentHealth = healthKey.Value;

        // 2. Güvenli okuma:
        if (healthKey.TryGet(out int val))
        {
            Debug.Log($"Sağlık: {val}");
        }

        // 3. Olay fırlatmadan sessizce State güncelleme:
        healthKey.SetState(80, notify: false);

        // 4. Sadece değer değişmişse güncelle ve uyar:
        healthKey.SetStateIfChanged(50, notify: true);
    }
}
```

---

### 3. Event Bus Olarak Kullanım (Tetikleme / Dinleme)

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class HealthUI : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;

    private void OnEnable()
    {
        // replayLatest: true -> Abone olduğu an hafızadaki son değeri hemen fırlatır!
        healthKey.Subscribe(OnHealthChanged, replayLatest: true);
    }

    private void OnDisable()
    {
        healthKey.Unsubscribe(OnHealthChanged);
    }

    private void OnHealthChanged(int currentHealth)
    {
        Debug.Log($"UI Güncellendi: {currentHealth}");
    }
}
```

---

### 4. Parametresiz Sinyaller (VoidSignal)

```csharp
[SerializeField] private EventKey<VoidSignal> onPlayerDied;

// Tetikleme:
onPlayerDied.Raise();

// Dinleme:
onPlayerDied.Subscribe(() => Debug.Log("Oyuncu öldü!"));
```

---

## 🔍 EventBoard Hub

Projedeki tüm kanalları, anlık Blackboard değerlerini ve dinleyicileri izlemek için:
> **Tools > EventBoard > Hub**

---

## 📄 Lisans
MIT License - Yasin Öztürk
