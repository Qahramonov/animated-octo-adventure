using System.Collections.Generic;

namespace Survival.Client
{
    /// <summary>
    /// Minimal localization (Uzbek / English / Russian). Keys fall back to English, then to the key.
    /// Uzbek uses the ASCII apostrophe (o', g') so every UI font can render it.
    /// </summary>
    public static class Loc
    {
        public static string Language = "uz";

        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // key                       en                               uz                                  ru
            { "play",               new[] { "PLAY",                         "O'YNASH",                          "ИГРАТЬ" } },
            { "servers",            new[] { "SERVERS",                      "SERVERLAR",                        "СЕРВЕРЫ" } },
            { "settings",           new[] { "SETTINGS",                     "SOZLAMALAR",                       "НАСТРОЙКИ" } },
            { "profile",            new[] { "PROFILE",                      "PROFIL",                           "ПРОФИЛЬ" } },
            { "inventory",          new[] { "INVENTORY",                    "INVENTAR",                         "ИНВЕНТАРЬ" } },
            { "exit",               new[] { "EXIT",                         "CHIQISH",                          "ВЫХОД" } },
            { "skip",               new[] { "SKIP",                         "O'TKAZISH",                        "ПРОПУСТИТЬ" } },
            { "loading",            new[] { "Loading",                      "Yuklanmoqda",                      "Загрузка" } },
            { "join",               new[] { "JOIN",                         "KIRISH",                           "ВОЙТИ" } },
            { "refresh",            new[] { "REFRESH",                      "YANGILASH",                        "ОБНОВИТЬ" } },
            { "online",             new[] { "Online",                       "Onlayn",                           "Онлайн" } },
            { "offline",            new[] { "Offline",                      "Oflayn",                           "Офлайн" } },
            { "full",               new[] { "Full",                         "To'la",                            "Заполнен" } },
            { "empty",              new[] { "Empty",                        "Bo'sh",                            "Пусто" } },
            { "checking",           new[] { "Checking...",                  "Tekshirilmoqda...",                "Проверка..." } },
            { "not_deployed",       new[] { "Not deployed yet",             "Hali joylashtirilmagan",           "Ещё не развёрнут" } },
            { "players",            new[] { "Players",                      "O'yinchilar",                      "Игроки" } },
            { "bots",               new[] { "bots",                         "bot",                              "ботов" } },
            { "ping",               new[] { "Ping",                         "Ping",                             "Пинг" } },
            { "region",             new[] { "Region",                       "Hudud",                            "Регион" } },
            { "connecting",         new[] { "Connecting",                   "Ulanmoqda",                        "Подключение" } },
            { "authenticating",     new[] { "Authenticating",               "Tekshirilmoqda",                   "Авторизация" } },
            { "loading_island",     new[] { "Loading island",               "Orol yuklanmoqda",                 "Загрузка острова" } },
            { "spawning",           new[] { "Spawning",                     "Paydo bo'linmoqda",                "Появление" } },
            { "cancel",             new[] { "CANCEL",                       "BEKOR QILISH",                     "ОТМЕНА" } },
            { "retry",              new[] { "RETRY",                        "QAYTA URINISH",                    "ПОВТОРИТЬ" } },
            { "close",              new[] { "CLOSE",                        "YOPISH",                           "ЗАКРЫТЬ" } },
            { "back",               new[] { "BACK",                         "ORQAGA",                           "НАЗАД" } },
            { "save",               new[] { "SAVE",                         "SAQLASH",                          "СОХРАНИТЬ" } },
            { "reset",              new[] { "RESET",                        "TIKLASH",                          "СБРОС" } },
            { "offline_practice",   new[] { "OFFLINE PRACTICE",             "OFLAYN MASHQ",                     "ОФЛАЙН ТРЕНИРОВКА" } },
            { "offline_note",       new[] { "Local server on this phone. Not online multiplayer.", "Shu telefondagi lokal server. Bu onlayn multiplayer emas.", "Локальный сервер на этом телефоне. Не онлайн-мультиплеер." } },
            { "host_lan",           new[] { "HOST ON WI-FI (LAN)",          "WI-FI ORQALI HOST (LAN)",          "ХОСТ ПО WI-FI (LAN)" } },
            { "lan_note",           new[] { "Other phones on the same Wi-Fi can join this game.", "Shu Wi-Fi'dagi boshqa telefonlar bu o'yinga qo'shila oladi.", "Другие телефоны в той же сети Wi-Fi смогут подключиться." } },
            { "find_lan",           new[] { "FIND LAN GAMES",               "LAN O'YINLARNI TOPISH",            "НАЙТИ LAN ИГРЫ" } },
            { "add_server",         new[] { "ADD SERVER",                   "SERVER QO'SHISH",                  "ДОБАВИТЬ СЕРВЕР" } },
            { "server_full_msg",    new[] { "The server is full. Try again later.", "Server to'la. Keyinroq urinib ko'ring.", "Сервер заполнен. Попробуйте позже." } },
            { "server_offline_msg", new[] { "The server is offline or unreachable.", "Server oflayn yoki unga ulanib bo'lmadi.", "Сервер офлайн или недоступен." } },
            { "conn_lost",          new[] { "Connection lost - reconnecting", "Aloqa uzildi - qayta ulanmoqda", "Связь потеряна - переподключение" } },
            { "graphics",           new[] { "GRAPHICS",                     "GRAFIKA",                          "ГРАФИКА" } },
            { "controls",           new[] { "CONTROLS",                     "BOSHQARUV",                        "УПРАВЛЕНИЕ" } },
            { "audio",              new[] { "AUDIO",                        "OVOZ",                             "ЗВУК" } },
            { "general",            new[] { "GENERAL",                      "UMUMIY",                           "ОБЩИЕ" } },
            { "quality",            new[] { "Quality",                      "Sifat",                            "Качество" } },
            { "fps_limit",          new[] { "FPS limit",                    "FPS chegarasi",                    "Лимит FPS" } },
            { "render_scale",       new[] { "Render scale",                 "Render miqyosi",                   "Масштаб рендера" } },
            { "view_distance",      new[] { "View distance",                "Ko'rish masofasi",                 "Дальность обзора" } },
            { "shadows",            new[] { "Shadows",                      "Soyalar",                          "Тени" } },
            { "post_fx",            new[] { "Post effects (bloom)",         "Post-effektlar (bloom)",           "Пост-эффекты (bloom)" } },
            { "vegetation",         new[] { "Vegetation density",           "O'simliklar zichligi",             "Плотность растений" } },
            { "sensitivity",        new[] { "Look sensitivity",             "Qarash sezgirligi",                "Чувствительность" } },
            { "invert_y",           new[] { "Invert Y",                     "Y o'qini teskari",                 "Инверсия Y" } },
            { "joystick_size",      new[] { "Joystick size",                "Joystik o'lchami",                 "Размер джойстика" } },
            { "button_opacity",     new[] { "Button opacity",               "Tugmalar shaffofligi",             "Прозрачность кнопок" } },
            { "auto_sprint",        new[] { "Sprint when stick at edge",    "Joystik chetida yugurish",         "Бег при упоре стика" } },
            { "edit_layout",        new[] { "EDIT BUTTON LAYOUT",           "TUGMALAR JOYLASHUVI",              "РАСКЛАДКА КНОПОК" } },
            { "master_volume",      new[] { "Master volume",                "Umumiy ovoz",                      "Общая громкость" } },
            { "music_volume",       new[] { "Ambience volume",              "Atmosfera ovozi",                  "Громкость окружения" } },
            { "sfx_volume",         new[] { "Effects volume",               "Effektlar ovozi",                  "Громкость эффектов" } },
            { "language",           new[] { "Language",                     "Til",                              "Язык" } },
            { "show_fps",           new[] { "Show FPS / ping",              "FPS / ping ko'rsatish",            "Показывать FPS / пинг" } },
            { "offline_bots",       new[] { "Bots in offline practice",     "Oflayn mashqda botlar",            "Боты в офлайн режиме" } },
            { "nickname",           new[] { "Nickname",                     "Taxallus",                         "Никнейм" } },
            { "player_id",          new[] { "Player ID",                    "O'yinchi ID",                      "ID игрока" } },
            { "sessions",           new[] { "Sessions",                     "Seanslar",                         "Сеансы" } },
            { "play_time",          new[] { "Play time",                    "O'yin vaqti",                      "Время в игре" } },
            { "deaths",             new[] { "Deaths",                       "O'limlar",                         "Смерти" } },
            { "inventory_empty",    new[] { "Your survivor's inventory is stored on the server. Items, gathering and crafting arrive in Phase 2-3; the last synced state is shown here.", "Inventar serverda saqlanadi. Buyumlar, yig'ish va craft 2-3-bosqichlarda qo'shiladi; bu yerda oxirgi sinxron holat ko'rsatiladi.", "Инвентарь хранится на сервере. Предметы, сбор и крафт появятся в фазах 2-3; здесь показано последнее синхронизированное состояние." } },
            { "last_state",         new[] { "Last known state",             "Oxirgi holat",                     "Последнее состояние" } },
            { "health",             new[] { "Health",                       "Salomatlik",                       "Здоровье" } },
            { "hunger",             new[] { "Food",                         "Ovqat",                            "Еда" } },
            { "thirst",             new[] { "Water",                        "Suv",                              "Вода" } },
            { "stamina",            new[] { "Stamina",                      "Chidamlilik",                      "Выносливость" } },
            { "resume",             new[] { "RESUME",                       "DAVOM ETISH",                      "ПРОДОЛЖИТЬ" } },
            { "leave",              new[] { "LEAVE SERVER",                 "SERVERDAN CHIQISH",                "ПОКИНУТЬ СЕРВЕР" } },
            { "you_died",           new[] { "YOU DIED",                     "SIZ HALOK BO'LDINGIZ",             "ВЫ ПОГИБЛИ" } },
            { "respawn",            new[] { "RESPAWN",                      "QAYTA TUG'ILISH",                  "ВОЗРОДИТЬСЯ" } },
            { "exit_confirm",       new[] { "Quit the game?",               "O'yindan chiqasizmi?",             "Выйти из игры?" } },
            { "yes",                new[] { "YES",                          "HA",                               "ДА" } },
            { "no",                 new[] { "NO",                           "YO'Q",                             "НЕТ" } },
            { "select_server",      new[] { "Select a server",              "Server tanlang",                   "Выберите сервер" } },
            { "version_mismatch",   new[] { "Different game version",       "Boshqa o'yin versiyasi",           "Другая версия игры" } },
            { "drag_to_move",       new[] { "Drag buttons to move them. Pinch slider changes size.", "Tugmalarni surib joylashtiring. Slayder o'lchamni o'zgartiradi.", "Перетаскивайте кнопки. Ползунок меняет размер." } },
            { "done",               new[] { "DONE",                         "TAYYOR",                           "ГОТОВО" } },
            { "jump",               new[] { "JUMP",                         "SAKRASH",                          "ПРЫЖОК" } },
            { "crouch",             new[] { "CROUCH",                       "CHO'KKALASH",                      "ПРИСЕСТЬ" } },
            { "sprint",             new[] { "SPRINT",                       "YUGURISH",                         "БЕГ" } },
            { "menu",               new[] { "MENU",                         "MENYU",                            "МЕНЮ" } },
            { "world_mismatch",     new[] { "World data mismatch: update the game", "Dunyo ma'lumotlari mos emas: o'yinni yangilang", "Несовпадение мира: обновите игру" } },
        };

        public static string T(string key)
        {
            if (!Table.TryGetValue(key, out string[] v)) return key;
            int i = Language == "uz" ? 1 : Language == "ru" ? 2 : 0;
            return string.IsNullOrEmpty(v[i]) ? v[0] : v[i];
        }
    }
}
