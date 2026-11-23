# Köylüler Modu 0.2.14.8 – Gerçek boş zaman gezintisi

Bu düzeltme doğrudan ana Köylüler Modu paketine eklenecek ayrı DLL'dir.
0.2.14.7 dosyasındaki LivingTowns.dll, KoylulerModu.dll, animasyon,
model, ses, köylü veritabanı ve çiftçi sistemlerine dokunmaz.

**V0.1 gezinti eklentisi neden başarısız olmuş olabilir?**
Önceki eklenti yalnızca `TickPatrol` çağrısının yarıçapını düzenliyordu.
Ana yapay zekâ `TickPatrol` çağırmadığında köylü kapıda durabilir.

**Bu sürümün farkı:** Ana `Update(float)` sonrasında, ancak gündüz ve
köylü gerçek anlamda boşta olduğunda native `SetManagedDestination`
metoduyla 7-16 blok yakındaki geçerli bir noktaya hedef verir.
İşe gitme, gündüz iş hedefi, home exit, kapı, geceleri uyku,
konuşma ve tehlike/iş bayrakları sırasında yeni hedef atamaz.
Mevcut hareket hedefi aktifken onu normalde bozmadan bekler.
Yalnızca boşta takılmış hedef 14 saniyedir ilerlemiyorsa yeniler.

`[KoylulerRoam]` logları devreye girme, meşguliyet ve
uygun hedef bulunamama sebeplerini raporlar.
Yeni animasyonlar ve doğrudan sohbet hareketleri bu sürümde yok;
öncelik sahici dolaşmayı doğrulamak.

Test: Önce eski Sosyal Yaşam 0.1 eklentisini **devre dışı bırak**.
Ana modun yalnızca güncellenmiş tek paketi yüklü olsun.
Değerli kayıtlarda değil ayrı test dünyasında kullan.

Derleme başarılı olsa bile Android'de davranışın çalıştığı
ayrı oyun testiyle doğrulanmalıdır.
