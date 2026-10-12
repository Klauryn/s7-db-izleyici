# S7 DB İzleyici

Siemens S7-1200/1500 PLC'lerdeki veri bloklarını (DB) izleyen ve yazabilen küçük bir **.NET 8 WinForms** aracı. PLC iletişimi için [S7.Net+](https://github.com/S7NetPlus/s7netplus) kullanır.

> **Durum:** Derleniyor, ancak henüz gerçek bir PLC üzerinde sistematik olarak test edilmedi. Üretim makinelerinde kullanmadan önce kendi test ortamında dene.

## Özellikler
- Bağlantı: IP, CPU tipi, rack/slot; kopmada 0,5 sn'den 5 sn'ye çıkan beklemeyle otomatik yeniden bağlanma.
- **Bool dizisi:** `Array[0..255] of Bool` için 16×16 bit ızgarası; **From** (yalnız okuma) ve **To** (tıklayınca ters çevirip yazar).
- **Sayı dizisi:** LReal (double), Real, DInt, Int; From/To tablosu, To hücresini düzenleyip Enter ile yaz.
- **String dizisi:** `Array of String[N]`; From/To tablosu, yazma.
- **Serbest değişken listesi:** Ad, tip, DB, ofset, bit; aynı DB'deki değişkenler tek istekte okunur.
- **Süre ölçümü:** Toplu okuma ile tek tek okumayı 50 turda karşılaştırır.
- **Yazma koruması:** "Yazmaya izin ver" kutusu varsayılan kapalı.

## Gereksinimler
- Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download)
- S7-1200/1500 CPU ve aynı ağda erişim

## PLC hazırlığı
1. CPU özellikleri → Koruma ve güvenlik → Bağlantı mekanizmaları: **PUT/GET ile erişime izin ver**.
2. Okunacak DB'lerde **Optimized block access kapalı** (yalnız global DB; S7 temel protokolü sembolik/optimize erişimi desteklemez).
3. Derle ve donanımı indir. S7-1200/1500 için genelde Rack 0, Slot 1.
4. TCP 102 açık olmalı. Base S7 protokolü şifrelemez: PLC'yi izole bir ağda tut.

## Çalıştırma
```
dotnet run
```

## Dizi yerleşimi
- `Array of Bool`: eleman *i* → başlangıç baytı + i/8. bayt, i%8. bit.
- Sayı: bayt ofseti = başlangıç + i × eleman boyutu (LReal 8, Real/DInt 4, Int 2).
- String: eleman boyutu = 2 + N bayt (başlık: maks. uzunluk, gerçek uzunluk). Karakter kodlaması Latin-1; `ş`, `ğ`, `ı` yazılırsa `?` olur.

Sekmelerdeki DB numarası ve ofsetler örnektir; kendi PLC'ne göre TIA'daki Offset sütunundan gir.

## Bilinen sınırlar
- Ayarlar (DB, tip) kaydedilmez; değişken listesi `variables.json` dosyasına kaydedilir.
- Optimize DB'de çalışmaz (sembolik erişim için OPC UA ya da S7CommPlus gerekir).
- Çıkışları etkileyen bitlere yazmadan önce makinenin güvenli durumda olduğundan emin ol.
