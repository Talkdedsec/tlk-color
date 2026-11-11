using System.Globalization;

namespace TlkColor;

// UI text is written in Turkish; English comes from this table, keyed by the Turkish source.
static class L
{
    public static bool En;
    static readonly CultureInfo Tr = new("tr-TR");
    public static CultureInfo Culture => En ? CultureInfo.InvariantCulture : Tr;

    public static string T(string tr) => En && Map.TryGetValue(tr, out var en) ? en : tr;

    public static string Default => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    static readonly Dictionary<string, string> Map = new()
    {
        // Sidebar
        ["Ekran renk ayarı"] = "Screen color tuner",
        ["Renk"] = "Color",
        ["Görüntü"] = "Display",
        ["EKRAN"] = "SCREEN",
        ["PROFİLLER"] = "PROFILES",
        ["Tıkla: uygula · Ctrl+Alt+1…9: kısayolla uygula"] = "Click: apply · Ctrl+Alt+1…9: apply by hotkey",
        ["Kaydet"] = "Save",
        ["Sil"] = "Delete",
        ["Şu anki ayarları profil olarak kaydet"] = "Save the current settings as a profile",
        ["Seçili profili sil"] = "Delete the selected profile",
        ["AYARLAR"] = "SETTINGS",
        ["Etkin"] = "Enabled",
        ["Windows ile başlat"] = "Start with Windows",
        ["Kapatınca tepsiye"] = "Close to tray",
        ["Windows uç gama değerlerini kısar; bu ayar sınırı kaldırır (yönetici izni ister)"] = "Windows clamps extreme gamma values; this removes the limit (needs admin rights)",
        ["Geniş gama aralığı açık"] = "Wide gamma range on",
        ["Geniş gama aralığını aç"] = "Enable wide gamma range",
        ["Henüz profil yok"] = "No profiles yet",
        ["Ayarları beğenince Kaydet'e bas"] = "Like your settings? Press Save.",

        // Color page
        ["Tümü"] = "All",
        ["Kırmızı"] = "Red",
        ["Yeşil"] = "Green",
        ["Mavi"] = "Blue",
        ["KANAL"] = "CHANNEL",
        ["Test deseni ekranın gerçek çıktısıdır · alt sıradaki koyu ve açık kutuların hepsi ayırt edilebilmeli"] = "The test pattern is the screen's real output · every dark and light box in the bottom row should be distinguishable",
        ["TON"] = "TONE",
        ["RENK"] = "COLOR",
        ["Parlaklık"] = "Brightness",
        ["Kontrast"] = "Contrast",
        ["Gama"] = "Gamma",
        ["Renk sıcaklığı"] = "Color temperature",
        ["Dijital canlılık"] = "Digital vibrance",
        ["Ton kayması"] = "Hue shift",
        ["NVIDIA ekran yok"] = "No NVIDIA display",
        ["Sürükle, tekerlek veya ok tuşları · çift tık: varsayılan"] = "Drag, wheel or arrow keys · double-click: default",
        ["TON EĞRİSİ"] = "TONE CURVE",
        ["ÖNCE"] = "BEFORE",
        ["SONRA"] = "AFTER",
        ["Önce / Sonra  ·  basılı tut"] = "Before / After  ·  hold",
        ["Önce  ·  bırakınca sonra"] = "Before  ·  release for after",
        ["Basılı tutunca orijinal renkler görünür · Boşluk tuşu da çalışır · Ctrl+Alt+B kilitler"] = "Hold to see the original colors · Space works too · Ctrl+Alt+B locks it",
        ["KARŞILAŞTIRMA"] = "COMPARE",
        ["Önce"] = "Before",
        ["Sonra"] = "After",
        ["Sıfırla"] = "Reset",
        ["Seçili ekranı varsayılana döndür · Ctrl+Alt+0"] = "Reset the selected screen to defaults · Ctrl+Alt+0",
        ["SIFIRLANDI"] = "RESET",
        ["Tüm ekranlar"] = "All screens",
        ["Ekran"] = "Screen",
        ["birincil"] = "primary",
        ["Ekran gama ayarını kabul etmedi"] = "The screen rejected the gamma setting",
        ["Windows sınırı: etki kısıldı"] = "Windows limit: effect reduced",
        ["Bazı kısayollar başka programda"] = "Some hotkeys are used by another app",
        ["Orijinal renkler"] = "Original colors",
        ["Devre dışı"] = "Disabled",
        ["Uygulandı"] = "Applied",

        // Profiles
        ["Profil kaydet"] = "Save profile",
        ["Profil"] = "Profile",
        ["PROFİL ADI"] = "PROFILE NAME",
        ["PROFİL"] = "PROFILE",
        ["PROFİL KAYDEDİLDİ"] = "PROFILE SAVED",
        ["PROFİL SİLİNDİ"] = "PROFILE DELETED",
        ["Önce birini seç"] = "Select one first",
        ["Vazgeç"] = "Cancel",

        // Wide gamma
        ["GENİŞ GAMA"] = "WIDE GAMMA",
        ["Açıldı"] = "Enabled",
        ["Yazılamadı"] = "Could not write",
        ["İzin verilmedi"] = "Permission denied",

        // Display page
        ["Ayarlamak istediğin ekrana tıkla"] = "Click the screen you want to adjust",
        ["YERLEŞİM"] = "LAYOUT",
        ["Yatay"] = "Landscape",
        ["Dikey"] = "Portrait",
        ["Ters yatay"] = "Flip land.",
        ["Ters dikey"] = "Flip port.",
        ["Oranı koru"] = "Keep aspect",
        ["Tam ekran"] = "Full screen",
        ["Ortala"] = "Center",
        ["Monitör"] = "Monitor",
        ["Genişlik (piksel)"] = "Width (pixels)",
        ["Yükseklik (piksel)"] = "Height (pixels)",
        ["sığdırma: Tam ekran (kenarlar siyah olmaz)"] = "scaling: Full screen (no black bars)",
        ["Oranı koru: siyah kenar · Tam ekran: görüntüyü uzatır · Ortala: ölçeklemez · Monitör: ekranın kendisi karar verir"] = "Keep aspect: black bars · Full screen: stretches the image · Center: no scaling · Monitor: the screen decides",
        ["Doğal çözünürlükten farklı modlarda geçerli. 4:3'te kenarlar siyah kalmasın istiyorsan: Tam ekran."] = "Applies to non-native resolutions. To avoid black bars at 4:3, use Full screen.",
        ["Uygula"] = "Apply",
        ["Uygulanmamış seçimleri bırak"] = "Discard unapplied changes",
        ["Çözünürlük, yenileme hızı, yön ve özel çözünürlük 15 sn içinde onaylanmazsa geri alınır."] = "Resolution, refresh rate, orientation and custom resolution revert unless confirmed within 15 s.",
        ["ÇÖZÜNÜRLÜK"] = "RESOLUTION",
        ["YENİLEME HIZI"] = "REFRESH RATE",
        ["ÖZEL ÇÖZÜNÜRLÜK"] = "CUSTOM RESOLUTION",
        ["ÖLÇEK"] = "SCALE",
        ["YÖN"] = "ORIENTATION",
        ["SIĞDIRMA"] = "SCALING",
        ["özel"] = "custom",
        ["Sınır: 640×480–7680×4320, 24–500 Hz"] = "Limit: 640×480–7680×4320, 24–500 Hz",
        ["Ekran bu modu kabul etmedi"] = "The screen rejected this mode",
        ["Eski görüntü ayarına dönüldü"] = "Reverted to the previous display setting",
        ["NVIDIA bu özel çözünürlüğü reddetti"] = "NVIDIA rejected this custom resolution",
        ["Özel çözünürlük geri alındı"] = "Custom resolution reverted",
        ["Özel çözünürlük kaydedilemedi"] = "Custom resolution could not be saved",
        ["Özel çözünürlük yalnızca NVIDIA ekranda"] = "Custom resolution needs an NVIDIA display",
        ["Ölçek değiştirilemedi"] = "Scale could not be changed",
        ["Görüntü ayarı uygulandı"] = "Display setting applied",
        ["Ekran \"{0}\" desteklemiyor"] = "The screen does not support \"{0}\"",

        // Confirm dialog
        ["Görüntü ayarı"] = "Display setting",
        ["BU AYAR KORUNSUN MU?"] = "KEEP THIS SETTING?",
        ["{0} sn içinde onaylanmazsa eski ayara dönülür."] = "Reverts to the previous setting in {0} s unless confirmed.",
        ["Koru"] = "Keep",
        ["Geri al"] = "Revert",

        // Tray
        ["Göster"] = "Show",
        ["Çıkış"] = "Exit",
        ["Tepside çalışmaya devam ediyor. Çıkmak için simgeye sağ tıkla."] = "Still running in the tray. Right-click the icon to exit.",
        ["Arayüz dili · uygulama yeniden açılır"] = "Interface language · the app restarts",
    };
}
