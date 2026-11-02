# Roadmap

Belgilar: ✅ bajarildi va shu muhitda tekshirildi · 🟡 kod yozilgan, lekin Unity/qurilmada hali ishga tushirib ko'rilmagan · ⬜ hali yo'q

## Phase 1 — Playable Foundation (joriy)

| Vazifa | Holat |
|---|---|
| Unity 6.3 LTS loyihasi (URP, Android ARM64/IL2CPP, landscape, paket ID) | ✅ kompilyatsiya (litsenziyasiz tekshiruv) · 🟡 Unity Editor'da ochilmagan |
| Cinematic intro (zarralar, 3D logotip, glow, light sweep, real yuklash, Skip, qisqa rejim) | 🟡 |
| Animatsiyali 3D lobby (qahramon idle, gulxan, okean, orol silueti, menyu, safe area) | 🟡 |
| Server tanlash (real UDP holat/ping/o'yinchilar, Full/Empty/Offline, qo'lda IP, LAN, oflayn) | ✅ tarmoq qismi testlangan · 🟡 UI |
| Mobil boshqaruv (joystick, qarash, sakrash, cho'kkalash, yugurish, layout muharriri) | 🟡 |
| Kichik sinov zonasi o'rniga — butun 5×5 km orol streaming bilan | ✅ generator/test · 🟡 render |
| APK build pipeline (GameCI, release, AAB, signing, validator) | 🟡 Unity secrets qo'shilgach birinchi build kerak |

## Phase 2 — Island World

| | |
|---|---|
| 5×5 km orol, qirg'oq, plyaj, tog', o'rmon, tekislik | ✅ |
| Terrain streaming + LOD, uzoq orol mesh'i | 🟡 |
| Daraxt/tosh/ruda (Stone/Metal/Sulfur) joylashuvi | ✅ joylashuv · ⬜ yig'ish |
| Random xavfsiz spawn (server) | ✅ |
| Daryolar, ko'llar, g'orlar | ⬜ |
| Resurs yig'ish (server-authoritative), node health/respawn, density/timer konfiguratsiyasi | ⬜ |
| Inventar (server-side, persist) + UI | ⬜ (lobby'da oxirgi holat paneli bor) |

## Phase 3 — Survival

| | |
|---|---|
| Health/hunger/thirst/stamina, starvation/dehydration, regen, yiqilish zarari | ✅ server qoidalari · 🟡 HUD |
| O'lim / respawn | ✅ (integration test) |
| Ovqat/suv/davolanish buyumlari, crafting (stone axe, pickaxe, spear, bow, arrows, bandage, campfire) | ⬜ |
| Hayvonlar AI (wolf, bear, boar, deer, chicken, horse) | ⬜ |
| Jang (melee, kamon, hit detection + lag compensation, loot drop) | ⬜ (pozitsiya tarixi tayyor) |

## Phase 4 — Multiplayer

| | |
|---|---|
| Linux dedicated server (.NET 8), Docker, systemd, status/admin HTTP | ✅ |
| Real client ulanishlari, handshake, versiya/auth/full tekshiruvlari | ✅ |
| Prediction + reconciliation, interpolatsiya, interest management | ✅ |
| Persistence (pozitsiya, vitals, statistika) | ✅ |
| Disconnect/reconnect (grace period) | ✅ |
| Ikki qurilmada haqiqiy test | ⬜ APK yig'ilgandan keyin |

## Phase 5 — Advanced World

| | |
|---|---|
| 60–90 bot konfiguratsiyasi, o'yinchi kirsa kamayishi, distant simulation, CPU budjeti | ✅ (Phase 1 bot aqli: kezish) |
| Bot: yig'ish, ov, jang, chekinish, davolanish, loot | ⬜ |
| Dinamik eventlar (airdrop, supply crates, storm, wildlife, rare resource) | ⬜ (storm uchun shader/osmon zamini bor) |
| Komponentlar/loot jadvallari | ⬜ |
| Yuk testi (100 mijoz) | ✅ |

## Phase 6 — Final Demo

UI/animatsiya polish, haqiqiy 3D modellar (CC0: Quaternius/Kenney yoki o'z modellari), musiqa, qurilmada FPS profiling,
Unity PlayMode testlari, Google Play uchun AAB + release signing, auth.
