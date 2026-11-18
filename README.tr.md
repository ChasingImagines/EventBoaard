# EventBoard & Blackboard for Unity (Türkçe Dokümantasyon)

**EventBoard**, Unity projeleri için ScriptableObject tabanlı, tamamen bağımsız (decoupled), tip güvenli (strongly-typed) ve Inspector dostu bir **Event Bus + Global Blackboard (Merkezi Veri Deposu)** mimarisidir.

---

## ❓ Neden Kullanmalısınız?

Klasik Unity projelerinde sistemler büyüdükçe sınıflar birbirine sıkı sıkıya bağlanır (Spagetti Kod):
- UI scripti Oyuncuya, Oyuncu Görev Yöneticisine, Düşman ise UI'a referans verir.
- Sahne değiştiğinde veya bir nesne silindiğinde `NullReferenceException` ve `MissingReference` hataları başlar.

**EventBoard bunu tamamen çözer:**
1. **Sıfır Referans Bağı (Decoupled):** Oyuncu ve UI birbirini tanımaz; ikisi de ortak panoya (EventBoard) bakar.
2. **Hem Olay (Event) Hem Hafıza (Blackboard):** Klasik event sistemleri olayı fırlatır ve unutur. EventBoard ise son değeri hafızada saklar. Geç açılan bir UI menüsü, "Skor kaçtı?" diye sormadan son değeri anında okur.
3. **Tertemiz Yaşam Döngüsü (Fresh State):** Çalışma anındaki tüm değişkenler C# statik belleğinde yaşar; ScriptableObject dosyasına yazılmaz. Oyun durduğunda veriler uçar, oyun yeniden açıldığında her şey tertemiz (varsayılan değerlerle) başlar.
4. **İsteğe Bağlı Save/Load Entegrasyonu (`Export/Import`):** Sadece `Saveable (Kaydedilebilir)` işaretlediğiniz veriler tek satırla JSON snapshot olarak dışarı aktarılır ve geri yüklenir. Eksik/hatalı verilerde sistem çökmez, varsayılan değerleri korur.
5. **Kopmayan Referanslar (GUID Koruması):** Kanal isimlerini değiştirseniz veya başka bir kategoriye taşısanız bile Inspector'daki ve Prefab'lardaki seçimleriniz asla bozulmaz.
6. **Sıfır GC (Garbage Collection):** Generic delegeler ve runtime ID eşleştirmeleriyle bellek çöpü üretmez, drop/takılma yapmaz.

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
- Üst menüden **Tools > EventBoard > Hub** penceresini açın.
- Grubunuza bir ad yazıp (örn: `PlayerEvents`) **"+ Oluştur"** butonuna basın.
- Gerekli klasörler otomatik açılır ve ScriptableObject oluşturulur.
- Inspector'da **"Add Event"** diyerek kanallarınızı tanımlayın (`Health`, `Score` vb.).

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

    private void OnEnable()
    {
        // replayLatest: true sayesinde UI açıldığı an son değeri hemen alır!
        healthKey.Subscribe(UpdateHealthBar, replayLatest: true);
    }

    private void OnDisable()
    {
        healthKey.Unsubscribe(UpdateHealthBar);
    }

    private void UpdateHealthBar(int newHealth) => healthSlider.value = newHealth;
}
```

---


---

## 👥 Emeği Geçenler (Credits)
- **Geliştirici:** [Yasin Öztürk](https://github.com/ChasingImagines)
- **AI Pair-Programmer:** Antigravity (Google DeepMind)

## 📄 Lisans
Bu proje **MIT Lisansı** altında lisanslanmıştır. Yasin Öztürk tarafından geliştirilmiştir.
