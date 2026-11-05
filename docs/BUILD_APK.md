# APK'ni telefondan yig'ish (GitHub Actions + Unity 6.3)

APK GitHub serverlarida (GameCI Docker image `unityci/editor:ubuntu-6000.3.26f1-android-3`) yig'iladi. Sizga faqat telefon
va brauzer kerak. Unity build uchun **faollashtirilgan Unity litsenziyasi** majburiy — bu Unity talabi, uni chetlab bo'lmaydi.

## 1. Unity litsenziyasi (bir martalik, faqat telefon)

Bepul **Unity Personal** litsenziyasi uchun faqat Unity akkauntining email va paroli kerak. GameCI (2026) build boshida
Unity'ning litsenziya serveridan Personal o'rinni oladi va build tugagach qaytaradi. Kompyuter, Unity Hub yoki `.ulf` fayl kerak emas.

1. https://id.unity.com da bepul akkaunt oching (yoki mavjudiga kiring).
   - Google/Apple orqali kirgan bo'lsangiz parolingiz yo'q: chiqib, **Forgot password** orqali parol o'rnating.
   - **Ikki bosqichli himoya (2FA) o'chiq bo'lsin** — bulut serveri SMS/kodni kirita olmaydi. Xohlasangiz faqat build uchun alohida akkaunt oching.
2. GitHub (brauzerda "Desktop site") → repozitoriy → **Settings → Secrets and variables → Actions → New repository secret**:

| Secret | Qiymat |
|---|---|
| `UNITY_EMAIL` | Unity akkaunt emaili |
| `UNITY_PASSWORD` | Unity akkaunt paroli |

Token/parollar kodga yozilmaydi; workflow ularni faqat secrets'dan oladi va logda yashiradi.

Ixtiyoriy: Plus/Pro uchun `UNITY_SERIAL`; Enterprise `.ulf` fayli bo'lsa `UNITY_LICENSE` (bu holda fayl ishlatiladi).
`.devcontainer/unity-license` (Codespaces + Unity Hub) endi shart emas — Unity Personal uchun `.ulf` olishni to'xtatgan.

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

## 4. Doimiy imzo kaliti (yangi versiya eskisining ustiga o'rnatilishi uchun)

Har bir build vaqtinchalik debug kalit bilan imzolansa, Android yangi versiyani eskisining ustiga o'rnatmaydi
(avval o'chirish kerak bo'ladi). Doimiy kalit uchun **bitta secret** yetarli:

| Secret | Qiymat |
|---|---|
| `ANDROID_KEYSTORE_PASS` | O'zingiz o'ylab topgan parol, **kamida 16 belgi** (harf + raqam). Uni xavfsiz joyda saqlang. |

Birinchi build kalitni o'zi yaratadi va repozitoriyga faqat shu parol bilan **shifrlangan** holda yozadi
(`signing/orol-release.p12.enc`, AES-256, PBKDF2 600 000 iteratsiya). Keyingi barcha buildlar shu kalitdan foydalanadi.
APK v1 + v2 + v3 sxemalarida imzolanadi (fayl menejerlari "imzo xatosi" ko'rsatmaydi).

- Parolni yo'qotsangiz yoki o'zgartirsangiz — kalitni ochib bo'lmaydi: `signing/orol-release.p12.enc` ni o'chiring,
  yangi kalit yaratiladi va telefonda ilovani bir marta qayta o'rnatish kerak bo'ladi.
- Google Play uchun keyinchalik AAB + Play App Signing (alohida upload kaliti) ishlatiladi.

## 5. Litsenziyasiz tez tekshiruv

**Unity Compile Check** workflow (har push'da) loyihaning barcha C# assembly'larini Unity 6.3 ning o'z Roslyn kompilyatori va
haqiqiy UnityEngine/UnityEditor reference DLL'lariga qarshi kompilyatsiya qiladi — litsenziya talab qilinmaydi.
U shader va IL2CPP'ni tekshirmaydi; buni faqat haqiqiy Unity build qiladi.

## 6. Muammolar (troubleshooting)

| Belgi | Sabab / yechim |
|---|---|
| `Unity license is not configured` | Secrets qo'shilmagan yoki nomi xato (yuqoridagi jadval). |
| `No valid Unity Editor license found` / activation xatosi | Email/parol xato, akkauntda 2FA yoqilgan yoki Personal o'rinlar band (boshqa joyda faollashtirilgan Unity'lar). id.unity.com da 2FA ni o'chiring, parolni tekshiring; *Subscriptions/Seats* da eski aktivatsiyalarni bo'shating. |
| `No space left on device` | Workflow disk tozalaydi; baribir bo'lsa Library keshini o'chiring (*Actions → Caches*). |
| `Project validation failed` | Build oldidan `ProjectValidator` ishlaydi: log'da `[Validate]` qatorlarini qidiring (shader xatosi, missing script, yo'q resurs). |
| Shader xatosi (`Shader error OROL/...`) | Log'da qator raqami bor. O'yin baribir URP fallback materiallariga o'tadi, lekin xatoni tuzatish kerak. |
| `Build failed` | *Build with Unity* qadamida `error CS` (C# xatosi) yoki `[Build]` qatorlarini qidiring. |
| Gradle/SDK xatolari | Unity versiyasi `ProjectSettings/ProjectVersion.txt` da. Boshqa versiyaga o'tishda GameCI'da `ubuntu-<versiya>-android-3` image mavjudligini tekshiring. |
| APK o'rnatilmaydi ("App not installed") yoki o'rnatish osilib qoladi | Eski versiya boshqa kalit bilan imzolangan: eskisini o'chirib qayta o'rnating. Doimiy kalit uchun 4-bo'lim. |

Unity versiyasini yangilash: `UnityClient/ProjectSettings/ProjectVersion.txt` va `Packages/manifest.json` dagi paket versiyalari.
