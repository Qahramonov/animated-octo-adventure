# APK'ni telefondan yig'ish (GitHub Actions + Unity 6.3)

APK GitHub serverlarida (GameCI Docker image `unityci/editor:ubuntu-6000.3.26f1-android-3`) yig'iladi. Sizga faqat telefon
va brauzer kerak. Unity build uchun **faollashtirilgan Unity litsenziyasi** majburiy — bu Unity talabi, uni chetlab bo'lmaydi.

## 1. Unity litsenziyasi (bir martalik)

### Kerakli GitHub Secrets

| Secret | Qiymat |
|---|---|
| `UNITY_EMAIL` | Unity akkauntingiz emaili |
| `UNITY_PASSWORD` | Unity akkaunti paroli |
| `UNITY_LICENSE` | `Unity_lic.ulf` faylining **to'liq matni** (Personal litsenziya uchun) |
| `UNITY_SERIAL` | faqat Unity Plus/Pro bo'lsa (`UNITY_LICENSE` o'rniga) |

Secrets qo'shish: GitHub (brauzerda "Desktop site") → repozitoriy → **Settings → Secrets and variables → Actions → New repository secret**.
Token/parollar hech qachon kodga yozilmaydi; workflow ularni faqat secrets'dan oladi.

### `Unity_lic.ulf` ni kompyutersiz olish (GitHub Codespaces)

Personal litsenziya fayli Unity Hub orqali yaratiladi. Kompyuter bo'lmasa, Unity Hub'ni bulutdagi Linux desktopda ishga tushiramiz:

1. GitHub'da repozitoriy → **Code → Codespaces → ⋯ → New with options** → *Dev container configuration*: **Unity license helper** → *Create*.
   (Bepul Codespaces limiti bu ish uchun yetarli.)
2. Codespace tayyor bo'lgach **Ports** bo'limida `6080` (Desktop) portini brauzerda oching → *Connect* → parol: `orol`.
3. Desktopdagi terminalda: `~/start-unity-hub.sh` → Unity Hub ochiladi → **Sign in** (brauzer oynasida Unity akkauntga kiring).
4. Unity Hub: **Preferences → Licenses → Add → Get a free personal license** (*Add* tugmasini albatta bosing — fayl shunda yaratiladi).
5. Terminalda: `~/show-license.sh` → chiqqan butun matnni (`<?xml ...` dan `</root>` gacha) nusxalab `UNITY_LICENSE` secret'iga qo'ying.
6. Codespace'ni o'chiring (*Stop/Delete*), aks holda bepul soatlar sarflanadi.

> Bu yordamchi (`.devcontainer/unity-license`) shu sessiyada Unity hisobisiz **sinab ko'rilmagan** (login sizning akkauntingizni
> talab qiladi). Muammo bo'lsa: istalgan kompyuterda Unity Hub orqali faollashtirib, `.ulf` faylni oling — GameCI
> hujjatiga ko'ra fayl platformaga bog'liq emas (Windows/Mac/Linux'dagi fayl Ubuntu runnerda ishlaydi).
> Joylashuvi: Windows `C:\ProgramData\Unity\Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`,
> Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`.

## 2. Build'ni ishga tushirish

- Avtomatik: `UnityClient/**` dagi har bir push'da **Android APK** workflow ishga tushadi.
- Qo'lda: **Actions → Android APK → Run workflow** → `apk` (telefonga o'rnatish) yoki `aab` (Google Play).
- Birinchi build ~40–70 daqiqa (Library kesh yo'q), keyingilari tezroq.

Agar secrets yo'q bo'lsa, workflow birinchi qadamda aniq xabar bilan to'xtaydi: *"Unity license is not configured"*.

## 3. APK'ni yuklab olish

- **Releases → `apk-latest` → `OROL-Survival.apk`** — telefonda to'g'ridan-to'g'ri ochiladi (zip emas).
- Yoki run sahifasidagi **Artifacts** (zip ichida APK + SHA-256).
- O'rnatish: faylni oching → "Noma'lum manbalardan o'rnatishga ruxsat" → O'rnatish.
- Paket nomi: `com.qahramonov.orol`, arxitektura: ARM64, minimal Android 8.0 (API 26), landscape.

## 4. Google Play uchun (AAB + release signing)

1. Keystore yarating (bir marta, masalan Termux'da `pkg install openjdk-17` → `keytool -genkeypair -v -keystore orol.keystore -alias orol -keyalg RSA -keysize 4096 -validity 10000`).
2. Secrets: `ANDROID_KEYSTORE_BASE64` (`base64 -w0 orol.keystore`), `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS`.
3. **Run workflow → aab**. Keystore'ni xavfsiz joyda saqlang — yo'qotilsa ilovani yangilab bo'lmaydi.

Secrets bo'lmasa APK Unity debug kaliti bilan imzolanadi (o'z telefoningizga o'rnatish uchun yetarli, Play Store uchun emas).

## 5. Litsenziyasiz tez tekshiruv

**Unity Compile Check** workflow (har push'da) loyihaning barcha C# assembly'larini Unity 6.3 ning o'z Roslyn kompilyatori va
haqiqiy UnityEngine/UnityEditor reference DLL'lariga qarshi kompilyatsiya qiladi — litsenziya talab qilinmaydi.
U shader va IL2CPP'ni tekshirmaydi; buni faqat haqiqiy Unity build qiladi.

## 6. Muammolar (troubleshooting)

| Belgi | Sabab / yechim |
|---|---|
| `Unity license is not configured` | Secrets qo'shilmagan yoki nomi xato (yuqoridagi jadval). |
| `No valid Unity Editor license found` / activation xatosi | `UNITY_LICENSE` noto'liq nusxalangan (boshi/oxiri kesilgan) yoki boshqa akkauntniki. Qayta faollashtiring. Email/parol aynan shu akkauntniki bo'lsin. |
| `No space left on device` | Workflow disk tozalaydi; baribir bo'lsa Library keshini o'chiring (*Actions → Caches*). |
| `Project validation failed` | Build oldidan `ProjectValidator` ishlaydi: log'da `[Validate]` qatorlarini qidiring (shader xatosi, missing script, yo'q resurs). |
| Shader xatosi (`Shader error OROL/...`) | Log'da qator raqami bor. O'yin baribir URP fallback materiallariga o'tadi, lekin xatoni tuzatish kerak. |
| `Build failed` | *Build with Unity* qadamida `error CS` (C# xatosi) yoki `[Build]` qatorlarini qidiring. |
| Gradle/SDK xatolari | Unity versiyasi `ProjectSettings/ProjectVersion.txt` da. Boshqa versiyaga o'tishda GameCI'da `ubuntu-<versiya>-android-3` image mavjudligini tekshiring. |
| APK o'rnatilmaydi ("App not installed") | Eski build boshqa kalit bilan imzolangan bo'lsa, eskisini o'chirib qayta o'rnating. |

Unity versiyasini yangilash: `UnityClient/ProjectSettings/ProjectVersion.txt` va `Packages/manifest.json` dagi paket versiyalari.
