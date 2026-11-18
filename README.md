# EventBoard & Blackboard for Unity

**EventBoard**, Unity projeleri için ScriptableObject tabanlı, decoupled (tamamen bağımsız), tip güvenli (strongly-typed) ve Inspector dostu bir **Event Bus + Global Blackboard (State Store)** mimarisidir.

Sahneler ve sistemler arasında hem anlık olay haberleşmesini sağlar hem de paylaşılan verileri (Blackboard) sıfır GC ile saklayıp okumanıza olanak tanır.

---

## 🚀 Öne Çıkan Özellikler

### 1. 📂 ScriptableObject ile Modüler Gruplama & GUID Koruması
- **Kategorize Gruplar:** Event ve Blackboard değişkenleri tek bir devasa listeye yığılmaz; `PlayerEvents`, `CombatEvents`, `UIEvents` gibi bağımsız `.asset` dosyalarında gruplanır.
- **GUID Koruması (Kopmayan Bağlantılar):** Her kanala arka planda kalıcı bir GUID atanır. Kanalın adını değiştirseniz veya başka bir gruba taşısanız bile **Inspector'daki, sahnelerdeki ve prefab'lardaki bağlantılar ASLA kopmaz**.
- **Otomatik Yükleme (`Resources/EventBoard`):** Grup dosyalarını bu klasöre koyduğunuzda sistem runtime'da hepsini otomatik tanır ve ayağa kaldırır.

### 2. 🧠 Global Blackboard (Veri Depolama & State Store)
- **Hafızada Son Değer Saklama (`Value`):** Kanallar en son iletilen değeri tutar. Dinleyici olmasanız bile doğrudan `eventKey.Value` ile okuyabilirsiniz.
- **Replay Desteği (`replayLatest`):** Geç doğan (spawn olan) nesneler abone olur olmaz son kaydedilen değeri anında alır.
- **Sessiz Güncelleme (`SetState`):** Olay fırlatmadan sadece veriyi güncelleme veya sadece değer değiştiğinde fırlatma (`SetStateIfChanged`).
- **DefaultValue & Kalıcılık:** ScriptableObject üzerinden varsayılan değer tanımlama ve sahne geçişlerinde veriyi koruma (`IsPersistent`).

### 3. 📡 Event Bus (Olay Haberleşmesi) & Sıfır GC
- **Decoupled (Bağımsız) Mimari:** Sınıflar birbirine doğrudan referans vermez; sistemler tamamen birbirinden bağımsız kalır.
- **Tip Güvenliği (Type-Safe):** `EventKey<T>` ile hem Inspector üzerinden seçim yapılır hem de kod tarafında tam tip kontrolü sağlanır.
- **VoidSignal Desteği:** Parametresiz sinyaller için sıfır maliyetli tetikleme.
- **Sıfır GC (Zero Allocation):** C# generic delegeleri ve runtime ID eşleştirmeleriyle çöp üretmeyen yapı.

### 4. 🛠️ Editör Araçları
- **Hiyerarşik Inspector Dropdown:** Kodunuza bir `EventKey` eklediğinizde Inspector'da arama çubuğu olan şık, grup bazlı hiyerarşik bir liste açılır.
- **EventBoard Hub (`Tools > EventBoard > Hub`):** Tüm event gruplarını, anlık Blackboard değerlerini ve dinleyicileri canlı izleme/tetikleme paneli.

---

## 📦 Kurulum (Unity Package Manager / Git URL)

1. **Window > Package Manager** menüsünü açın.
2. Sol üstteki **`+`** butonuna tıklayıp **"Add package from git URL..."** seçin:
   ```text
   https://github.com/<kullanici_adi>/<repo_adi>.git
   ```
*(Yerel test için: **"Add package from disk..."** ile paketin `package.json` dosyasını seçebilirsiniz.)*

---

## 📁 Gruplama Nasıl Yapılır? (Adım Adım)

Gruplama, kod içine string yazarak değil; ScriptableObject varlıkları oluşturarak yapılır:

1. Proje penceresinde `Assets/Resources/EventBoard/` klasörüne gidin (yoksa oluşturun).
2. Sağ tıklayın: **`Create > EventBoard > Event Group (SO)`**
3. Dosyaya grubun adını verin (Örn: `CombatEvents.asset` veya `UIEvents.asset`).
4. Inspector'da gruba ait kanalları ekleyin:
   - **Kanal Adı:** `OnTakeDamage`
   - **Payload Türü:** `Integer`
   - **Default Value:** `0`
   - **Is Persistent:** Sahne geçişinde değer silinmesin istiyorsanız işaretleyin.

Kodunuzda `[SerializeField] private EventKey<int> damageKey;` yazdığınızda Inspector listesinde otomatik olarak şöyle görünür:
```text
▼ CombatEvents
   ├── OnTakeDamage (int)
   └── CriticalHit (int)
▼ PlayerEvents
   ├── Health (int)
   └── Score (int)
```

---

## 🛠️ Kod Kullanım Kılavuzu

### 1. Blackboard Olarak Kullanım (Veri Okuma / Yazma)

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;

    private void Start()
    {
        // 1. Blackboard'daki mevcut değeri doğrudan okuma:
        int currentHealth = healthKey.Value;

        // 2. Güvenli okuma:
        if (healthKey.TryGet(out int val))
        {
            Debug.Log($"Mevcut Sağlık: {val}");
        }

        // 3. Olay fırlatmadan sessizce veriyi güncelleme:
        healthKey.SetState(80, notify: false);

        // 4. Sadece değer değişmişse güncelle ve dinleyicileri uyar:
        healthKey.SetStateIfChanged(50, notify: true);
    }
}
```

---

### 2. Event Bus Olarak Kullanım (Tetikleme / Dinleme)

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class HealthUI : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;

    private void OnEnable()
    {
        // replayLatest: true -> Abone olduğu an hafızadaki son değeri hemen tetikler!
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

### 3. Parametresiz Sinyaller (VoidSignal)

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
