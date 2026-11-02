# Arxitektura

## 1. Tarmoq yechimini tanlash

Talab: 100 ta faol ishtirokchi (o'yinchi + 60–90 bot), 25 km² dunyo, server-authoritative harakat va jang, interest management,
interpolatsiya, client prediction, lag compensation, kelajakda bir nechta server; ishlab chiquvchi — kompyutersiz, faqat telefon.

| Mezon | Netcode for Entities (N4E) | Netcode for GameObjects (NGO) | **Tanlangan: maxsus replikatsiya + LiteNetLib** |
|---|---|---|---|
| Prediction / lag comp | Ichida bor (ghost prediction, physics history) | Yo'q, o'zi yoziladi | O'zimizniki: input-tick prediction + reconciliation (yozilgan, testlangan); lag comp uchun pozitsiya tarixi (`ServerEntity.History`) |
| Interest management | Ghost relevancy/importance | O'zi yoziladi (NetworkShow/Hide) | Spatial grid + radius + hysteresis + masofaga qarab chastota (yozilgan) |
| Server | Unity headless build (litsenziya + og'ir runtime) | Unity headless build | **.NET 8 jarayon**: Unity'siz, ~25 MB RAM 100 o'yinchida, 287 MB Docker image, CI'da litsenziyasiz test |
| Yozish/sinash usuli | DOTS/ECS, subscene baking — Editor'siz deyarli imkonsiz | Prefab GlobalObjectIdHash Editor'da generatsiya qilinadi | Toza C#: barcha server logikasi shu muhitda build, unit/integration/load testdan o'tdi |
| Mobil client | Entities Graphics mobilda cheklovli | Yaxshi | Yaxshi (oddiy MonoBehaviour + instancing) |
| Transport | Unity Transport | Unity Transport | LiteNetLib 2.1.5 (MIT, reliable UDP, IL2CPP/iOS/Android qo'llab-quvvatlaydi, production'da keng ishlatiladi) |

**Asoslash.** N4E texnik jihatdan eng kuchli (o'rnatilgan prediction va relevancy), lekin u DOTS va Editor'dagi baking'ga
qattiq bog'langan — telefon orqali, Editor'siz ishlab chiqishda uni sinash va xatolarini topish amalda mumkin emas, server ham
Unity build bo'lib qoladi. NGO sodda, lekin prediction/lag compensation/interest management baribir qo'lda yoziladi, prefab
hash'lari esa Editor'da yaratiladi. Shu sababli Rust kabi o'yinlardagi yondashuv tanlandi: **o'z replikatsiya qatlami** ishonchli
UDP kutubxonasi ustida. Natija: server va client **bitta C# kod** (harakat, protokol, orol generatori) — server Unity'siz ishlaydi,
CI'da har push'da 29 ta test va 100 mijozli yuk testi o'tadi. Kamchiliklari ochiq: fizika serverda soddalashtirilgan (height-field +
silindr to'siqlar, PhysX yo'q), prediction/lag compensation kodini o'zimiz qo'llab-quvvatlaymiz.

## 2. Modullar

```
Survival.Core        (engine'siz)  SMath, Hashing/Rng (PCG32), deterministik Noise, Log, SpatialGrid, RingBuffer, Pool
Survival.World       (engine'siz)  WorldConfig, IslandGenerator, HeightField (2 m, 1/64 m kvantlangan, chunk kesh),
                                   NodeLayout (daraxt/buta/tosh/ruda), WorldRuntime (to'siqlar), SpawnFinder, WorldFingerprint
Survival.Gameplay    (engine'siz)  PlayerInput, MovementSim (yurish/yugurish/cho'kkalash/sakrash/suzish/stamina), SurvivalStats
Survival.Networking  (engine'siz)  NetProtocol, xabarlar, NetCodec (kvantlash), ServerQueryClient, GameClient, ClientSimulation
Survival.Server      (engine'siz)  GameServer, Replication, BotManager/BotBrain, PlayerStore, ServerRunner, admin buyruqlar
Survival.Client      (Unity)       App/Session/Settings, Intro, Lobby, UI, Rendering, WorldView (terrain/vegetation/ocean), Game, Audio
Survival.Editor      (Editor)      BuildScript (CI), ProjectValidator
Survival.Tests.EditMode            Unity testlari; Server/tests — .NET xUnit testlari
```

Foydalanuvchi so'ragan bo'limlar xaritasi: Core → `Core`; Player/Gameplay → `Gameplay/Movement`; Networking → `Networking`;
Server → `Server/Core`; AI → `Server/AI`; Persistence → `Server/Persistence`; World → `World` + `Client/World`;
UI/Audio/Optimization → `Client/UI`, `Client/Audio`, `Client/App/QualityManager` + `Client/World` streaming; Editor/Tests — o'z papkalari.
Wildlife, Resources (yig'ish), Inventory, Crafting, Combat, Events modullari keyingi bosqichlarda shu tuzilma ichida qo'shiladi
(ROADMAP), ular uchun zamin tayyor: `NodeKind` (Stone/Metal/Sulfur), `DamageType`, `EntityType.Animal/ItemDrop/Container/Airdrop`,
`IsNodeAlive` (kesilgan daraxtlar), pozitsiya tarixi (lag compensation), `NoticeKind.Event`.

## 3. Dunyo: 5×5 km orol, tarmoq orqali uzatilmaydi

- Orol **seed**dan deterministik generatsiya qilinadi (domain-warp qirg'oq, plyajlar, qoyali sohillar, tepaliklar, tekisliklar,
  ridged tog' tizmalari 270 m gacha). Server Welcome xabarida faqat `seed/size/sea/version` yuboradi — har bir telefon orolni o'zi quradi.
- Platformalararo bir xillik: noise faqat `+ − × ÷ floor sqrt` ishlatadi, balandlik 1/64 m ga kvantlanadi, daraxt joylashuvi
  qarorlari butun sonli qiyalik bilan qilinadi. **WorldFingerprint** (balandlik va daraxtlar namunasi xeshi) Welcome'da keladi;
  client mos kelmasa ogohlantiradi; xesh .NET va Unity testlarida bir xil qiymatga "mahkamlangan" (`0xf94ef610`).
- Daraxtlar/toshlar/rudalar 32 m kataklarda deterministik joylashadi va stabil ID oladi → kelajakda server faqat
  "kesildi/qazildi/qayta o'sdi" holatini yuboradi (Phase 2).
- Rivers, lakes, caves — Phase 2 (height funksiyasiga qo'shimcha qatlam sifatida).

## 4. Sinxronizatsiya

```
Client (30 Hz fixed tick)                                     Server (30 Hz)
  input → MovementSim → tarix ──Input(seq, 4x redundancy, unreliable)──▶ navbat (dedup, tartib) → token-bucket (≤1 input/tick)
  ◀── Snapshot(tick, ackSeq, own state, vitals, time) unreliable ──── MovementSim (xuddi shu kod) → spatial grid
  ack bo'yicha solishtirish; farq > 5 sm → server holatidan
  qolgan inputlarni qayta o'ynash (reconciliation), vizual
  xato ~150 ms da silliq so'ndiriladi
  boshqa ob'ektlar: ~100 ms orqada interpolatsiya, ≤200 ms ekstrapolatsiya, teleportda snap
```

- **Interest management**: 64 m grid, radius 320 m (+30 m hysteresis), eng yaqin 80 ta ob'ekt; 120 m gacha har snapshot, 220 m gacha har 2-, undan
  uzog'i har 4- snapshot. Spawn/despawn — reliable, holat — unreliable; 60 tadan ortiq yozuv bo'lsa snapshot bo'linadi (MTU < 1200 B).
- **Kvantlash**: X/Z 1/256 m (24 bit), Y 1/64 m (16 bit), yaw 16 bit, ID varint → ~15 B/ob'ekt.
- **Lag compensation (Phase 3)**: har tick pozitsiya tarixi saqlanadi (`ServerEntity.PositionAt(tick)`); hujum kelganda client ko'rgan
  vaqtga (render tick) "orqaga o'rash" bilan tekshiriladi.
- **Reconnect**: client uzilishni sezsa 1/2/3/5/8/12 s oraliqda qayta ulanadi; server o'yinchini 90 s saqlaydi va o'sha personajga qaytaradi.
- **Autentifikatsiya**: qurilmada random `playerId` + `secret`; server birinchi kirishda sir xeshini (SHA-256) mahkamlaydi (TOFU).
  Ommaviy relizdan oldin Google Play Games / Game Center tokenlari va transport shifrlash qo'shilishi kerak.

## 5. Botlar va CPU budjeti

`botCount` bo'sh serverdagi botlar soni; `realPlayers + bots ≤ maxPlayers` har tickda saqlanadi (o'yinchi kirganda bot darhol joy bo'shatadi).
O'yinchilardan 400 m dan uzoq botlar 5 Hz da (dt×6) yengil simulyatsiya qilinadi va bir tickda cheklangan sonda "o'ylaydi".
Phase 1 bot aqli: orolni kezish, suvdan qochish, tiqilib qolishdan chiqish, dam olish, ko'nikma darajasi (Easy/Normal/Hard).
Phase 5: yig'ish, ov, jang, chekinish, davolanish, loot.

## 6. Client renderi va Android optimizatsiyasi

- **Terrain streaming**: 128 m chunklar, LOD 2/4/8/16 m, skirt'lar, vertex ma'lumotlari worker thread'larda, kadrda ≤2 mesh yuklash,
  ob'ekt/mesh pool. Ufqda butun orolning 40 m'lik past-poligonli mesh'i (shader ichida yaqin hudud kesiladi).
- **O'simliklar**: `Graphics.RenderMeshInstanced` (GPU instancing), 128 m bloklar bo'yicha frustum culling, yaqin LOD soya bilan,
  uzoq LOD soyasiz, ro'yxatlar worker thread'da quriladi; zichlik/masofa sifat sozlamasiga bog'liq.
- **Shader'lar**: o'zimizning mobil URP shader'lari (vertex-colour lit, suv, osmon, additive) — tekstura yo'q, bitta yorug'lik;
  shader ishlamasa URP fallback materiallari. Instancing va fog variantlari build'da saqlanadi (GraphicsSettings).
- **Sifat**: Low/Medium/High (har biri o'z URP asset'i: soya, HDR, MSAA) + Custom (render scale, ko'rish masofasi, soya, bloom, o'simlik);
  FPS 30/45/60 (ekran qo'llasa). Birinchi ishga tushishda RAM/GPU bo'yicha taxminiy tanlov.
- **Fizika**: client'da PhysX ishlatilmaydi — harakat va to'qnashuv height-field orqali (server bilan bir xil).
- **Xotira**: sahna almashganda `UnloadUnusedAssets`, personaj mesh'lari o'chirilganda bo'shatiladi.
- Occlusion culling: ochiq orolda statik occluder yo'q, shuning uchun frustum + masofa + fog bilan cheklangan; Addressables o'rniga
  procedural generatsiya (APK hajmi kichik). Haqiqiy FPS o'lchovi — qurilmada (TEST_REPORT).

## 7. Kengaytirish

- **Uchinchi shaxs kamera** allaqachon bor (pauza menyusi → 1ST / 3RD PERSON).
- **Yangi tizimlar** (inventar, craft, jang, hayvonlar, eventlar): server tomonda `Survival.Server/<Modul>`, umumiy qoidalar
  `Survival.Gameplay/<Modul>`, xabarlar `Networking/Protocol` (yangi `MsgId` + `NetProtocol.Version` oshiriladi), vizual — `Survival.Client/<Modul>`.
- **Ko'p server**: har server mustaqil jarayon; keyingi qadam — kichik "directory" xizmati (serverlar o'zini e'lon qiladi) va
  akkaunt/inventar uchun umumiy DB (PostgreSQL/Redis) — `IPlayerStore` interfeysi shu uchun ajratilgan.
- **iOS**: kodda platformaga bog'liq joy yo'q; BuildScript iOS identifikator va IL2CPP'ni ham sozlaydi; GameCI macOS runner kerak bo'ladi.
