# EventBoard & Blackboard for Unity (Türkçe Dokümantasyon)

**EventBoard**, Unity projeleri için ScriptableObject tabanlı, tamamen bağımsız (decoupled), tip güvenli (strongly-typed) ve Inspector dostu bir **Event Bus + Global Blackboard (Merkezi Veri Deposu)** mimarisidir.

---

## ❓ Neden Kullanmalısınız?

Klasik Unity projelerinde sistemler büyüdükçe sınıflar birbirine sıkı sıkıya bağlanır (Spagetti Kod):
- UI scripti Oyuncuya, Oyuncu Görev Yöneticisine, Düşman ise UI'a referans verir.
- Sahne değiştiğinde veya bir nesne silindiğinde `NullReferenceException` ve `MissingReference` hataları başlar.

**EventBoard v2.0 bunu tamamen çözer:**
1. **Sıfır Referans Bağı (Decoupled):** Oyuncu ve UI birbirini tanımaz; ikisi de ortak panoya (EventBoard) bakar.
2. **Sıfır Sahne Kurulumu & Özgür Klasör Yapısı:** `EventBoardGroup` ScriptableObject varlıklarınızı `Assets/` altında istediğiniz herhangi bir klasöre koyabilirsiniz. `Resources` klasörü veya sahneye yönetici objesi koyma zorunluluğu yoktur; `EventBoardRegistry` Unity'nin `PreloadedAssets` mekanizması ve `IPreprocessBuildWithReport` ile build öncesi otomatik senkronize edilir.
3. **Hem Olay (Event) Hem Hafıza (Blackboard):** Klasik event sistemleri olayı fırlatır ve unutur. EventBoard ise son değeri hafızada saklar. Geç açılan bir UI menüsü, "Skor kaçtı?" diye sormadan son değeri anında okur.
4. **Tertemiz Yaşam Döngüsü (Fresh State):** Çalışma anındaki tüm değişkenler C# statik belleğinde yaşar; ScriptableObject dosyasına yazılmaz. Oyun durduğunda veriler uçar, oyun yeniden açıldığında her şey tertemiz (varsayılan değerlerle) başlar.
5. **Kopmayan Referanslar (GUID Koruması):** Kanal isimlerini değiştirseniz veya başka bir kategoriye taşısanız bile Inspector'daki, Prefab'lardaki ve ScriptableObject'lerdeki seçimleriniz asla bozulmaz. Çoğaltılan (`Ctrl+D`) grup dosyalarının GUID'leri otomatik yenilenir.
6. **Sıfır GC & Re-Entrancy Güvenliği:** Generic delegeler ve runtime ID eşleştirmeleriyle bellek çöpü üretmez; iç içe (re-entrant) `Publish` çağrılarında hiçbir aboneyi atlamaz.
7. **Sahip Takibi & `IDisposable` Abonelik:** `EventSubscription` token'ı (`Dispose()`), closure üretmeyen parametresiz `Subscribe(Action)` ve lambda/closure kullanımları için sahip takibi (`key.Subscribe(this, val => ...)`) sunar.
8. **Kültürden Bağımsız Save/Load (`Export/Import`):** Sadece `Saveable (Kaydedilebilir)` işaretlediğiniz veriler kültür/dil farkından etkilenmeyen (`tr-TR`, `en-US` vb.) JSON snapshot olarak dışarı aktarılır ve geri yüklenir. Eksik/hatalı verilerde sistem varsayılan değerlere döner.

---

## 📦 Kurulum (Unity Package Manager)

1. Unity'de **Window > Package Manager** penceresini açın.
2. Sol üstteki **`+`** butonuna tıklayın ve **"Add package from git URL..."** seçin.
3. Aşağıdaki Git URL'sini yapıştırıp **Add** butonuna basın:
   ```text
   https://github.com/ChasingImagines/EventBoard.git
   ```

---

## 🚀 Hızlı Başlangıç

### 1. Kanal / Grup Oluşturma (Tam Otomatik)
- Üst menüden **Tools > EventBoard > Hub** penceresini açın (veya `Assets/` altında istediğiniz herhangi bir klasörde sağ tıklayıp **Create > EventBoard > Event Group (SO)** seçin).
- Grubunuza bir ad yazıp (örn: `PlayerEvents`) **"+ Oluştur"** butonuna basın.
- `EventBoardRegistry` projedeki tüm grupları otomatik olarak toplar ve `PlayerSettings > Preloaded Assets` listesine kaydeder — sahneye herhangi bir yönetici koymanız gerekmez.
- Inspector'dan kanallarınızı tanımlayın (`Health`, `Score` vb.).

### 2. C# Kodunda Kullanım

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private EventKey<int> healthKey;

    private void Start()
    {
        // Blackboard'daki mevcut değeri oku:
        int currentHealth = healthKey.Value;

        // Olay fırlatmadan sessizce güncelle:
        healthKey.SetState(80, notify: false);

        // Değer değiştiyse güncelle ve uyar:
        healthKey.SetStateIfChanged(50, notify: true);
    }
}
```

### 3. UI Tarafında Dinleme

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
        // replayLatest: true sayesinde UI açıldığı an son değeri hemen alır!
        // Sahip (this) ve IDisposable token ile lambda kullanımlarında dahi sızıntı olmaz:
        _subscription = healthKey.Subscribe(this, newHealth => healthSlider.value = newHealth, replayLatest: true);
    }

    private void OnDisable()
    {
        _subscription.Dispose();
    }
}
```

### 4. Parametresiz Sinyaller ve Dinleyiciler

`Subscribe(Action)` hem `EventKey<VoidSignal>` hem de herhangi bir `EventKey<T>` üzerinde closure tahsisi olmadan çalışır:

```csharp
[SerializeField] private EventKey<VoidSignal> onGameStarted;

// Tetikle:
onGameStarted.Raise();

// Parametresiz dinle:
onGameStarted.Subscribe(HandleGameStarted);
```

---

## 🧩 Özel Veri Tipleri (Extensibility)

Varsayılan tiplerin (`int`, `float`, `string`, `bool`, `Vector2`, `Vector3`, `Color`, `GameObject`, `Transform`) yanında kendi `struct` veya `class` tiplerinizi de ekleyebilirsiniz:

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

---

## 💾 Opsiyonel Save / Load (JSON Snapshot)

```csharp
// Saveable işaretli kanalları dışa aktar (tr-TR / en-US fark etmeksizin güvenlidir):
string json = EventBoard.ExportStateJson();

// Geri yükle (eksik veya bozuk kanallar otomatik DefaultValue değerine döner):
EventBoard.ImportStateJson(json, notifyListeners: true);
```

- **Sıfır Bağımlılık (Varsayılan):** `Newtonsoft.Json` yoksa `UnityJsonSerializer` (`JsonUtility`) üzerinden çalışır.
- **Otomatik `Newtonsoft.Json` Desteği:** Projenizde `com.unity.nuget.newtonsoft-json` paketi varsa, runtime assembly `versionDefines` ile `EVENTBOARD_NEWTONSOFT_JSON` tanımlar ve `NewtonsoftEventBoardSerializer`'ı otomatik devreye alır. Böylece `Dictionary<K,V>`, `HashSet<T>`, `DateTime`, `Guid`, nullable tipler ve C# property'ler doğrudan kaydedilebilir; dahili Unity struct converter'ları (`Vector2/3/4`, `Quaternion`, `Color/32`, `Rect`, `Bounds`) sonsuz döngü hatalarını önler ve `JsonUtility` formatıyla uyumluluğu korur.
- **Özel Serializer:** İsterseniz `EventBoard.Serializer` üzerinden kendi `IEventBoardSerializer` uygulamanızı (veya `UnityJsonSerializer.Instance`) atayabilirsiniz.

---

## 🔍 Görsel Tanılama ve Denetim (Tools > EventBoard > Hub)

**Tools > EventBoard > Hub** penceresi üzerinden canlı Blackboard değerlerini, aktif aboneleri, sahne/prefab/ScriptableObject bağlantı matrisini, statik denetim (Audit) raporlarını ve Zero-GC performans testlerini çalıştırabilirsiniz.

---

## 👥 Emeği Geçenler (Credits)
- **Geliştirici:** [Yasin Öztürk](https://github.com/ChasingImagines)
- **AI Pair-Programmer:** Antigravity (Google DeepMind)

## 📄 Lisans
Bu proje **MIT Lisansı** altında lisanslanmıştır. Yasin Öztürk tarafından geliştirilmiştir.
