# OROL Survival

Android uchun **Unity 6.3 LTS** (6000.3.26f1, URP) da yozilgan original 3D open-world multiplayer survival o'yini va uning
**Linux dedicated server**i. Loyiha telefon orqali (kompyutersiz) ishlab chiqishga moslangan: APK GitHub Actions'da
yig'iladi, server Docker'da ishlaydi, server manzili esa repozitoriydagi bitta JSON fayl orqali o'zgartiriladi.

> Holat: **Phase 1 — Playable Foundation** + tarmoq yadrosi. Nima ishlaydi va nima hali tekshirilmagani —
> [docs/TEST_REPORT.md](docs/TEST_REPORT.md). Keyingi bosqichlar — [docs/ROADMAP.md](docs/ROADMAP.md).

| Hujjat | Mazmuni |
|---|---|
| [docs/BUILD_APK.md](docs/BUILD_APK.md) | Telefondan APK olish: Unity litsenziyasi, GitHub Secrets, build, yuklab olish, muammolar |
| [docs/SERVER.md](docs/SERVER.md) | Dedicated serverni VPS'ga joylash (Docker / systemd), konfiguratsiya, admin, monitoring |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Modullar, tarmoq yechimini baholash (Netcode for Entities vs NGO vs LiteNetLib), sinxronizatsiya |
| [docs/ROADMAP.md](docs/ROADMAP.md) | 6 bosqich va har birining holati |
| [docs/TEST_REPORT.md](docs/TEST_REPORT.md) | O'tkazilgan testlar, natijalar, tekshirilmagan qismlar |

## O'yin oqimi

`Unity splash → Cinematic intro → 3D lobby → Server tanlash (real ping/holat) → Ulanish → Orol yuklanishi → Xavfsiz random spawn → O'yin`

## Repozitoriy tuzilmasi

```
UnityClient/                     Unity 6.3 LTS loyihasi (Android ARM64, IL2CPP, URP, landscape)
  Assets/Survival/Core           deterministik RNG/noise, matematika, log, spatial grid      (engine'siz C#)
  Assets/Survival/World          5x5 km orol generatori, height field, daraxt/resurs joylashuvi, spawn qoidalari
  Assets/Survival/Gameplay       harakat simulyatsiyasi (prediction+server), survival ko'rsatkichlari
  Assets/Survival/Networking     protokol, server so'rovi (ping), ulanish/reconnect, prediction, interpolatsiya
  Assets/Survival/Server         authoritative server: sessiyalar, botlar, interest management, saqlash
  Assets/Survival/Client         Unity qatlami: intro, lobby, UI, terrain streaming, o'simliklar, HUD, audio
  Assets/Survival/Editor         CI build skripti, loyiha validatori
  Assets/Survival/Tests          Unity EditMode testlari
  Assets/Plugins/LiteNetLib      UDP transport (MIT, 2.1.5)
Server/                          .NET 8 dedicated server (Linux), testlar, load-test, Docker/systemd
config/serverlist.json           O'yindagi server ro'yxati (APK'ni qayta yig'masdan IP o'zgartirish)
tools/unity-compile-check/       Unity litsenziyasisiz C# kompilyatsiya tekshiruvi
.github/workflows/               Android APK, Dedicated Server, Unity Compile Check
.devcontainer/unity-license/     Kompyutersiz Unity litsenziyasini olish uchun Codespaces yordamchisi
```

`Core/World/Gameplay/Networking/Server` kodi **bir vaqtning o'zida** Unity'da (telefon) va .NET serverda kompilyatsiya
qilinadi — server va o'yin har doim aynan bir xil fizika, protokol va orol generatorini ishlatadi.

## Tezkor start (telefondan)

1. **APK**: [docs/BUILD_APK.md](docs/BUILD_APK.md) bo'yicha Unity secrets'ni bir marta qo'shing → *Actions → Android APK → Run workflow* →
   tayyor bo'lgach *Releases → apk-latest → OROL-Survival.apk*.
2. **Serversiz sinash**: o'yinda *SERVERLAR → OFLAYN MASHQ* (telefonning o'zida lokal server + botlar; bu onlayn multiplayer emas).
3. **Ikki telefon bitta Wi-Fi'da**: birinchisida *WI-FI ORQALI HOST (LAN)*, ikkinchisida *LAN O'YINLARNI TOPISH*.
4. **Haqiqiy onlayn server**: VPS'da `docker run` ([docs/SERVER.md](docs/SERVER.md)) → `config/serverlist.json` dagi `"host"` ga IP yozing →
   o'yin DEMO SERVER #1 ni avtomatik ko'radi.

## Litsenziyalar va assetlar

Barcha kod, 3D modellar (procedural), shader'lar va tovushlar (sintez qilingan) shu loyiha uchun yaratilgan.
Uchinchi tomon: LiteNetLib (MIT), shriftlar Play va Russo One (SIL OFL 1.1, `UnityClient/Assets/Survival/Client/ThirdPartyLicenses`),
Unity URP shablon sozlamalari (Unity Companion License). CS2, PUBG Mobile, Rust'dan faqat janr ilhomi olingan —
ularning model, tovush, logotip yoki interfeysi ishlatilmagan.
