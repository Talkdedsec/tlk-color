# DEVAM

NVIDIA renk ayarı benzeri ekran rengi aracı (C# WinForms, .NET 9, bağımlılık yok).

## Dosyalar
- `Native.cs` — gama rampası (`Ramp`), NVIDIA NVAPI canlılık/ton (`Nv`), DWM koyu başlık, kısayol, otomatik başlatma (`Win`).
- `Config.cs` — ayarlar/profiller, `%AppData%\tlk-color\config.json` (yaz-taşı ile atomik kayıt, açılışta doğrulama).
- `Controls.cs` — elle çizilmiş koyu tema kontrolleri + `Anim` (tek zamanlayıcılı üstel yumuşatma, Windows animasyon ayarına uyar).
- `Display.cs` — çözünürlük/Hz/yön (`ChangeDisplaySettingsEx`, önce `CDS_TEST`) ve ekran başına ölçek (belgelenmemiş DisplayConfig DPI paketleri -3/-4, Ayarlar uygulamasının kullandığı yol).
- `MainForm.cs` — düzen ve davranış; Renk ve Görüntü sayfaları. `Program.cs` — tek örnek, `--tray`, `--selftest`.

## Özellikler
- Ekran başına ayar (veya tümü), kanal başına (Tümü/R/G/B) parlaklık-kontrast-gama, renk sıcaklığı 1900–10000 K.
- NVIDIA'ya bağlı ekranlarda dijital canlılık ve ton (NVAPI, `nvapi64.dll` sürücüyle gelir). Laptop iç ekranı Intel'e bağlıysa devre dışı görünür.
- Önce/Sonra: buton basılı / Boşluk basılı / Ctrl+Alt+B kilit. Gerçek ekranda doğrusal rampa + varsayılan canlılık uygular.
- Profiller, Ctrl+Alt+1…9 ile uygulanır; Ctrl+Alt+0 sıfırla; Ctrl+Alt+C aç/kapa.
- Tepsi menüsü, Windows ile başlat (`--tray`), ekran/oturum/uyku değişiminde yeniden uygulama.
- Windows uç rampaları reddederse, kabul edilen en güçlü ara değeri uygular ("etki kısıldı"); "Geniş gama aralığı" butonu HKLM `GdiIcmGammaRange=256` yazar (UAC).

- Görüntü sayfası: monitör yerleşim haritası (tıkla-seç), çözünürlük, yenileme hızı, ölçek (%), yön. Mod değişikliği 15 sn geri sayımlı onay ister, onaylanmazsa eski DEVMODE geri yüklenir; ölçek onaysız uygulanır.
- Özel çözünürlük (G × Y @ Hz): sürücü kabul ederse `ChangeDisplaySettingsEx`, etmezse NVIDIA ekranda NVAPI `GetTiming(AUTO)` → `TryCustomDisplay` → onay → `SaveCustomDisplay` / `RevertCustomDisplayTrial` (kimlikler NVIDIA/nvapi `nvapi_interface.h`). Intel ekranda desteklenmez.
- 4:3 hazırları (1440×1080, 1280×960, 1024×768) sığdırmayı "Tam ekran" yapar.
- Sığdırma (NVIDIA "masaüstü boyutu" gibi): Oranı koru / Tam ekran / Ortala / Monitör = DISPLAYCONFIG_SCALING 4/3/2/1, `SetDisplayConfig` **virtual-mode-aware** bayraklarıyla (yoksa Windows identity'ye sıfırlar). Mod başına saklanır, doğal çözünürlükte etkisizdir; Intel laptop paneli "Ortala"yı desteklemiyor.

## Doğrulama
- `tlk-color.exe --selftest` — her ekranda rampa yaz/geri oku, 1900 K kısma, NVIDIA canlılık yaz/geri al. Çıktı stdout.
- 2026-10-28: iki ekranda rampa birebir; DISPLAY5'te (NVIDIA) canlılık 50→80→50 çalıştı.
- `tlk-color.exe --selftest-display` — ikincil ekranda Hz ve ölçeği değiştirip geri alır (ekran kısa süre titrer).
- 2026-10-29: DISPLAY1 144→60→144 Hz ve %125→%100→%125 başarılı.
- 2026-10-29: 4:3'te sığdırma 3/4/2 DISPLAY5'te tuttu (DISPLAY1'de 2 hariç); NVIDIA özel 1776×1000 deneme + geri alma; arayüzden özel mod → onay penceresi → Esc ve 15 sn zaman aşımı ile geri dönüş doğrulandı.

## Bilinen sınırlar
- Gama rampası tüm monitöre uygulanır; aynı ekranda yan yana önce/sonra mümkün değil, bu yüzden basılı-tut yöntemi var.
- Bazı tam ekran oyunlar rampayı sıfırlar; şu an periyodik yeniden uygulama yok.
- NVAPI fonksiyon kimlikleri sürücüden sorgulanır; desteklenmezse sessizce devre dışı kalır.

## Yayın
- Arayüz dili TR/EN: `L.cs` (`L.T("Türkçe")`, sözlük Türkçe metinle anahtarlı), ayar `Lang`; değişince uygulama yeniden başlar.
- Site: `docs/index.html` (GitHub Pages, master /docs). Ekran görüntüleri `docs/assets/screenshot-*.png` (EN) ve `*-tr.png`.
- Release: `dotnet publish ... -o publish` sonra `gh release create vX.Y.Z publish/tlk-color.exe`.
