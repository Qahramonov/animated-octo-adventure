# Dedicated server (Linux)

`Server/` — .NET 8 da yozilgan haqiqiy authoritative server. U Unity'siz ishlaydi (Unity litsenziyasi kerak emas),
o'yin bilan bir xil C# kodni (`UnityClient/Assets/Survival/{Core,World,Gameplay,Networking,Server}`) kompilyatsiya qiladi.

## Nima qiladi

- UDP 7777 da ulanishlar (LiteNetLib), protokol versiyasi tekshiruvi, o'yinchi ID + sir bilan autentifikatsiya
- Xavfsiz random spawn (suv/tik qiyalik/daraxt ichi/boshqa o'yinchi ustiga emas), saqlangan joydan davom ettirish
- Server-authoritative harakat (client input'lari, qat'iy tick dt, input rate-limit — speedhack yo'q)
- Health/hunger/thirst/stamina, yiqilish zarari, o'lim va respawn
- AI botlar: real o'yinchi kirishi bilan kamayadi (o'yinchi + bot ≤ `maxPlayers`), uzoqdagi botlar yengil simulyatsiya
- Interest management (spatial grid, radius, hysteresis, masofaga qarab yangilanish chastotasi), MTU'ga sig'adigan snapshot'lar
- Disconnect'dan keyin 90 s davomida personaj saqlanadi — reconnect shu personajga qaytaradi
- O'yinchilar profili diskka atomar saqlanadi (`data/players/*.bin`), avtosave
- Brauzer so'rovi (unconnected UDP): nomi, region, o'yinchilar, botlar, max, ping — o'yindagi server ro'yxati shuni ko'rsatadi
- `/health`, `/status` (JSON) HTTP endpoint, admin buyruqlar (konsol yoki token bilan HTTP)

## VPS'ga o'rnatish (Docker — tavsiya)

Minimal: 1 vCPU, 1 GB RAM, Ubuntu 22.04/24.04, ochiq **UDP 7777** (+ ixtiyoriy TCP 8080). Osiyo uchun Toshkent/Almati/
Frankfurt/Singapur'dagi VPS mos (ping o'yinchilarga yaqinlikka bog'liq).

```bash
# VPS'da (SSH — telefondan Termux yoki JuiceSSH orqali):
curl -fsSL https://get.docker.com | sh
git clone https://github.com/Qahramonov/animated-octo-adventure.git orol && cd orol
docker compose -f Server/deploy/docker-compose.yml up -d --build
docker logs -f orol-server            # loglar
curl localhost:8080/status            # holat
sudo ufw allow 7777/udp               # firewall (agar yoqilgan bo'lsa)
```

Yoki tayyor image (GitHub Actions `Dedicated Server` workflow main branch'da yoki qo'lda ishga tushirilganda yig'adi):

```bash
docker run -d --name orol --restart unless-stopped -p 7777:7777/udp -p 8080:8080/tcp \
  -v orol-data:/data -e OROL_BOT_COUNT=70 ghcr.io/qahramonov/orol-server:latest
```

### O'yinga ulash

`config/serverlist.json` da `"host"` ga VPS IP manzilini (yoki domenni) yozing va commit qiling (GitHub'da brauzerdan tahrirlash mumkin).
O'yin bu faylni `raw.githubusercontent.com` dan o'qiydi — **APK'ni qayta yig'ish shart emas**. (Repozitoriy private bo'lsa
fayl o'qilmaydi; u holda IP'ni o'yindagi *SERVER QO'SHISH* maydoniga yozing yoki `Resources/client_config.json` ga kiritib APK yig'ing.)

## Docker'siz (systemd)

```bash
# Actions → Dedicated Server → Artifacts → orol-server-linux (x64 yoki arm64)
sudo mkdir -p /opt/orol && sudo tar -xzf orol-server-linux-x64.tar.gz -C /opt/orol
sudo useradd --system --home /opt/orol orol && sudo chown -R orol /opt/orol
sudo cp /opt/orol/orol-server.service /etc/systemd/system/ && sudo systemctl daemon-reload
sudo systemctl enable --now orol-server && journalctl -u orol-server -f
```

## Konfiguratsiya

Ustuvorlik: standart < `server.json` < muhit o'zgaruvchilari `OROL_*` < buyruq qatori `--kalit qiymat`.

| Kalit | Standart | Izoh |
|---|---|---|
| `serverName` | DEMO SERVER #1 | server ro'yxatidagi nom |
| `region` / `mapName` | Asia / Demo Island | |
| `port` | 7777 | UDP |
| `maxPlayers` | 100 | faol ishtirokchilar (o'yinchi + bot) chegarasi |
| `botCount` | 70 | bo'sh serverdagi bot soni (demo uchun 60–90) |
| `tickRate` | 30 | simulyatsiya Hz (10–60) |
| `snapshotInterval` | 1 | har N tickda snapshot |
| `interestRadius` | 320 | tarmoq ko'rinish radiusi, m |
| `worldSeed` / `worldSize` | 20261102 / 5000 | orol (o'zgartirilsa — yangi dunyo) |
| `resourceDensity` / `vegetationDensity` | 1.0 | resurs/o'simlik zichligi (client ham shu qiymatni oladi) |
| `animalDensity` / `eventFrequency` | 1.0 | Phase 3/5 uchun (hozircha saqlanadi) |
| `dayLengthMinutes` | 48 | kun-tun davri |
| `botFullSimRadius` | 400 | o'yinchidan uzoqdagi botlar yengil rejimda |
| `distantBotTickDivider` / `maxBotThinksPerTick` | 6 / 12 | AI CPU budjeti |
| `reconnectGraceSeconds` | 90 | uzilgan o'yinchi personaji saqlanadigan vaqt |
| `dataDirectory` | data | profillar |
| `statusHttpPort` | 8080 | 0 = o'chirilgan |
| `adminToken` | (bo'sh) | bo'sh bo'lsa HTTP admin o'chiq |

## Admin

HTTP (`adminToken` o'rnatilganda) yoki interaktiv konsol (serverni `--no-console` siz terminalda ishga tushirganda,
masalan `docker run -it --rm -p 7777:7777/udp ghcr.io/qahramonov/orol-server:latest dotnet orol-server.dll`):

```bash
curl -s -d '' "http://SERVER:8080/admin?token=TOKEN&cmd=bots%2080"
```

Buyruqlar: `status`, `players` (ping, pozitsiya, input diagnostikasi), `bots <n>`, `kick <nom>`, `say <matn>`, `save`, `time <0..1>`, `config`, `quit`.

## Yuk testi

```bash
dotnet build Server/Survival.Server.sln -c Release
Server/tools/Survival.LoadTest/bin/Release/net8.0/orol-loadtest --host SERVER_IP --port 7777 --clients 100 --duration 60
```

Har bir test-client telefon bilan bir xil prediction/reconciliation kodini ishlatadi va RTT, trafik, ko'rinadigan ob'ektlar,
prediction tuzatishlari sonini chiqaradi. Natijalar: [TEST_REPORT.md](TEST_REPORT.md).

## Bir nechta server

Har bir server — alohida jarayon/konteyner (alohida port yoki host, alohida `dataDirectory`). Ro'yxatga
`config/serverlist.json` ga yangi yozuv qo'shiladi. Markaziy akkaunt/matchmaking xizmati — [ARCHITECTURE.md](ARCHITECTURE.md#kengaytirish).
