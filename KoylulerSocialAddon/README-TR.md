# Köylüler Sosyal Yaşam 0.1 — İlk deneysel düzeltme

Bu ayrı küçük `.scmod`, Köylüler Modu 0.2.14.7 ile birlikte çalışmak üzere hazırlandı.

Ana moddaki LivingTowns.dll, KoylulerModu.dll, çiftçi, envanter, ticaret, kapılar ve yataklar değiştirilmez.
Yalnızca **yerel denetim bir köylü için TickPatrol çalıştırdığında** 2,5–25 blokluk boş zaman yürüyüşünü ayarlar.

Önemli hata düzeltmesi: Eski yatak-gezinme yaması yatağı bulunmayan köylünün patrulünü tamamen iptal edebiliyordu. Artık iptal etmez. Köylünün iş merkezi, yalnızca TickPatrol içindeyken yatakla değiştirilir; normal iş yürüyüşü korunur.

**İletişim ve animasyonlar:** Asıl LivingTowns.dll'de TryStartConversation, BeginConversation, EndConversation ve SetTalkingAnimation zaten mevcuttur. Bu sürüm onların tetiklenme olasılığını, köylülerin dolaşıp karşılaşmasını kolaylaştırarak dolaylı artırmayı hedefler. Gerçek yeni diyalog ve animasyon çeşitliliği henüz uygulanmadı.

İlk test: Yeni oyun dünyasında 2–3 köylüyü yakın evlere yerleştir; gündüz bir süre izle. Yürüme, birbirine yaklaşma ve konuşma gerçekleşirse ekran kaydı gönder. Gerekirse sonraki sürümde ayrı konuşma davranışı geliştirilecek.

Android oyun testi yapılmamıştır. Herhangi sorun olursa yalnızca bu ek modu kaldır; ana mod aynı kalır.
