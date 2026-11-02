# Test hisoboti — Phase 1

Sana: 2026-11-02. Muhit: bulutdagi Linux konteyner (4 vCPU, 15 GB RAM), .NET 8.0.33, Unity 6000.3.26f1 reference
assembly'lari (GameCI image'idan). **Unity Editor bu muhitda ishga tushirilmagan** — Unity litsenziyasi yo'q (pastga qarang).

## 1. Tekshirildi va o'tdi ✅

### .NET testlari — `Server/tests/Survival.Tests` (29/29 o'tdi, ~13 s)

| Guruh | Nimani tekshiradi |
|---|---|
| World (7) | noise determinizmi; orol markazi quruqlik, chetlari okean; plyaj/tog'/quruqlik ulushi (47.6% quruqlik, cho'qqi 269 m); keshlangan va keshsiz balandliklar ko'p thread'da bir xil; daraxt/ruda joylashuvi ikki marta bir xil, ID'lar takrorlanmaydi, suv ostida yo'q; 40 ta spawn — suvda emas, tik qiyalikda emas, daraxt ichida emas, bir-biridan ≥12 m |
| Fingerprint (2) | orol generatori natijasi `0xf94ef610` ga mahkamlangan (o'zgarsa test yiqiladi → GeneratorVersion oshiriladi); seed'ga bog'liq |
| Movement (7) | yurish tezligi; yugurish tezroq va stamina sarflaydi; sakrash/qo'nish; chuqur suvda suzish; daraxt to'sig'i; **prediction = server (0 tuzatish)**; server boshqacha holat yuborsa reconciliation tuzatadi |
| Protocol (5) | varint; pozitsiya kvantlash xatosi < 3 mm; Welcome xabari to'liq qaytadi; 60 ob'ektli snapshot < 1200 B (MTU); nik sanitizatsiyasi |
| Integration, haqiqiy UDP (8) | server so'rovi real ma'lumot + ping qaytaradi; o'chiq server → Offline; ikki o'yinchi bir-birini ko'radi va harakat replikatsiya bo'ladi; **server to'la → ServerFull bilan rad**; botlar joy bo'shatadi (o'yinchi+bot ≤ max); noto'g'ri sir → AuthFailed, profil diskda saqlanadi va qaytganda saqlangan joyda paydo bo'ladi; **reconnect o'sha personajga qaytaradi**; o'lim → respawn oqimi; eski versiya → VersionMismatch |

### Yuk testlari — `orol-loadtest` (har bir mijoz telefon bilan bir xil prediction kodini ishlatadi)

| Stsenariy | Natija |
|---|---|
| 100 real UDP mijoz, 5×5 km orol, 60 s (botlar avtomatik 70 → 0) | 100/100 ulandi · server tick o'rtacha **1.25 ms**, max 2.1 ms (budjet 33.3 ms) · mijozga ~1.9 KB/s · server chiqishi 194 KB/s · RAM 33 MB · prediction tuzatishlari **0** |
| Zich: 60 mijoz + 40 bot, 1×1 km orol (hamma bir-birini ko'radi) | 60/60 · har mijoz ~49 ob'ektni ko'radi · ~9.6 KB/s/mijoz · tick o'rtacha 1.1 ms, max 8 ms |
| Zich + mobil tarmoq emulyatsiyasi: 40–140 ms kechikish, 3% paket yo'qotish (client chiquvchi) | 60/60 · RTT p50 105 ms, p95 ~330 ms · ~12 KB/s/mijoz · prediction tuzatishlari **0** (input redundancy yo'qotishni yopdi) |
| 80 bot, o'yinchisiz | tick o'rtacha 0.19 ms |
| Docker konteyner (`docker build` + `docker run`) + 10 mijoz | image 287 MB, /health, /status ishlaydi, mijozlar ulandi |

Kod ko'rib chiqishda topilib tuzatilgan client xatolari: 60 FPS da sakrash bosilishi tick'siz kadrda yo'qolishi mumkin edi (endi
tick bajarilguncha saqlanadi); o'yinga kirish ekrani 220–300 m dagi LOD1 chunk'larni LOD0 deb kutib 20 s timeout'ga tushardi;
telefondagi oflayn server ishga tushishda terrain'ni asosiy thread'da oldindan generatsiya qilardi (endi `prewarmRadius = 0`).

Topilgan va tuzatilgan muammo: eski build'dagi load-test boshqa orol generatori bilan bashorat qilgani uchun ~1000 tuzatish
berdi. Natijada **WorldFingerprint** qo'shildi (server Welcome'da yuboradi, client solishtiradi) va generator natijasi testda mahkamlandi.

### GitHub Actions (haqiqiy CI)

| Workflow | Natija |
|---|---|
| **Dedicated Server** (run #1) | ✅ build · 29/29 test · 100 mijozli load test PASS (tick o'rtacha 1.11 ms, max 1.46 ms) · Linux x64 (29 MB) va arm64 (27 MB) binarlar artifact |
| **Android APK** (run #1) | ⛔ kutilganidek birinchi qadamda to'xtadi: *Missing repository secrets: UNITY_EMAIL UNITY_PASSWORD UNITY_LICENSE* |
| **Unity Compile Check** (run #1) | ✅ GameCI image'dan Unity 6000.3.26f1 DLL'larini olib, 9 ta assembly'ni player + editor rejimida kompilyatsiya qildi (~5 daqiqa) |

### Unity C# kompilyatsiyasi (litsenziyasiz) — `tools/unity-compile-check`

Unity 6000.3.26f1 ning o'z Roslyn kompilyatori bilan, haqiqiy UnityEngine/UnityEditor DLL'lari va Input System 1.20, uGUI 2.0,
URP/Core RP 17.3 manba kodlariga qarshi: **player (Android) va editor o'tishlarida 9/9 assembly xatosiz** (Core, World, Gameplay,
LiteNetLib, Networking, Server, Client, Editor, Tests). Bu vosita ikki haqiqiy API xatosini topdi va ular tuzatildi.

### Boshqa
- Orol xaritasi rasmga chiqarilib vizual tekshirildi (tog'lar past chiqqani shu yerda aniqlanib, generator sozlandi).
- Workflow YAML va shell skriptlar sintaksisi tekshirildi.

## 2. Hali tekshirilmagan ⚠️ (da'vo qilinmaydi)

| Qism | Sabab / qanday tekshiriladi |
|---|---|
| **Android APK** | Unity build litsenziya talab qiladi. Secrets qo'shilgach **Android APK** workflow birinchi marta yig'adi; natija o'shanda ma'lum bo'ladi. APK hozircha **yo'q**. |
| Shader'lar (OROL/VertexLit, Water, Sky, Additive) | Faqat Unity kompilyatsiya qiladi. Xato bo'lsa `ProjectValidator` build'ni aniq xabar bilan to'xtatadi; runtime'da URP fallback'ga o'tadi. |
| Sahnalar, intro, lobby, HUD ko'rinishi, animatsiyalar, tovushlar | Unity'da ishga tushirib ko'rilmagan — kod kompilyatsiya bo'ladi, lekin vizual natija qurilmada tekshirilishi kerak. |
| Touch boshqaruv, layout muharriri | Qurilmada tekshiriladi. |
| FPS (30/60) | Hech qanday raqam kafolatlanmaydi; o'rta Android'da 30 FPS maqsad — qurilmada profiling kerak (FPS hisoblagich HUD'da). |
| Telefon ARM64 ↔ server x64 determinizmi | Fingerprint mexanizmi farqni aniqlaydi; haqiqiy telefonda tekshiriladi (HUD'da ogohlantirish chiqadi). |
| Ikki telefon orasida onlayn o'yin | Server VPS'ga joylanib, APK yig'ilgandan keyin. |
| Unity EditMode testlari (`Survival.Tests.EditMode`) | Kompilyatsiya bo'ladi; Unity Test Runner'da hali ishga tushirilmagan. |
| Codespaces litsenziya yordamchisi | Unity akkaunti talab qiladi — sinalmagan. |
| 5×5 km dunyoda 100 haqiqiy telefon | Load-test 100 mijozni simulyatsiya qildi; haqiqiy qurilmalar bilan emas. |

## 3. Qayta ishga tushirish

```bash
dotnet test Server/tests/Survival.Tests -c Release
Server/tools/Survival.LoadTest/bin/Release/net8.0/orol-loadtest --clients 100 --duration 60 [--latency 40-140 --loss 3]
tools/unity-compile-check/extract.sh 6000.3.26f1 /tmp/unity && python3 tools/unity-compile-check/compile_check.py --unity /tmp/unity --project UnityClient
```

CI'da xuddi shular: **Dedicated Server** (build + test + 100 mijozli load test) va **Unity Compile Check** workflow'lari.
