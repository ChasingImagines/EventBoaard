# EventBoard for Unity

**EventBoard**, Unity projeleri için ScriptableObject tabanlı, decoupled (bağımsız), tip güvenli (strongly-typed) ve Inspector dostu bir event mimarisidir.

---

## 🚀 Özellikler

- **Sıfır Bağımlılık:** Harici hiçbir kütüphaneye veya pakete ihtiyaç duymaz.
- **Tip Güvenliği (Type-Safe):** `EventKey<T>` ile hem Inspector üzerinden seçim yapabilir hem de kod tarafında tam tip güvenliği elde edersiniz.
- **Payload Desteği:** Parametresiz sinyaller için `VoidSignal`, veri taşıyan eventler için jenerik tipler (`int`, `float`, `string` veya özel `struct`/`class`).
- **Gelişmiş Editör ve Arama:** Unity Inspector'da arama destekli açılır liste (AdvancedDropdown) ve tüm event kanallarını izleyebileceğiniz görsel **EventBoard Hub** penceresi.
- **Performans & Zero-GC:** Optimize edilmiş callback dağıtımı.

---

## 📦 Kurulum (Unity Package Manager / Git URL)

Unity projenizde:
1. **Window > Package Manager** menüsünü açın.
2. Sol üstteki **`+`** butonuna tıklayın ve **"Add package from git URL..."** seçin.
3. GitHub deponuzun URL'sini yapıştırın:
   ```text
   https://github.com/<kullanici_adi>/<repo_adi>.git
   ```
*(Alternatif olarak yerel diskten test etmek için: **"Add package from disk..."** seçip paketin içindeki `package.json` dosyasını seçebilirsiniz.)*

---

## 🛠️ Hızlı Başlangıç

### 1. Event Group Oluşturma
Proje penceresinde sağ tıklayın:
> **Create > EventBoard > Event Group (SO)**

Örneğin `GameEvents.asset` adında bir grup oluşturun ve içerisine kanallar ekleyin:
- `GameStarted` (Payload Type: `None / VoidSignal`)
- `PlayerScoreChanged` (Payload Type: `Integer`)

### 2. Kodda Dinleme (Subscribe)

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class ScoreUI : MonoBehaviour
{
    [SerializeField] private EventKey<int> onScoreChanged;

    private void OnEnable()
    {
        onScoreChanged.Subscribe(HandleScoreChanged);
    }

    private void OnDisable()
    {
        onScoreChanged.Unsubscribe(HandleScoreChanged);
    }

    private void HandleScoreChanged(int newScore)
    {
        Debug.Log($"Yeni Skor: {newScore}");
    }
}
```

### 3. Kodda Tetikleme (Raise)

```csharp
using UnityEngine;
using Yasin.EventBoard;

public class ScoreManager : MonoBehaviour
{
    [SerializeField] private EventKey<int> onScoreChanged;

    public void AddScore(int amount)
    {
        onScoreChanged.Raise(amount);
    }
}
```

### 4. Parametresiz Sinyaller (VoidSignal)

```csharp
[SerializeField] private EventKey<VoidSignal> onGameStarted;

// Tetikleme:
onGameStarted.Raise();

// Dinleme:
onGameStarted.Subscribe(() => Debug.Log("Oyun Başladı!"));
```

---

## 🔍 EventBoard Hub

Projedeki tüm event gruplarını ve aboneleri tek bir pencereden incelemek için:
> **Tools > EventBoard > Hub**

---

## 📄 Lisans
MIT License
