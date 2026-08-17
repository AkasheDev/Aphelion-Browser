# Toplu geliştirme

Bu dalın iş listesi. Sıra: önce yanlışlar, sonra eksikler, sonra eklenecekler.
Bir madde bitince buradan çizilir. `main`’e ancak madde tek başına review edilebilir olunca gider.

## Yanlış / yarım

1. ~~Ayarlar düğmesi duruyor, komutu yok.~~ `aphelion://settings` açılıyor.
2. ~~Sekme değiştirmek ve split sayfayı yeniden yüklüyor.~~ Görünüm havuzda kalıyor; Unloaded dispose yok. Koparma hâlâ native view taşıyamadığı için reload — platform sınırı.
3. ~~Gizli pencere ayrı profil değil.~~ Pencereye özel UserDataFolder + kapanınca silme; InPrivate / NonPersistentDataStore.
4. ~~İndirme ve HTML fullscreen yalnızca Windows.~~ Windows COM + diğerlerinde HttpClient / sayfa kuyruğu.
5. ~~Oturum yalnızca ana pencereyi yazıyor.~~ Bütün sıradan pencereler `session.json` içinde.
6. ~~Grup çipi sürüklenerak grubun tamamı taşınamıyor.~~ `TabStripDragHandler` zaten `DropGroup` ile taşıyor.
7. ~~Adres çubuğu düz metin.~~ Kilit / site bilgisi, Search/Site rozeti, öneri listesi.
8. ~~Sekme menüsünde pin, sessize al, çoğalt, kapatılanı geri aç, adresi kopyala yok.~~
9. ~~Sayfada sağ tık Aphelion menüsü değil.~~ Sayfa `contextmenu` chrome menüsüne düşüyor.
10. ~~Masaüstü otomatik testi yok.~~ `desktop/tests/` xunit (ADR-0002).
11. ~~Linux ve macOS derlemesi doğrulanmamış.~~ `.github/workflows/desktop.yml` üç OS’ta `dotnet build` / `test`.
12. ~~`current-state.md` eski.~~ Yerel dosya güncellendi (yayınlanan repoda gitignore).

## Eksik (tarayıcı olarak durmalı)

13. ~~Ayarlar sayfası (`aphelion://settings`).~~
14. ~~Geçmiş (`aphelion://history`), Ctrl+H.~~
15. ~~Sayfada bul: Ctrl+F.~~
16. ~~Kapatılan sekmeyi geri aç: Ctrl+Shift+T.~~
17. ~~Sekmeyi sabitle, sessize al, çoğalt.~~
18. ~~Yazdır (Ctrl+P) ve DevTools (Windows).~~
19. ~~İlk açılış onboarding.~~

## Eklenecek

20. ~~Komut paleti (Ctrl+K).~~
21. ~~Tema seçimi Light/Dark/System.~~
22. ~~Omnibox: kilit, rozet, öneriler.~~
23. ~~Site izinleri popover.~~
24. ~~Kayıtlı şifre UI (otomatik doldurma yok; cihaz içi liste).~~
25. ~~Varsayılan tarayıcı ayarları + güncelleme kanalı.~~
26. ~~İndirme duraklat / devam / iptal her platformda (HttpClient yedek).~~
27. ~~Masaüstü test projesi.~~
28. ~~Motor ADR takibi: ADR-0002.~~
29. ~~`shared/product` dolduruldu. Bu dalda mobil kod yok.~~
