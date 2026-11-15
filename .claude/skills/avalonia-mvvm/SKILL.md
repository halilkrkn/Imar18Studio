---
name: avalonia-mvvm
description: Avalonia UI 11, Semi.Avalonia, CommunityToolkit.Mvvm ve SkiaSharp 2D CAD tuvali için MVVM, CommandBus, 60 FPS çizim ve 6 ileri seviye CAD stüdyo özelliğini (Tahsis Okları, Swipe Perde, 5 Sekmeli Canlı Cetvel, OSNAP, Sağ-Tık Menüsü, Koyu/Açık Pafta Teması) uygular.
---
# Avalonia 11 MVVM ve SkiaSharp 2D CAD Kuralları (`avalonia-mvvm`)

1. **Katman ve MVVM Disiplini:**
   - `Imar18.Presentation.Avalonia` içinde iş mantığı veya doğrudan `Infrastructure` çağrısı bulunamaz; tüm mutasyonlar `CommandBus` üzerinden yürütülür.
2. **Referans Tasarım (`docs/UI_UX_Referans_v2_19.html`) ve 6 İleri Seviye Avalonia 11 + SkiaSharp Özelliği:**
   - **1. Kadastro → İmar Tahsis Akış Okları (`Allocation Vectors`):** `SKCanvas.DrawPath` ve `SKPathEffect.CreateDash(new float[]{6, 4}, phase)` kullanarak seçili parselin geldiği kadastro parselinden yeni imar parseline eğrisel tahsis okunu ve `m²` etiketini çiz.
   - **2. Kadastro ↔ İmar Karşılaştırma Perdesi (`Swipe Slider`):** Üst barda `Slider (0..100)` kontrolüyle `SKCanvas.ClipRect` kullanarak eski kadastro sınırları ile yeni imar adalarını canlı perdeyle karşılaştır.
   - **3. 5 Sekmeli Orta Çalışma Alanı (`TabControl`):** `[1. 2D CAD Tuvali]`, `[2. 4'lü Alan & Y1 Dengeleme]`, `[3. Tescile Esas Dağıtım (dm²)]`, `[4. Fen Klasörü (He/M2/Dm2)]` ve `[5. FR.521 Rölöve Krokisi]` sekmeleri arasında anlık geçiş sağla.
   - **4. Sürüklenebilir Kesim Tutamaçları ve Canlı HUD Rozeti:** Parsel kesim çizgileri üzerinde `PointerPressed/PointerMoved` ile çalışan tutamaçlar ve sol altta canlı `Alan (m²) | Tescil Paydası (dm²) | Y1 Yol Dengeleme` rozeti göster.
   - **5. Sağ-Tık CAD Bağlam Menüsü (`ContextMenu`) ve `OSNAP` Çubuğu:** Tuval üzerinde Avalonia `<ContextMenu>` (`45° Köşe Pahı`, `Müşterek Birleştir Md.17/1-d`, `16m Cephe Sabitle`, `Y1 Yol Dengele`) ve alt barda `OSNAP` (`Uç`, `Orta`, `Dik 90°`, `Kesişim`) `ToggleButton` çubuğu sun.
   - **6. Koyu Stüdyo ↔ Açık Pafta Teması (`ThemeVariant`):** `Application.Current.RequestedThemeVariant = ThemeVariant.Dark / ThemeVariant.Light` ile tek tıkla tema geçişi sağla.
3. **Performans ve Asenkronluk:**
   - 50 ms üzeri tüm hesap ve I/O işlemleri `IProgress<T>` ve `CancellationToken` ile arka planda çalışır; UI iş parçacığı asla bloklanmaz.
