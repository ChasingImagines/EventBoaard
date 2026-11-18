# EventBoard & Blackboard for Unity

**EventBoard**, Unity projeleri için ScriptableObject tabanlı, decoupled (tamamen bağımsız), tip güvenli (strongly-typed) ve Inspector dostu bir **Event Bus (Olay Haberleşmesi) + Global Blackboard (Merkezi Veri Deposu)** mimarisidir.

---

## ❓ Bu Araç Ne İşe Yarar? Neden Kullanmalıyım?

Klasik Unity projelerinde sistemler büyüdükçe kodlar birbirine dolanır (Spagetti Kod):
- UI scripti Oyuncuya referans verir, Oyuncu Görev Yöneticisine referans verir, Düşman UI'a bağlanmaya çalışır...
- Bir scripti sildiğinizde veya sahneyi değiştirdiğinizde her yer `MissingReferenceException` ve `NullReferenceException` hatalarıyla dolar.

**EventBoard bunu tamamen çözer:**
1. **Sıfır Referans Bağı (Decoupled):** Oyuncu, UI veya Düşman birbirini tanımak zorunda kalmaz. Herkes ortadaki ortak panoya (EventBoard) bakar.
2. **Hem Olay (Event) Hem Hafıza (Blackboard):** Klasik event sistemleri olayı fırlatır ve unutur. EventBoard ise son değeri hafızasında tutar. Yeni doğan bir karakter veya geç açılan bir UI menüsü, "Skor kaçtı?" diye aramak yerine panodaki son değeri anında okur.
3. **Kopmayan Referanslar (GUID Koruması):** Kanal isimlerini değiştirseniz veya başka bir kategoriye taşısanız bile Inspector'daki ve Prefab'lardaki seçimleriniz asla bozulmaz.
4. **Sıfır GC (Garbage Collection):** Oyun içinde bellek çöpü üretmez, drop/takılma yapmaz.

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

### Adım 3: Grup İçine Neler Tanımlayabilirim?

Oluşturduğunuz `.asset` dosyasına tıklayın. Inspector penceresinde **"Add Event"** butonuna basarak içine kanallar ekleyin:

| Kanal Ayarı | Ne İşe Yarar? | Örnek |
| :--- | :--- | :--- |
| **Event Name** | Kanalın adı | `PlayerHealth`, `OnLevelCompleted` |
| **Payload Definition** | Kanalın taşıyacağı veri tipi | `Integer`, `Float`, `Boolean`, `String` veya `Void (Parametresiz)` |
| **Default Value** | Oyun başındaki varsayılan Blackboard değeri | Sağlık için `100`, Para için `0` |
| **Is Persistent** | Sahne değiştiğinde değer korunsun mu? | Seviye arası taşınacak veriler için işaretleyin |
| **Description** | Takım arkadaşlarınız veya sizin için açıklama notu | "Oyuncunun anlık can değeri." |

---

### Adım 4: C# Kodunda Nasıl Kullanılır?

Kod yazarken sınıfınızın en üstüne namespace'i ekleyin:
```csharp
using Yasin.EventBoard;
```

#### Senaryo A: Blackboard Olarak Kullanım (Veri Yazma ve Okuma)
Herhangi bir script, doğrudan panodaki değeri okuyabilir veya güncelleyebilir:

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class PlayerController : MonoBehaviour
{
    // Inspector'da açılır listeden kanal seçilir
    [SerializeField] private EventKey<int> healthKey;

    private void Start()
    {
        // 1. Panodaki mevcut değeri doğrudan oku (Sıfır GC):
        int currentHealth = healthKey.Value;
        Debug.Log($"Başlangıç Canı: {currentHealth}");

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

#### Senaryo B: Event Dinleme (UI veya Takipçi Scriptler)
UI scriptiniz hiçbir yere referans vermeden panoyu dinler:

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
        // Bellek sızıntısını önlemek için dinlemeyi bırak:
        healthKey.Unsubscribe(UpdateHealthBar);
    }

    private void UpdateHealthBar(int newHealth)
    {
        healthSlider.value = newHealth;
    }
}
```

#### Senaryo C: Verisiz Sinyaller (VoidSignal)
Sadece bir olayın gerçekleştiğini bildirmek için (`GameStarted`, `PlayerDied`, `PauseGame` vb.):

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class GameManager : MonoBehaviour
{
    [SerializeField] private EventKey<VoidSignal> onGameStarted;

    public void StartGame()
    {
        // Sinyali tetikle:
        onGameStarted.Raise();
    }
}
```

---

## 🔍 Görsel Kontrol Merkezi (EventBoard Hub)

Tüm projede hangi eventler var, çalışma anında (Play Mode) hafızadaki değerleri ne ve o kanalı kaç kişi dinliyor görmek için:
> **Tools > EventBoard > Hub**

Bu pencereden:
- Projedeki tüm ScriptableObject gruplarını listeleyebilir,
- Canlı olarak Blackboard değerlerini izleyebilir,
- Test amaçlı Inspector'dan butonla manuel event tetikleyebilirsiniz.

---

## 📄 Lisans
Bu proje **MIT Lisansı** altında lisanslanmıştır. Yasin Öztürk tarafından geliştirilmiştir. İstediğiniz ticari ve açık kaynak projelerde özgürce kullanabilirsiniz.
