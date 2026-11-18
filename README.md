# EventBoard & Blackboard for Unity

**EventBoard**, Unity projeleri için ScriptableObject tabanlı, decoupled (tamamen bağımsız), tip güvenli (strongly-typed) ve Inspector dostu bir **Event Bus (Olay Haberleşmesi) + Global Blackboard (Merkezi Veri Deposu)** mimarisidir.

---

## ❓ Bu Araç Ne İşe Yarar? Neden Kullanmalıyım?

Klasik Unity projelerinde sistemler büyüdükçe kodlar birbirine dolanır (Spagetti Kod):
- UI scripti Oyuncuya referans verir, Oyuncu Görev Yöneticisine referans verir, Düşman UI'a bağlanmaya çalışır...
- Bir scripti sildiğinizde veya sahneyi değiştirdiğinizde her yer `MissingReferenceException` ve `NullReferenceException` hatalarıyla dolar.

**EventBoard bunu tamamen çözer:**
1. **Sıfır Referans Bağı (Decoupled):** Oyuncu, UI veya Düşman birbirini tanımak zorunda kalmaz. Herkes ortadaki ortak panoya (EventBoard) bakar.
2. **Hem Olay (Event) Hem Hafıza (Blackboard):** Klasik event sistemleri olayı fırlatır ve unutur. EventBoard ise son değeri hafızasında tutar. Yeni doğan bir karakter veya geç açılan bir UI menüsü, panodaki son değeri anında okur.
3. **Tertemiz Yaşam Döngüsü (Fresh State):** Çalışma anındaki tüm değişkenler C# statik belleğinde tutulur; ScriptableObject dosyasına yazılmaz. Oyun durduğunda veriler uçar, oyun yeniden açıldığında her şey tertemiz (SO varsayılanlarıyla) başlar.
4. **İsteğe Bağlı Save/Load Entegrasyonu (`Export/Import`):** Sadece `Saveable (Kaydedilebilir)` işaretlediğiniz veriler tek satırla JSON snapshot olarak dışarı aktarılır ve geri yüklenir. Eksik/hatalı verilerde sistem asla çökmez, SO varsayılanlarını korur.
5. **Kopmayan Referanslar (GUID Koruması):** Kanal isimlerini değiştirseniz veya başka bir kategoriye taşısanız bile Inspector'daki ve Prefab'lardaki seçimleriniz asla bozulmaz.
6. **Sıfır GC (Garbage Collection):** Oyun içinde bellek çöpü üretmez, drop/takılma yapmaz.

---

## 📦 Kurulum (Unity Package Manager)

1. Unity'de üst menüden **Window > Package Manager** penceresini açın.
2. Sol üstteki **`+`** butonuna tıklayın ve **"Add package from git URL..."** seçeneğini seçin.
3. Aşağıdaki Git URL'sini yapıştırıp **Add** butonuna basın:
   ```text
   https://github.com/ChasingImagines/EventBoaard.git
   ```

---

## 🚀 Sıfırdan Adım Adım Kurulum ve Kullanım Rehberi

### Adım 1: Event/Blackboard Grubu Oluşturma (Tamamen Otomatik)

Elle klasör oluşturmanıza **gerek yoktur**, sistem klasörleri arka planda kendisi açar:

- **1. Kolay Yol (Tek Tıkla - Önerilen):**
  - Üst menüden **Tools > EventBoard > Hub** penceresini açın.
  - Grubunuza bir ad yazıp (örn: `PlayerEvents`) **"+ Oluştur"** butonuna basın.
  - Sistem gerekli tüm klasörleri (`Assets/Resources/EventBoard/`) otomatik açar ve grubunuzu içine oluşturur!

- **2. Klasik Yol (Project Penceresinden):**
  - Project penceresinde istediğiniz yerde sağ tıklayın:
    > **Create > EventBoard > Event Group (SO)**
  - Gruba bir isim verin (Örn: `PlayerEvents`, `CombatEvents`, `UIEvents`).

---

### Adım 2: Grup İçine Neler Tanımlayabilirim?

Oluşturduğunuz `.asset` dosyasına tıklayın. Inspector penceresinde **"Add Event"** butonuna basarak içine kanallar ekleyin:

| Kanal Ayarı | Ne İşe Yarar? | Örnek |
| :--- | :--- | :--- |
| **Event Name** | Kanalın adı | `PlayerHealth`, `Score` |
| **Payload Definition** | Kanalın taşıyacağı veri tipi | `Integer`, `Float`, `Boolean`, `String` veya `Void (Parametresiz)` |
| **Default Value** | Oyun başındaki varsayılan Blackboard değeri | Sağlık için `100`, Para için `0` |
| **💾 Saveable** | İsteğe bağlı Save/Load çıktısına (JSON) dahil edilsin mi? | Skor için işaretleyin; anlık zıplama sinyali için boş bırakın |
| **Description** | Geliştirici açıklama notu | "Oyuncunun anlık can değeri." |

---

### Adım 3: C# Kodunda Nasıl Kullanılır?

Kod yazarken sınıfınızın en üstüne namespace'i ekleyin:
```csharp
using Yasin.EventBoard;
```

#### Senaryo A: Blackboard Olarak Kullanım (Veri Okuma ve Yazma)
```csharp
using UnityEngine;
using Yasin.EventBoard;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;

    private void Start()
    {
        // 1. Panodaki mevcut değeri doğrudan oku (Sıfır GC):
        int currentHealth = healthKey.Value;

        // 2. Panodaki değeri sessizce güncelle (Event fırlatmadan):
        healthKey.SetState(80, notify: false);

        // 3. Değer değiştiyse güncelle ve tüm dinleyicileri uyar:
        healthKey.SetStateIfChanged(50, notify: true);
    }

    public void TakeDamage(int damage)
    {
        // Değeri doğrudan yeni haliyle tetikle:
        healthKey.Raise(healthKey.Value - damage);
    }
}
```

#### Senaryo B: Event Dinleme (UI Scriptleri)
```csharp
using UnityEngine;
using UnityEngine.UI;
using Yasin.EventBoard;

public class HealthUI : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;
    [SerializeField] private Slider healthSlider;

    private void OnEnable()
    {
        // replayLatest: true sayesinde UI açıldığı an panodaki en güncel can değerini anında alır!
        healthKey.Subscribe(UpdateHealthBar, replayLatest: true);
    }

    private void OnDisable()
    {
        healthKey.Unsubscribe(UpdateHealthBar);
    }

    private void UpdateHealthBar(int newHealth)
    {
        healthSlider.value = newHealth;
    }
}
```

#### Senaryo C: Verisiz Sinyaller (VoidSignal)
```csharp
[SerializeField] private EventKey<VoidSignal> onGameStarted;

// Tetikleme:
onGameStarted.Raise();

// Dinleme:
onGameStarted.Subscribe(() => Debug.Log("Oyun Başladı!"));
```

---

---

## 🧩 Kendi Özel Veri Tiplerinizi Tanımlama (Custom Types)

EventBoard; `int`, `float`, `string`, `bool`, `Vector2`, `Vector3`, `Color`, `GameObject`, `Transform` gibi tipleri dahili olarak destekler. Ancak projenize özel `struct` veya `class` veri tiplerini (örn: `QuestData`, `InventoryItem`, `DamageInfo`) eklemek de son derece kolaydır:

### 1. Veri Tipinizi Oluşturun:
```csharp
using System;

[Serializable]
public struct QuestData
{
    public string questId;
    public int targetCount;
    public bool isCompleted;
}
```

### 2. EventBoard İçin Payload Tanımını Ekleyin:
```csharp
using System;
using Yasin.EventBoard;

[Serializable]
public sealed class QuestDataPayload : EventPayloadDefinition<QuestData>
{
    public override string DisplayName => "QuestData";
}
```

> **Nasıl Çalışır?** Paketin hiçbir koduna dokunmanıza gerek yoktur. Unity Editörü (`TypeCache`) bu sınıfı otomatik keşfeder ve Inspector'daki tip seçici açılır listesinde `QuestData` anında görünür!
> 
> Kodunuzda doğrudan `EventKey<QuestData>` olarak tip güvenliğiyle kullanabilirsiniz:
> ```csharp
> [SerializeField] private EventKey<QuestData> questKey;
> 
> void UpdateQuest()
> {
>     questKey.Raise(new QuestData { questId = "q1", targetCount = 3 });
>     QuestData current = questKey.Value;
> }
> ```

## 💾 İsteğe Bağlı Save / Load (JSON Snapshot)

Eğer projenizde Blackboard verilerini diske kaydetmek isterseniz:
Sistem dosya yönetimiyle uğraşmaz; sadece JSON çıktısı verir ve alır:

```csharp
// 1. Sadece [Saveable] işaretli kanalların anlık değerlerini JSON olarak alır:
string json = EventBoard.ExportStateJson();
// Bu string'i kendi Save Sisteminize (PlayerPrefs, Dosya, Bulut vb.) verin.

// 2. Kaydedilen veriyi geri yükler (Hatalı/eksik alanlarda DefaultValue korunur):
EventBoard.ImportStateJson(json, notifyListeners: true);
```

---

## 🔍 Görsel Kontrol Merkezi (EventBoard Hub)

Tüm projede hangi eventler var, hafızadaki değerleri ne ve o kanalı kaç kişi dinliyor görmek için:
> **Tools > EventBoard > Hub**

---

## 📄 Lisans
Bu proje **MIT Lisansı** altında lisanslanmıştır. Yasin Öztürk tarafından geliştirilmiştir. İstediğiniz ticari ve açık kaynak projelerde özgürce kullanabilirsiniz.
