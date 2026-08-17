# Toplu geliştirme

Bu dalın iş listesi. Sıra: önce yanlışlar, sonra eksikler, sonra eklenecekler.
Bir madde bitince buradan çizilir. `main`’e ancak madde tek başına review edilebilir olunca gider.

## Yanlış / yarım

1. Ayarlar düğmesi duruyor, komutu yok. Tıklanınca bir şey olmamalı; `aphelion://settings` açılmalı.
2. Sekme değiştirmek, koparmak ve split’in diğer yarısını açmak sayfayı yeniden yüklüyor. Motor yüzeyi her attach’te sıfırdan kuruluyor; scroll, form ve oynayan medya kayboluyor.
3. Gizli pencere ayrı profil değil: motor aynı depo, kapanınca çerez silme denemesi. Gerçek isolation yok.
4. İndirme köprüsü ve HTML fullscreen yalnızca Windows / WebView2. Linux ve macOS’ta ikisi de düşüyor.
5. Oturum yalnızca ana pencereyi yazıyor. Koparılmış pencereler restart’ta yok.
6. Grup çipi sürüklenerak grubun tamamı taşınamıyor.
7. Adres çubuğu düz metin: şema/host vurgusu yok, kilit ikonu yok, arama motoru rozeti yok.
8. Sekme menüsünde pin, sessize al, çoğalt, kapatılanı geri aç, adresi kopyala yok.
9. Sayfada sağ tık Aphelion menüsü değil; WebView2’nin kendi menüsü (veya hiçbiri). Geri / ileri / yenile, bağlantıyı yeni sekmede aç, seçileni kopyala / ara yok.
10. Masaüstü otomatik testi yok. Domain kuralları (sekme, grup, split, adres) korumasız.
11. Linux ve macOS derlemesi / çalışması doğrulanmamış.
12. `current-state.md` eski: yer imi, indirme, gizli pencere artık var; belge hâlâ yok diyor.

## Eksik (tarayıcı olarak durmalı)

13. Ayarlar sayfası (`aphelion://settings`): tema, başlangıç (oturumu geri yükle / belirli sayfalar / New Tab), varsayılan arama motoru, yeni sekme widget’ları, gizlilik (hava durumu, öneri), indirme klasörü. Kalıcı, Domain’den dışarı katman katman.
14. Geçmiş (`aphelion://history`): ziyaretler, arama, günlere göre liste, silme, Ctrl+H. Oturum kaydından ayrı.
15. Sayfada bul: Ctrl+F, sayaç, sonraki / önceki, Esc. Motor köprüsü gerekir.
16. Kapatılan sekmeyi geri aç: Ctrl+Shift+T, bir yığın kadar.
17. Sekmeyi sabitle, sessize al, çoğalt.
18. Yazdır (Ctrl+P) ve sayfa kaynağı / DevTools (en azından Windows’ta motorun penceresi).
19. İlk açılış: profil, arama motoru, görünüm için kısa onboarding. Ayarlar olmadan kalıcı olmaz.

## Eklenecek

20. Komut paleti (Ctrl+K): yeni sekme, ayarlar, sayfada bul, yenile, tema, indirmeler, geçmiş.
21. Tema seçimi: palet Light/Dark duruyor, kullanıcıya geçiş yok; sistem `Default`’una bağlı.
22. Omnibox: URL parçalarını boya, arama / site ayrımını göster, önerileri adres çubuğuna da taşı.
23. Site izinleri: çerez, konum, bildirim, kamera — en azından sayfa bilgisi popover’ı.
24. Kayıtlı şifre / otomatik doldurma. Ayrı güvenlik kararı; acele edilmez.
25. Varsayılan tarayıcı yap ve güncelleme kanalı.
26. İndirme: duraklat / devam / iptal her platformda; macOS / Linux köprüsü.
27. Masaüstü test projesi (`desktop/tests/`): adres çözümleme, sekme/grup/split, iç sayfa geçmişi.
28. Motor ADR takibi: indirme, context menu, tab lifecycle üç platformda spike.
29. Mobil ve paylaşılan kontratlar: masaüstü davranışları oturunca, `shared/product` doldurulur. Bu dalda mobil kod yazılmaz.
