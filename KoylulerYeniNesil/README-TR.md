# Köylüler Yeni Nesil — V0.1 / Test Sürümü

### Bağımsız mod altyapısı

Bu mod **eski LivingTowns.dll veya KoylulerModu.dll dosyalarını içermez**.
Önceki 0.2.14.7 modundan yalnızca kullanıcının sağladığı 3D karakter modelleri,
animasyon JSON dosyaları ve karakter tanımlarının vanilla özellikleri referans
alınmıştır. Karakter yapay zekâsı Game.ComponentYeniNesilVillager olarak
sıfırdan yazılmıştır.

### İlk sürüm davranışları

- Kendi bulunduğu 30 blok yarıçaplı bölgede 5–22 blok uzakta gezinti hedefi arar.
- Başarısız hedefleri ve 6,5 saniyeyi aşan ilerlememe durumunu algılayıp yeniden dener.
- Var olan Survivalcraft ComponentPathfinding rotasını kullanır.
- Yakın iki köylü birbirine yönelip 3–6 saniye sohbete girer; ardından tekrar gezinir.
- Orijinal animasyon ayarlarında tanımlı IsTalking parametresini ve SpeedAbs yürüyüş
  koşullarını kullanır; ilk üç karakterde konuşma animasyonu tanımlıdır.
- Dokuz köylü tipi için de basit gezinme beynini kullanır.

### Bilinen eksikler

- Ev, yatak, iş ve meslek davranışları henüz uygulanmadı.
- Konuşma yazıları, ticaret ve köy yerleşimiyle otomatik bağlantı yok.
- Diyalog sadece sosyal animasyon/karşılıklı yöneliş; sesli konuşma değil.
- Android'de gerçek yürüyüş ve varlık yüklenmesi henüz test edilmedi.
- Dönüşler/kapılar/merdivenler özellikle köyün içinde doğrulanmalı.
- Gece özel uyku rutini yok.

### Kurulum

Eski **Köylüler Modu** sürümlerini ve **Sosyal Yaşam 0.1** eklentisini
devre dışı bırak. Bu tek yeni `.scmod` dosyasını yükle.
Değerli kaydı kullanma; **yeni test dünyası** aç.
Yaratıcı envanterde Köylüler Yeni Nesil çağırma yumurtalarını arayıp
2–3 köylü oluştur; açık alanda 1 dakika gözlemle.

### Test sonucu ayrımı

- GLB/JSON model ve animasyon eşleştirme: bağımsız varlık kontrolleri
- C# derleme: GitHub Actions raporuna bakılmalı
- SCAPI gerçek Android davranışı: kullanıcının oyundaki testi gerekir
