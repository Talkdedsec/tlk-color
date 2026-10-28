# DEVAM

NVIDIA renk ayarı benzeri basit ekran rengi aracı (C# WinForms, .NET 9).

- `SetDeviceGammaRamp` ile tüm monitörlere parlaklık / kontrast / gama / R-G-B kazancı uygular.
- Ayar kaydedilmez; gamma rampası uygulama kapansa da kalır, çözünürlük/sürücü değişince sıfırlanır.
- Windows uç rampaları reddeder (durum satırında görünür).

Yapılmadı: dijital titreşim (NVAPI gerekir), ayar kaydı, açılışta otomatik uygulama, tepsi simgesi.
